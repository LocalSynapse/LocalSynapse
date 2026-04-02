using System.Security.Cryptography;
using System.Text;
using LocalSynapse.Pipeline.Interfaces;

namespace LocalSynapse.Pipeline.Chunking;

/// <summary>
/// 텍스트를 문단 경계 기준으로 1000자 청크로 분할한다.
/// 파일당 최대 500개 청크.
/// </summary>
public sealed class TextChunker : ITextChunker
{
    /// <summary>최대 청크 크기 (문자 수).</summary>
    public const int MaxChunkSize = 1000;

    /// <summary>파일당 최대 청크 수.</summary>
    public const int MaxChunksPerFile = 500;

    private static readonly string[] ParagraphSeparators = { "\r\n\r\n", "\n\n" };

    /// <summary>텍스트를 문단 경계 기준으로 분할한다.</summary>
    public IReadOnlyList<TextChunk> Chunk(string text, string sourceType = "text", string? originMeta = null)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var paragraphs = text.Split(ParagraphSeparators, StringSplitOptions.None);
        var chunks = new List<TextChunk>();
        var current = new StringBuilder();
        var offset = 0;
        var chunkStart = 0;

        foreach (var para in paragraphs)
        {
            if (chunks.Count >= MaxChunksPerFile)
                break;

            // If adding this paragraph would exceed limit, flush current
            if (current.Length > 0 && current.Length + para.Length + 2 > MaxChunkSize)
            {
                AddChunk(chunks, current.ToString(), chunkStart, sourceType, originMeta);
                current.Clear();
                chunkStart = offset;

                if (chunks.Count >= MaxChunksPerFile)
                    break;
            }

            // Single paragraph exceeding MaxChunkSize → force split
            if (para.Length > MaxChunkSize)
            {
                // Flush what we have first
                if (current.Length > 0)
                {
                    AddChunk(chunks, current.ToString(), chunkStart, sourceType, originMeta);
                    current.Clear();

                    if (chunks.Count >= MaxChunksPerFile)
                        break;
                }

                chunkStart = offset;
                for (int i = 0; i < para.Length && chunks.Count < MaxChunksPerFile; i += MaxChunkSize)
                {
                    var len = Math.Min(MaxChunkSize, para.Length - i);
                    var slice = para.Substring(i, len);
                    AddChunk(chunks, slice, offset + i, sourceType, originMeta);
                }

                offset += para.Length + 2; // account for separator
                chunkStart = offset;
                continue;
            }

            // Accumulate
            if (current.Length > 0)
                current.Append("\n\n");
            else
                chunkStart = offset;

            current.Append(para);
            offset += para.Length + 2; // account for separator between paragraphs
        }

        // Flush remaining
        if (current.Length > 0 && chunks.Count < MaxChunksPerFile)
        {
            AddChunk(chunks, current.ToString(), chunkStart, sourceType, originMeta);
        }

        return chunks;
    }

    private static void AddChunk(List<TextChunk> chunks, string text, int startOffset,
        string sourceType, string? originMeta)
    {
        chunks.Add(new TextChunk
        {
            Text = text,
            ContentHash = ComputeHash(text),
            StartOffset = startOffset,
            EndOffset = startOffset + text.Length,
            SourceType = sourceType,
            OriginMeta = originMeta
        });
    }

    private static string ComputeHash(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
