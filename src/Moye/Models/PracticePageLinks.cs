namespace Moye.Models;

/// <summary>Keep source links inside a copied set and isolate copied question groups.</summary>
public static class PracticePageLinks
{
    public static void RemapCopiedSources(IEnumerable<NotePage> copies, IReadOnlyDictionary<string, string> pageIds)
    {
        var groups = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var page in copies)
        {
            if (page.Practice is not { } practice || !pageIds.TryGetValue(practice.SourcePageId, out var sourceId)
                || sourceId == practice.SourcePageId) continue;
            practice.SourcePageId = sourceId;
            if (!groups.TryGetValue(practice.GroupId, out var group)) groups[practice.GroupId] = group = Guid.NewGuid().ToString("N");
            practice.GroupId = group;
        }
    }
}
