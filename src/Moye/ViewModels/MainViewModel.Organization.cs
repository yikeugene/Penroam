using System.IO;
using System.Text.Json;
using Moye.Models;
using Moye.Services;

namespace Moye.ViewModels;

public sealed record NotebookSearchResult(string NotebookId, string SectionId, string PageId,
    string NotebookTitle, string SectionTitle, string PageTitle, string Snippet)
{
    public string Location => $"{NotebookTitle} › {SectionTitle} › {PageTitle}";
}

public sealed record NotebookTransferReceipt(NotebookDocument SourceBefore, NotebookDocument TargetBefore,
    NotebookDocument SourceAfter, NotebookDocument TargetAfter);

public sealed partial class MainViewModel
{
    private string _categoryFilter = "";
    private bool _sortNotebooksByName;
    private PdfTextExtractionService? _pdfSearch;
    public string SearchCoverageMessage { get; private set; } = "";
    public string CategoryFilter { get => _categoryFilter; set { if (Set(ref _categoryFilter, value ?? "")) FilterLibrary(); } }
    public bool SortNotebooksByName { get => _sortNotebooksByName; set { if (Set(ref _sortNotebooksByName, value)) FilterLibrary(); } }
    public IReadOnlyList<string> Categories => _library.Select(note => note.Folder).Distinct().Order(StringComparer.CurrentCultureIgnoreCase).ToArray();

    public void SetPageDetails(string pageId, string title, bool bookmarked)
    {
        var page = Document?.Pages.FirstOrDefault(page => page.Id == pageId);
        if (page is null) return;
        title = title.Trim();
        if (page.Title == title && page.IsBookmarked == bookmarked) return;
        page.Title = title; page.IsBookmarked = bookmarked;
        RecordChange(true, page.SectionId, page.Id);
    }

    public async Task SetNotebookAppearanceAsync(string notebookId, bool pinned, string color)
    {
        if (color.Length != 0 && !NotebookAppearance.IsValidColor(color)) throw new ArgumentException("Choose a valid cover color.");
        if (Document?.Id == notebookId)
        {
            Document.IsPinned = pinned; Document.CoverColor = color; Changed();
            await Autosave.FlushAsync();
        }
        else
        {
            var document = await Repository.LoadAsync(notebookId) ?? throw new IOException("This notebook could not be found.");
            document.IsPinned = pinned; document.CoverColor = color;
            // Visual organization is not a new writing session.
            await Repository.SaveAsync(document);
        }
        await RefreshLibraryAsync();
    }

    public void ApplyOrganization(NotebookDocument edited)
    {
        if (Document is null || edited.Id != Document.Id) throw new InvalidOperationException("The notebook changed. Open the organizer again.");
        if (JsonSerializer.Serialize(Document, DocumentJson.Options) == JsonSerializer.Serialize(edited, DocumentJson.Options)) return;
        Document = edited.Snapshot();
        RecordChange(true);
    }

    public bool ContinueTextOnNewPage(string pageId, string textId, string retained, NoteText continuation)
    {
        if (Document is null) return false;
        var index = Document.Pages.FindIndex(page => page.Id == pageId);
        if (index < 0) return false;
        var source = Document.Pages[index];
        var text = source.Texts.FirstOrDefault(text => text.Id == textId);
        if (text is null) return false;
        var next = new NotePage { SectionId = source.SectionId, Width = source.Width, Height = source.Height,
            Template = source.Template, Texts = [continuation with { Id = Guid.NewGuid().ToString("N") }] };
        text.Text = retained;
        Document.Pages.Insert(index + 1, next);
        RecordChange(true, next.SectionId, next.Id);
        return true;
    }

