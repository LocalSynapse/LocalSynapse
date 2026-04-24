using Xunit;
using LocalSynapse.Search.Services;

namespace LocalSynapse.Search.Tests;

/// <summary>
/// Porter stemmer가 FTS5 레벨에서 실제로 동작하는지 검증하는 통합 테스트.
/// MigrationService가 porter 토크나이저로 FTS 테이블을 생성한 후,
/// 형태소 변형 검색이 매칭되는지 확인한다.
/// </summary>
public sealed class PorterStemmingIntegrationTest : IDisposable
{
    private readonly SearchTestDb _db;
    private readonly Bm25SearchService _sut;

    public PorterStemmingIntegrationTest()
    {
        _db = SearchTestHelper.Create();
        _sut = new Bm25SearchService(_db.Factory, _db.ClickService);
    }

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("budgets", "budget")]           // 복수형 → 원형
    [InlineData("searching", "search")]         // 진행형 → 원형
    [InlineData("indexed", "index")]             // 과거형 → 원형 (chunk3: "indexing")
    [InlineData("expenditures", "expenditure")] // 복수형 → 원형
    public void Search_PorterStemming_MatchesMorphologicalVariants(string queryVariant, string expectedStem)
    {
        // 테스트 데이터에 있는 단어들의 변형으로 검색
        var options = new SearchOptions { TopK = 10 };
        var results = _sut.Search(queryVariant, options);

        // Porter stemmer가 활성화되어 있으면 변형으로도 검색 결과가 나와야 함
        Assert.True(results.Count > 0,
            $"Porter stemmer should match '{queryVariant}' (stem: '{expectedStem}') to indexed content");
    }

    [Fact]
    public void Search_ExactTerm_StillWorks()
    {
        // 기존 동작: 정확한 텀으로도 당연히 매칭
        var options = new SearchOptions { TopK = 10 };
        var results = _sut.Search("budget", options);

        Assert.Contains(results, r => r.FileId == SearchTestHelper.File1Id);
    }

    [Fact]
    public void Search_PluralForm_FindsSingularContent()
    {
        // "contracts" 검색 → "contract" 콘텐츠 매칭
        var options = new SearchOptions { TopK = 10 };
        var results = _sut.Search("contracts", options);

        Assert.True(results.Count >= 1,
            "Porter stemmer should match 'contracts' to content containing 'contract'");
    }

    [Fact]
    public void Search_KoreanQuery_NotAffectedByPorter()
    {
        // 한국어 검색은 porter 영향 없이 정상 동작해야 함
        // (테스트 데이터에 한국어 콘텐츠가 없으므로 결과 0건이 정상)
        var options = new SearchOptions { TopK = 10 };
        var results = _sut.Search("계약서", options);

        // 매칭 여부보다 에러 없이 실행되는지가 핵심
        Assert.NotNull(results);
    }
}
