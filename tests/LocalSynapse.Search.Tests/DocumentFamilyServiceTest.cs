using Xunit;
using LocalSynapse.Core.Models;
using LocalSynapse.Search.Services;

namespace LocalSynapse.Search.Tests;

public sealed class DocumentFamilyServiceTest
{
    private readonly DocumentFamilyService _sut = new();

    [Fact]
    public void GroupResults_GroupsByFolder()
    {
        var hits = new List<HybridHit>
        {
            MakeHit("f1", "report_v1.pdf", "/Shared"),
            MakeHit("f2", "report_v2.pdf", "/Shared"),
        };

        var families = _sut.GroupResults(hits);

        // Both files share the same normalized name ("report") in the same folder
        Assert.Single(families);
        Assert.Equal(2, families[0].Files.Count);
    }

    [Fact]
    public void GroupResults_SeparatesDifferentFolders()
    {
        var hits = new List<HybridHit>
        {
            MakeHit("f1", "notes.txt", "/FolderA"),
            MakeHit("f2", "data.csv", "/FolderB"),
        };

        var families = _sut.GroupResults(hits);

        Assert.Equal(2, families.Count);
    }

    [Fact]
    public void GroupResults_GroupsVersions()
    {
        // "contract_final.pdf" normalizes to "contract" (final is removed by VersionRegex)
        // "contract_v1.pdf" normalizes to "contract" (v1 is removed by VersionRegex)
        var hits = new List<HybridHit>
        {
            MakeHit("f4", "contract_final.pdf", "/Legal"),
            MakeHit("f5", "contract_v1.pdf", "/Legal"),
        };

        var families = _sut.GroupResults(hits);

        // Both should normalize to "contract" -> same family key
        Assert.Single(families);
        Assert.Equal(2, families[0].Files.Count);
    }

    private static HybridHit MakeHit(string fileId, string filename, string folder, double score = 1.0) => new()
    {
        FileId = fileId,
        Filename = filename,
        Path = $"{folder}/{filename}",
        Extension = Path.GetExtension(filename),
        FolderPath = folder,
        HybridScore = score,
        ModifiedAt = DateTime.UtcNow.ToString("o"),
        MatchSource = MatchSource.Content,
    };
}
