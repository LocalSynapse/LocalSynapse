using Xunit;
using LocalSynapse.Search.Services;

namespace LocalSynapse.Search.Tests;

public sealed class NaturalQueryParserTest
{
    // ── Stop word 제거 ──

    [Fact]
    public void RemoveStopwords_RemovesEnglishStopWords()
    {
        var result = NaturalQueryParser.RemoveStopwords("how to configure the network settings");
        Assert.DoesNotContain("how", result.Split(' '));
        Assert.DoesNotContain("to", result.Split(' '));
        Assert.DoesNotContain("the", result.Split(' '));
        Assert.Contains("configure", result.Split(' '));
        Assert.Contains("network", result.Split(' '));
        Assert.Contains("settings", result.Split(' '));
    }

    [Fact]
    public void RemoveStopwords_RemovesKoreanStopWords()
    {
        var result = NaturalQueryParser.RemoveStopwords("계약서를 작성하고");
        Assert.DoesNotContain("를", result.Split(' '));
        Assert.Contains("계약서", result.Split(' '));
    }

    [Fact]
    public void RemoveStopwords_PreservesContentWords()
    {
        var result = NaturalQueryParser.RemoveStopwords("budget report");
        Assert.Equal("budget report", result);
    }

    // ── FTS5 쿼리 생성 ──

    [Fact]
    public void ToFts5Query_ShortToken_NoWildcard()
    {
        // 4자 미만 영어 토큰은 와일드카드 없음
        var result = NaturalQueryParser.ToFts5Query("run");
        Assert.Contains("\"run\"", result);
        Assert.DoesNotContain("\"run\"*", result);
    }

    [Fact]
    public void ToFts5Query_LongToken_HasWildcard()
    {
        // 4자 이상 영어 토큰은 와일드카드 추가
        var result = NaturalQueryParser.ToFts5Query("budget");
        Assert.Contains("\"budget\"*", result);
    }

    [Fact]
    public void ToFts5Query_Korean_HasWildcard()
    {
        var result = NaturalQueryParser.ToFts5Query("계약서");
        Assert.Contains("\"계약서\"*", result);
    }

    [Fact]
    public void ToFts5Query_StopWordsFiltered()
    {
        var result = NaturalQueryParser.ToFts5Query("the budget report");
        Assert.DoesNotContain("\"the\"", result);
        Assert.Contains("\"budget\"*", result);
        Assert.Contains("\"report\"*", result);
    }

    [Fact]
    public void ToFts5Query_AllStopWords_ReturnsEmpty()
    {
        var result = NaturalQueryParser.ToFts5Query("the a an is are");
        Assert.Equal("", result);
    }

    // ── Stem ──

    [Fact]
    public void Stem_EnglishWord_ReturnsStem()
    {
        Assert.Equal("document", NaturalQueryParser.Stem("documents"));
        Assert.Equal("run", NaturalQueryParser.Stem("running"));
        Assert.Equal("configur", NaturalQueryParser.Stem("configuration"));
    }

    [Fact]
    public void Stem_KoreanWord_ReturnsUnchanged()
    {
        Assert.Equal("계약서", NaturalQueryParser.Stem("계약서"));
    }

    // ── 하이픈/컴파운드 확장 ──

    [Fact]
    public void ExpandHyphenVariants_HyphenatedWord_ProducesJoined()
    {
        // "e-mail" → Tokenize splits to "e", "mail" but original has hyphen
        // Rule 1 should produce "email" from the original hyphenated word
        var results = NaturalQueryParser.ExpandHyphenVariants("e-mail");
        Assert.Contains("email", results);
    }

    [Fact]
    public void ExpandHyphenVariants_CompoundWord_ProducesRemainder()
    {
        // "reindex" → prefix "re" + remainder "index"
        var results = NaturalQueryParser.ExpandHyphenVariants("reindex");
        Assert.Contains("index", results);
    }

    [Fact]
    public void ExpandHyphenVariants_PrePrefix_ProducesRemainder()
    {
        var results = NaturalQueryParser.ExpandHyphenVariants("preconfigure");
        Assert.Contains("configure", results);
    }

    [Fact]
    public void ExpandHyphenVariants_UnPrefix_ProducesRemainder()
    {
        var results = NaturalQueryParser.ExpandHyphenVariants("uninstall");
        Assert.Contains("install", results);
    }

    [Fact]
    public void ExpandHyphenVariants_ShortWord_NoExpansion()
    {
        // 4자 미만 → 확장 없음
        var results = NaturalQueryParser.ExpandHyphenVariants("run");
        Assert.Empty(results);
    }

    [Fact]
    public void ExpandHyphenVariants_Korean_NoExpansion()
    {
        var results = NaturalQueryParser.ExpandHyphenVariants("계약서");
        Assert.Empty(results);
    }

    [Fact]
    public void ExpandHyphenVariants_NoMatch_EmptyResult()
    {
        // "budget" → prefix 매칭 없음
        var results = NaturalQueryParser.ExpandHyphenVariants("budget");
        Assert.Empty(results);
    }

    [Fact]
    public void ExpandHyphenVariants_MultiHyphen_ProducesJoined()
    {
        // "on-premise" → "onpremise"
        var results = NaturalQueryParser.ExpandHyphenVariants("on-premise");
        Assert.Contains("onpremise", results);
    }

    // ── 통합: ToFts5Query에서 하이픈 확장이 OR로 추가되는지 ──

    [Fact]
    public void ToFts5Query_HyphenatedInput_IncludesExpansionInOr()
    {
        // "re-index" → main expr에 "re" AND "index"
        // + OR 절에 "reindex" (하이픈 제거 변형)
        var result = NaturalQueryParser.ToFts5Query("re-index");
        Assert.Contains("OR", result);
        Assert.Contains("reindex", result);
    }
}
