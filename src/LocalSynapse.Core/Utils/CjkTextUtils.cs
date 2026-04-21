using System.Collections.Generic;
using System.Text;

namespace LocalSynapse.Core.Utils;

/// <summary>
/// CJK 텍스트 bigram 분할 유틸리티.
/// FTS5 인덱싱 전처리 및 쿼리 변환에 사용.
/// </summary>
public static class CjkTextUtils
{
    /// <summary>CJK Unified Ideographs 범위 문자인지 확인한다 (U+3400-U+4DBF, U+4E00-U+9FFF). 한국어 제외.</summary>
    public static bool IsCjk(char c)
    {
        return (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF);
    }

    /// <summary>CJK 또는 한국어 문자인지 확인한다. IsWordBoundaryMatch용.</summary>
    public static bool IsCjkOrKorean(char c)
    {
        return IsCjk(c) || IsKorean(c);
    }

    /// <summary>한국어 문자인지 확인한다.</summary>
    public static bool IsKorean(char c)
    {
        return (c >= 0xAC00 && c <= 0xD7A3) || (c >= 0x3131 && c <= 0x318E);
    }

    /// <summary>
    /// 텍스트 내 연속 CJK 구간을 bigram으로 분할하여 공백 삽입한 결과를 반환한다.
    /// 비CJK 구간(한국어 포함)은 그대로 유지.
    /// CJK 1글자 단독인 경우 원문 그대로 유지 (bigram 불가).
    /// </summary>
    public static string ApplyBigramSplit(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Quick check: any CJK at all?
        bool hasCjk = false;
        foreach (var c in text)
        {
            if (IsCjk(c)) { hasCjk = true; break; }
        }
        if (!hasCjk) return text;

        var sb = new StringBuilder(text.Length * 2);
        int i = 0;

        while (i < text.Length)
        {
            if (IsCjk(text[i]))
            {
                int start = i;
                while (i < text.Length && IsCjk(text[i]))
                    i++;
                int cjkLen = i - start;

                if (cjkLen == 1)
                {
                    sb.Append(text[start]);
                }
                else
                {
                    for (int j = start; j < i - 1; j++)
                    {
                        if (j > start) sb.Append(' ');
                        sb.Append(text[j]);
                        sb.Append(text[j + 1]);
                    }
                }
            }
            else
            {
                sb.Append(text[i]);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>CJK 텍스트에서 bigram 리스트를 추출한다 (쿼리 변환용).</summary>
    public static List<string> ExtractBigrams(string text)
    {
        var results = new List<string>();
        for (int i = 0; i < text.Length - 1; i++)
        {
            if (IsCjk(text[i]) && IsCjk(text[i + 1]))
                results.Add(text.Substring(i, 2));
        }
        return results;
    }
}
