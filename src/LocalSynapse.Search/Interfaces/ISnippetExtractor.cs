namespace LocalSynapse.Search.Interfaces;

public interface ISnippetExtractor
{
    string Extract(string content, IEnumerable<string> queryTerms, int maxLength = 200);
}
