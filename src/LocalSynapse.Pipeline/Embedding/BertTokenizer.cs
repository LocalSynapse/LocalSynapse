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

        // Fallback: BgeM3Installer stores files in onnx/ subdirectory
        var onnxDir = Path.Combine(modelDir, "onnx");
        var onnxTokenizerPath = Path.Combine(onnxDir, "tokenizer.json");
        var onnxVocabPath = Path.Combine(onnxDir, "vocab.txt");

        if (File.Exists(tokenizerPath))
        {
            await LoadTokenizerJsonAsync(tokenizerPath, ct);
        }
        else if (File.Exists(onnxTokenizerPath))
        {
            await LoadTokenizerJsonAsync(onnxTokenizerPath, ct);
        }
        else if (File.Exists(vocabPath))
        {
            await LoadVocabTxtAsync(vocabPath, ct);
        }
        else if (File.Exists(onnxVocabPath))
        {
            await LoadVocabTxtAsync(onnxVocabPath, ct);
        }
        else
        {
            throw new FileNotFoundException(
                $"No tokenizer.json or vocab.txt found in {modelDir} or {onnxDir}");
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

        var seqLen = tokens.Count + 2; // [CLS] + actual tokens + [SEP]
        var ids = new long[seqLen];
        var mask = new long[seqLen];

        ids[0] = _clsId;
        mask[0] = 1;

        for (int i = 0; i < tokens.Count; i++)
        {
            ids[i + 1] = _vocab.GetValueOrDefault(tokens[i], _unkId);
            mask[i + 1] = 1;
        }

        ids[tokens.Count + 1] = _sepId;
        mask[tokens.Count + 1] = 1;

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

        if (root.TryGetProperty("model", out var model) && model.TryGetProperty("vocab", out var vocab))
        {
            if (vocab.ValueKind == JsonValueKind.Object)
            {
                // WordPiece/BPE format: {"token": id, ...}
                foreach (var prop in vocab.EnumerateObject())
                    _vocab[prop.Name] = prop.Value.GetInt32();
            }
            else if (vocab.ValueKind == JsonValueKind.Array)
            {
                // Unigram (SentencePiece) format: [["token", score], ...]
                // Index in array = token ID
                int idx = 0;
                foreach (var entry in vocab.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.Array && entry.GetArrayLength() >= 1)
                    {
                        var token = entry[0].GetString();
                        if (token != null)
                            _vocab[token] = idx;
                    }
                    idx++;
                }
            }
        }

        // Add added_tokens (overrides vocab entries if present)
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