    public async Task CreateQuickNoteAsync()
    {
        await Autosave.FlushAsync();
        var inboxSummary = (await Repository.ListAsync()).FirstOrDefault(note => note.IsQuickInbox);
        var inbox = inboxSummary is null ? new NotebookDocument { Title = "Quick Notes", Folder = "Inbox", IsQuickInbox = true, IsPinned = true }
            : await Repository.LoadAsync(inboxSummary.Id) ?? throw new IOException("The quick notes inbox could not be opened.");
        NotebookStructure.Normalize(inbox);
        var page = new NotePage { Title = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), SectionId = inbox.Sections[0].Id, Template = PaperTemplate.Ruled };
        inbox.Pages.Add(page); inbox.ModifiedUtc = DateTimeOffset.UtcNow;
        await Repository.SaveAsync(inbox);
        Search = ""; CategoryFilter = "";
        await RefreshLibraryAsync();
        ReplaceDocumentCore(inbox, true, page.SectionId, page.Id);
        IsLibraryVisible = false; UpdateSaveStatus();
    }

    public async Task<IReadOnlyList<NotebookSearchResult>> SearchContentAsync(string query, bool bookmarksOnly = false, CancellationToken cancellationToken = default)
    {
        query = query.Trim();
        if (query.Length == 0 && !bookmarksOnly) return [];
        var current = Document?.Snapshot();
        IReadOnlyList<NotebookDocument> documents;
        if (Repository is SqliteNotebookRepository sqlite) documents = await sqlite.LoadSearchDocumentsAsync();
        else
        {
            var list = new List<NotebookDocument>();
            foreach (var summary in await Repository.ListAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await Repository.LoadAsync(summary.Id) is { } document) list.Add(document);
            }
            documents = list;
        }
        var searchable = documents.Where(document => document.Id != current?.Id).ToList();
        if (current is not null) searchable.Insert(0, current);
        var warnings = new HashSet<string>(); var incompletePages = 0;
        if (query.Length > 0)
        {
            _pdfSearch ??= new PdfTextExtractionService(Repository);
            foreach (var page in searchable.SelectMany(document => document.Pages).Where(page => page.Pdf is not null && (!bookmarksOnly || page.IsBookmarked)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var extracted = await _pdfSearch.ExtractPageTextAsync(page, cancellationToken);
                // This is an isolated search projection, never an editable page or saved text box.
                if (extracted.HasText) page.Texts.Add(new NoteText { Text = extracted.Text });
                if (extracted.Warning is not null) { incompletePages++; warnings.Add(extracted.Warning); }
            }
        }
        var results = await Task.Run<IReadOnlyList<NotebookSearchResult>>(() => SearchDocuments(searchable, query, bookmarksOnly, cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        SearchCoverageMessage = incompletePages == 0 ? "Handwriting and scanned images are not searched (no OCR)." :
            $"PDF coverage: {incompletePages} page(s) could not be fully searched. {warnings.First()}";
        return results;
    }

    internal static IReadOnlyList<NotebookSearchResult> SearchDocuments(IEnumerable<NotebookDocument> documents, string query, bool bookmarksOnly = false, CancellationToken cancellationToken = default)
    {
        var results = new List<NotebookSearchResult>();
        bool Matches(string value) => query.Length > 0 && value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var section in document.Sections)
            {
                var pages = document.Pages.Where(page => page.SectionId == section.Id).ToArray();
                for (var index = 0; index < pages.Length; index++)
                {
                    var page = pages[index];
                    if (bookmarksOnly && !page.IsBookmarked) continue;
                    var typed = page.Texts.FirstOrDefault(text => Matches(text.Text))?.Text;
                    var metadataMatch = Matches(page.Title) || (index == 0 && Matches(section.Title)) ||
                        (section == document.Sections[0] && index == 0 && (Matches(document.Title) || Matches(document.Folder)));
                    if (!metadataMatch && typed is null && !(bookmarksOnly && query.Length == 0)) continue;
                    var snippet = typed is null ? (page.IsBookmarked ? "Bookmarked page" : "Page title, section or notebook match") : SearchSnippet(typed, query);
                    results.Add(new(document.Id, section.Id, page.Id, document.Title, section.Title,
                        string.IsNullOrWhiteSpace(page.Title) ? $"Page {index + 1}" : $"{index + 1} · {page.Title}", snippet));
                    if (results.Count == 250) return results;
                }
            }
        }
        return results;
    }

    private static string SearchSnippet(string value, string query)
    {
        var match = value.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
        var start = Math.Max(0, match - 45);
        var length = Math.Min(180, value.Length - start);
        return (start > 0 ? "…" : "") + value.Substring(start, length).Replace('\r', ' ').Replace('\n', ' ') + (start + length < value.Length ? "…" : "");
    }

    public async Task<NotebookTransferReceipt> TransferPagesAsync(IEnumerable<string> pageIds, string targetNotebookId, string targetSectionId, bool copy)
    {
        if (Document is null) throw new InvalidOperationException("Open a notebook first.");
        if (Repository is not IAtomicNotebookRepository atomic) throw new NotSupportedException("This library does not support atomic page transfers.");
        if (Document.Id == targetNotebookId) throw new InvalidOperationException("Use Move to Section for pages in this notebook.");
        await Autosave.FlushAsync();
        var sourceBefore = Document.Snapshot();
        var targetBefore = await Repository.LoadAsync(targetNotebookId) ?? throw new IOException("The destination notebook no longer exists.");
        var source = sourceBefore.Snapshot(); var target = targetBefore.Snapshot();
        if (!target.Sections.Any(section => section.Id == targetSectionId)) throw new IOException("The destination section no longer exists.");
        var ids = pageIds.ToHashSet(StringComparer.Ordinal);
        var pages = source.Pages.Where(page => ids.Contains(page.Id)).ToArray();
        if (pages.Length == 0 || pages.Length != ids.Count) throw new IOException("One or more selected pages no longer exist.");
        var assets = pages.SelectMany(page => page.Images.Select(image => image.AssetId).Concat(page.Pdf is null ? [] : new[] { page.Pdf.AssetId })).Distinct();
        foreach (var asset in assets) await Repository.GetAssetAsync(asset);
        foreach (var page in pages)
        {
            var transferred = copy || target.Pages.Any(existing => existing.Id == page.Id) ? CopyPage(page) : page.Snapshot();
            transferred.SectionId = targetSectionId; target.Pages.Add(transferred);
        }
        if (!copy)
        {
            source.Pages.RemoveAll(page => ids.Contains(page.Id));
            if (source.Pages.Count == 0) source.Pages.Add(new NotePage { SectionId = source.Sections[0].Id });
            source.ModifiedUtc = DateTimeOffset.UtcNow;
        }
        target.ModifiedUtc = DateTimeOffset.UtcNow;
        NotebookStructure.Normalize(target);
        await atomic.SaveBatchAsync(copy ? [target] : [source, target]);
        // Notebook-local undo cannot safely reverse half a cross-notebook transaction.
        // The receipt supports an explicit checked, atomic Undo Transfer action.
        if (!copy) ReplaceDocument(source, true);
        await RefreshLibraryAsync(); UpdateSaveStatus();
        return new(sourceBefore, targetBefore, source.Snapshot(), target.Snapshot());
    }

    public async Task UndoTransferAsync(NotebookTransferReceipt receipt)
    {
        if (Repository is not IAtomicNotebookRepository atomic) throw new NotSupportedException();
        await Autosave.FlushAsync();
        foreach (var expected in new[] { receipt.SourceAfter, receipt.TargetAfter })
        {
            var actual = await Repository.LoadAsync(expected.Id);
            if (actual is null || JsonSerializer.Serialize(actual, DocumentJson.Options) != JsonSerializer.Serialize(expected, DocumentJson.Options))
                throw new InvalidOperationException("A transferred notebook has changed. Undo transfer is no longer safe; use the page organizer to move the pages back.");
        }
        await atomic.SaveBatchAsync([receipt.SourceBefore, receipt.TargetBefore]);
        if (Document?.Id == receipt.SourceBefore.Id) ReplaceDocument(receipt.SourceBefore.Snapshot(), true);
        else if (Document?.Id == receipt.TargetBefore.Id) ReplaceDocument(receipt.TargetBefore.Snapshot(), true);
        await RefreshLibraryAsync(); UpdateSaveStatus();
    }
}
