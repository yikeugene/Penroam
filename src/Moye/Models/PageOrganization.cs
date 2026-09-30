namespace Moye.Models;

/// <summary>Edits an isolated organizer snapshot; the shell applies the result as one undo step.</summary>
public static class PageOrganization
{
    public static void MoveToSection(NotebookDocument document, IEnumerable<string> ids, string sectionId)
    {
        if (!document.Sections.Any(section => section.Id == sectionId)) throw new ArgumentException("Choose an existing section.");
        var selected = ids.ToHashSet(StringComparer.Ordinal);
        var pages = document.Pages.Where(page => selected.Contains(page.Id)).ToArray();
        document.Pages.RemoveAll(page => selected.Contains(page.Id));
        foreach (var page in pages) { page.SectionId = sectionId; document.Pages.Add(page); }
        NotebookStructure.Normalize(document);
    }

    public static void MoveBefore(NotebookDocument document, IEnumerable<string> ids, string beforePageId)
    {
        var selected = ids.ToHashSet(StringComparer.Ordinal);
        var target = document.Pages.FirstOrDefault(page => page.Id == beforePageId);
        if (target is null || selected.Contains(target.Id)) return;
        var pages = document.Pages.Where(page => selected.Contains(page.Id)).ToArray();
        document.Pages.RemoveAll(page => selected.Contains(page.Id));
        foreach (var page in pages) page.SectionId = target.SectionId;
        document.Pages.InsertRange(document.Pages.IndexOf(target), pages);
        NotebookStructure.Normalize(document);
    }

    public static void MoveBy(NotebookDocument document, IEnumerable<string> ids, int direction)
    {
        var selected = ids.ToHashSet(StringComparer.Ordinal);
        if (direction < 0)
        {
            for (var index = 1; index < document.Pages.Count; index++)
                if (selected.Contains(document.Pages[index].Id) && !selected.Contains(document.Pages[index - 1].Id) && document.Pages[index].SectionId == document.Pages[index - 1].SectionId)
                    (document.Pages[index - 1], document.Pages[index]) = (document.Pages[index], document.Pages[index - 1]);
        }
        else
        {
            for (var index = document.Pages.Count - 2; index >= 0; index--)
                if (selected.Contains(document.Pages[index].Id) && !selected.Contains(document.Pages[index + 1].Id) && document.Pages[index].SectionId == document.Pages[index + 1].SectionId)
                    (document.Pages[index + 1], document.Pages[index]) = (document.Pages[index], document.Pages[index + 1]);
        }
    }

    public static IReadOnlyList<string> Duplicate(NotebookDocument document, IEnumerable<string> ids)
    {
        var selected = ids.ToHashSet(StringComparer.Ordinal);
        var copies = new List<string>();
        foreach (var page in document.Pages.Where(page => selected.Contains(page.Id)).ToArray())
        {
            var copy = page.Snapshot(); copy.Id = Guid.NewGuid().ToString("N");
            foreach (var text in copy.Texts) text.Id = Guid.NewGuid().ToString("N");
            foreach (var image in copy.Images) image.Id = Guid.NewGuid().ToString("N");
            document.Pages.Insert(document.Pages.IndexOf(page) + 1, copy); copies.Add(copy.Id);
        }
        return copies;
    }

    public static void Delete(NotebookDocument document, IEnumerable<string> ids)
    {
        var selected = ids.ToHashSet(StringComparer.Ordinal);
        document.Pages.RemoveAll(page => selected.Contains(page.Id));
        if (document.Pages.Count == 0) document.Pages.Add(new NotePage { SectionId = document.Sections[0].Id });
    }
}
