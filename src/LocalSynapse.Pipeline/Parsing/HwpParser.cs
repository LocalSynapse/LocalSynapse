using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using LocalSynapse.Core.Diagnostics;
using LocalSynapse.Pipeline.Interfaces;
using OpenMcdf;

namespace LocalSynapse.Pipeline.Parsing;

/// <summary>
/// HWP v5 파서. OpenMcdf로 OLE compound file을 열어 BodyText에서 텍스트를 추출한다.
/// </summary>
internal static class HwpParser
{
    private const ushort HWPTAG_PARA_TEXT = 67;

    /// <summary>HWP 파일에서 텍스트를 추출한다.</summary>
    public static ExtractionResult Parse(string filePath)
    {
        long sizeBytes = -1;
        try { sizeBytes = new FileInfo(filePath).Length; }
        catch (Exception sEx) { Debug.WriteLine($"[HwpParser] Size probe: {sEx.Message}"); }
        try
        {
            var openSw = Stopwatch.StartNew();
            using var cf = new CompoundFile(filePath);
            openSw.Stop();
            SpeedDiagLog.Log("PARSE_DETAIL",
                "ext", ".hwp", "stage", "open",
                "time_ms", openSw.ElapsedMilliseconds, "size_bytes", sizeBytes);

            // PrvText 스트림 시도 (미리보기 텍스트)
            var prvSw = Stopwatch.StartNew();
            var prvText = TryReadPrvText(cf);
            prvSw.Stop();
            if (!string.IsNullOrWhiteSpace(prvText))
            {
                SpeedDiagLog.Log("PARSE_DETAIL",
                    "ext", ".hwp", "stage", "prvtext",
                    "time_ms", prvSw.ElapsedMilliseconds);
                return ExtractionResult.Ok(prvText);
            }

            // BodyText 스토리지에서 추출
            var sectionsSw = Stopwatch.StartNew();
            var bodyText = ExtractFromBodyText(cf);
            sectionsSw.Stop();
            SpeedDiagLog.Log("PARSE_DETAIL",
                "ext", ".hwp", "stage", "sections",
                "time_ms", sectionsSw.ElapsedMilliseconds);
            return ExtractionResult.Ok(bodyText);
        }
        catch (CFItemNotFound)
        {
            Debug.WriteLine($"[HwpParser] No BodyText/PrvText stream: {filePath}");
            return ExtractionResult.Ok("");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HwpParser] Failed: {filePath} - {ex.Message}");
            return ExtractionResult.Fail("PARSE_ERROR", ex.Message);
        }
    }

    private static string? TryReadPrvText(CompoundFile cf)
    {
        try
        {
            var stream = cf.RootStorage.GetStream("PrvText");
            var data = stream.GetData();
            if (data.Length > 0)
                return Encoding.Unicode.GetString(data).Trim('\0').Trim()
                    .Replace("<", " ").Replace(">", " ");
        }
        catch (CFItemNotFound) { Debug.WriteLine("[HwpParser] PrvText stream not found"); }
        return null;
    }

    private static string ExtractFromBodyText(CompoundFile cf)
    {
        // Check compression flag from FileHeader
        bool compressed;
        try
        {
            var header = cf.RootStorage.GetStream("FileHeader").GetData();
            compressed = header.Length > 36 && (header[36] & 0x01) != 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HwpParser] FileHeader read failed, assuming compressed: {ex.Message}");
            compressed = true;
        }

        var bodyStorage = cf.RootStorage.GetStorage("BodyText");
        var sb = new StringBuilder();

        for (int i = 0; ; i++)
        {
            CFStream? section;
            try { section = bodyStorage.GetStream($"Section{i}"); }
            catch (CFItemNotFound) { break; }

            var raw = section.GetData();
            var data = compressed ? Decompress(raw) : raw;
            if (data == null) continue;

            ParseRecords(data, sb);
        }

        return sb.ToString();
    }

    private static byte[]? Decompress(byte[] raw)
    {
        // Raw deflate (no zlib header)
        try
        {
            using var input = new MemoryStream(raw);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return output.ToArray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HwpParser] Deflate failed, trying zlib offset: {ex.Message}");
            // Fallback: skip 2-byte zlib header
            if (raw.Length <= 2) return null;
            try
            {
                using var input = new MemoryStream(raw, 2, raw.Length - 2);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                deflate.CopyTo(output);
                return output.ToArray();
            }
            catch (Exception ex2)
            {
                Debug.WriteLine($"[HwpParser] Decompression failed: {ex2.Message}");
                return null;
            }
        }
    }

    private static void ParseRecords(byte[] data, StringBuilder sb)
    {
        int pos = 0;
        while (pos + 4 <= data.Length)
        {
            uint header = BitConverter.ToUInt32(data, pos);
            ushort tagId = (ushort)(header & 0x3FF);
            int size = (int)((header >> 20) & 0xFFF);
            pos += 4;

            if (size == 0xFFF)
            {
                if (pos + 4 > data.Length) break;
                size = (int)BitConverter.ToUInt32(data, pos);
                pos += 4;
            }

            if (pos + size > data.Length) break;

            if (tagId == HWPTAG_PARA_TEXT)
            {
                ExtractParaText(data, pos, size, sb);
            }

            pos += size;
        }
    }

    private static void ExtractParaText(byte[] data, int offset, int size, StringBuilder sb)
    {
        int end = offset + size;
        int i = offset;

        while (i + 1 < end)
        {
            ushort ch = BitConverter.ToUInt16(data, i);
            i += 2;

            switch (ch)
            {
                case <= 0x0001:
                    i += 12; // inline extended control (6 wchars)
                    break;
                case >= 0x0002 and <= 0x0008:
                    i += 8; // extended control (4 wchars)
                    break;
                case 0x0009:
                    sb.Append('\t');
                    break;
                case 0x000A:
                    sb.Append('\n');
                    break;
                case 0x000D:
                    sb.Append('\n');
                    break;
                case >= 0x000B and <= 0x001F:
                    break; // other controls, skip
                default:
                    sb.Append((char)ch);
                    break;
            }
        }
    }
}
