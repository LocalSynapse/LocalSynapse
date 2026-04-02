using LocalSynapse.Core.Models;

namespace LocalSynapse.Search.Interfaces;

public interface IDocumentFamilyService
{
    IReadOnlyList<DocumentFamily> GroupResults(IEnumerable<HybridHit> hits);
}

public sealed class DocumentFamily
{
    public required string FamilyKey { get; set; }
    public required string Label { get; set; }
    public required string FolderPath { get; set; }
    public List<HybridHit> Files { get; set; } = [];
    public string PrimaryType { get; set; } = "";
    public string LatestDate { get; set; } = "";
    public string MatchSnippet { get; set; } = "";
}
