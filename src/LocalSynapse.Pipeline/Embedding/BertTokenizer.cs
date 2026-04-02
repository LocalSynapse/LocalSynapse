using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalSynapse.Pipeline.Embedding;

/// <summary>
/// WordPiece 토크나이저. vocab.txt 또는 tokenizer.json을 로드하여 텍스트를 토큰 ID 시퀀스로 변환한다.
/// </summary>
public sealed partial class BertTokenizer
{
    private Dictionary<string, int> _vocab = new();
    private int _clsId;
    private int _sepId;
    private int _padId;
    private int _unkId;

    /// <summary>로드 완료 여부.</summary>
    public bool IsLoaded => _vocab.Count > 0;

    /// <summary>모델 디렉토리에서 어휘 사전을 로드한다.</summary>
    public async Task LoadAsync(string modelDir, CancellationToken ct = default)
    {
        var tokenizerPath = Path.Combine(modelDir, "tokenizer.json");
        var vocabPath = Path.Combine(modelDir, "vocab.txt");

        if (File.Exists(tokenizerPath))
        {
            await LoadTokenizerJsonAsync(tokenizerPath, ct);
        }
        else if (File.Exists(vocabPath))
        {
            await LoadVocabTxtAsync(vocabPath, ct);
        }
        else
        {
            throw new FileNotFoundException($"No tokenizer.json or vocab.txt found in {modelDir}");
        }

        // Resolve special tokens
        _clsId = ResolveSpecialToken("[CLS]", "<s>", "<bos>");
        _sepId = ResolveSpecialToken("[SEP]", "</s>", "<eos>");
        _padId = ResolveSpecialToken("[PAD]", "<pad>");
        _unkId = ResolveSpecialToken("[UNK]", "<unk>");

        Debug.WriteLine($"[BertTokenizer] Loaded {_vocab.Count} tokens. CLS={_clsId} SEP={_sepId} PAD={_padId} UNK={_unkId}");
    }

    /// <summary>텍스트를 토큰 ID와 attention mask로 인코딩한다.</summary>
    public (long[] InputIds, long[] AttentionMask) Encode(string text, int maxLength = 8192)
    {
        var tokens = Tokenize(text);
        var maxTokens = maxLength - 2; // Reserve for [CLS] and [SEP]
        if (tokens.Count > maxTokens)
            tokens = tokens.GetRange(0, maxTokens);

        var ids = new long[maxLength];
        var mask = new long[maxLength];

        ids[0] = _clsId;
        mask[0] = 1;

        for (int i = 0; i < tokens.Count; i++)
        {
            ids[i + 1] = _vocab.GetValueOrDefault(tokens[i], _unkId);
            mask[i + 1] = 1;
        }

        ids[tokens.Count + 1] = _sepId;
        mask[tokens.Count + 1] = 1;

        // Remaining positions already 0 (PAD)
        for (int i = tokens.Count + 2; i < maxLength; i++)
            ids[i] = _padId;

        return (ids, mask);
    }

    private List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var words = TokenizeRegex().Split(text.ToLowerInvariant())
            .Where(w => !string.IsNullOrWhiteSpace(w));

        foreach (var word in words)
        {
            WordPieceTokenize(word, tokens);
        }
        return tokens;
    }

    private void WordPieceTokenize(string word, List<string> output)
    {
        int start = 0;
        while (start < word.Length)
        {
            string? bestMatch = null;
            int bestEnd = start;

            for (int end = word.Length; end > start; end--)
            {
                var sub = start == 0 ? word[start..end] : "##" + word[start..end];
                if (_vocab.ContainsKey(sub))
                {
                    bestMatch = sub;
                    bestEnd = end;
                    break;
                }
            }

            if (bestMatch == null)
            {
                output.Add(start == 0 ? word[start].ToString() : "##" + word[start]);
                start++;
            }
            else
            {
                output.Add(bestMatch);
                start = bestEnd;
            }
        }
    }

    private async Task LoadTokenizerJsonAsync(string path, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Try model.vocab (object format)
        if (root.TryGetProperty("model", out var model) && model.TryGetProperty("vocab", out var vocab))
        {
            foreach (var prop in vocab.EnumerateObject())
                _vocab[prop.Name] = prop.Value.GetInt32();
        }

        // Add added_tokens
        if (root.TryGetProperty("added_tokens", out var addedTokens))
        {
            foreach (var token in addedTokens.EnumerateArray())
            {
                if (token.TryGetProperty("content", out var content) && token.TryGetProperty("id", out var id))
                    _vocab[content.GetString()!] = id.GetInt32();
            }
        }
    }

    private async Task LoadVocabTxtAsync(string path, CancellationToken ct)
    {
        var lines = await File.ReadAllLinesAsync(path, ct);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!string.IsNullOrEmpty(line))
                _vocab[line] = i;
        }
    }

    private int ResolveSpecialToken(params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (_vocab.TryGetValue(alias, out var id))
                return id;
        }
        return 0;
    }

    [GeneratedRegex(@"[\s\p{P}]+")]
    private static partial Regex TokenizeRegex();
}
