using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Moye.Controls;
using Moye.Models;
using Moye.Services;

namespace Moye.Tests;

[Collection("Page spread shell")]
public sealed class DocumentSelectionDialogTests(PageSpreadTestApplication application)
{
    [Fact]
    public void ExportStartsWithOnlyTheCurrentSectionsPagesVisibleAndSelected() => application.Run(() =>
    {
        using var fixture = new SelectionFixture();

        Assert.Equal(new[] { "page-1", "page-2" }, fixture.VisibleIds);
        Assert.Equal(fixture.VisibleIds, fixture.SelectedIds);
        Assert.True(fixture.Accept.IsEnabled);

        fixture.Scope.SelectedIndex = 1;
        Assert.Equal(new[] { "page-2" }, fixture.VisibleIds);
        Assert.Equal(fixture.VisibleIds, fixture.SelectedIds);

        fixture.Scope.SelectedIndex = 3;
        Assert.Equal(new[] { "page-3", "page-4", "page-5" }, fixture.VisibleIds);
        Assert.Equal(fixture.VisibleIds, fixture.SelectedIds);
    });

    [Fact]
    public void SelectAllIncludesOnlyFilteredResultsAndKeepsHiddenPagesUnchecked() => application.Run(() =>
    {
        using var fixture = new SelectionFixture();
        fixture.Click("ResetExportFilters");
        fixture.Click("ClearDocumentPages");
        fixture.Search.Text = "shared";

        Assert.Equal(new[] { "page-2", "page-4" }, fixture.VisibleIds);
        Assert.Empty(fixture.SelectedIds);
        fixture.Click("SelectAllDocumentPages");
        Assert.Equal(new[] { "page-2", "page-4" }, fixture.SelectedIds);

        fixture.Click("ResetExportFilters");
        Assert.Equal(5, fixture.VisibleIds.Length);
        Assert.Equal(new[] { "page-2", "page-4" }, fixture.SelectedIds);

        fixture.Search.Text = "alpha";
        Assert.Empty(fixture.SelectedIds);
        fixture.Click("SelectAllDocumentPages");
        Assert.Equal(new[] { "page-1" }, fixture.SelectedIds);
        fixture.Click("ResetExportFilters");
        Assert.Equal(new[] { "page-1", "page-2", "page-4" }, fixture.SelectedIds);
    });

    [Fact]
    public void ClearingFilteredPagesDoesNotClearOtherSectionsOrReselectWhenReturning() => application.Run(() =>
    {
        using var fixture = new SelectionFixture();
        fixture.Search.Text = "shared";
        fixture.Click("ClearDocumentPages");
        Assert.Empty(fixture.SelectedIds);

        fixture.Scope.SelectedIndex = 3;
        Assert.Equal(new[] { "page-4" }, fixture.SelectedIds);
        fixture.Scope.SelectedIndex = 2;
        Assert.Equal(new[] { "page-2" }, fixture.VisibleIds);
        Assert.Empty(fixture.SelectedIds);

        fixture.Click("ResetExportFilters");
        Assert.Equal(new[] { "page-1", "page-3", "page-4", "page-5" }, fixture.SelectedIds);
    });

    [Fact]
    public void SectionSearchBookmarkAndGlobalPageRangeFiltersIntersectWithoutChangingNotebookOrder() => application.Run(() =>
    {
        using var fixture = new SelectionFixture();
        var original = JsonSerializer.Serialize(fixture.Document, DocumentJson.Options);
        fixture.Scope.SelectedIndex = 0;
        fixture.Search.Text = "  REVIEW  shared  ";
        fixture.Control<CheckBox>("ExportBookmarkedFilter").IsChecked = true;
        fixture.Range.Text = "4, 2, 4";
        fixture.Click("ApplyExportRange");

        Assert.Equal(new[] { "page-2", "page-4" }, fixture.VisibleIds);
        Assert.Equal(fixture.VisibleIds, fixture.SelectedIds);
        fixture.Scope.SelectedIndex = 3;
        Assert.Equal(new[] { "page-4" }, fixture.SelectedIds);

        fixture.Search.Text = "CALCULUS shared";
        Assert.Equal(new[] { "page-4" }, fixture.SelectedIds);
        var snapshot = DocumentWorkflows.ExportSnapshot(fixture.Document, fixture.SelectedIds);
        Assert.Equal(new[] { "page-4" }, snapshot.Pages.Select(page => page.Id));
        Assert.Equal(original, JsonSerializer.Serialize(fixture.Document, DocumentJson.Options));

        fixture.Click("ResetExportFilters");
        Assert.Equal(0, fixture.Scope.SelectedIndex);
        Assert.Equal("", fixture.Search.Text);
        Assert.Equal("", fixture.Range.Text);
        Assert.False(fixture.Control<CheckBox>("ExportBookmarkedFilter").IsChecked);
        Assert.Equal(fixture.Document.Pages.Select(page => page.Id), fixture.SelectedIds);
    });

    [Fact]
    public void SearchFindsSectionNamesAndTraditionalChineseTitlesWithoutChangingNotebookContent() => application.Run(() =>
    {
        using var fixture = new SelectionFixture();
        fixture.Scope.SelectedIndex = 0;
        fixture.Search.Text = "calCULus BASICS";
        Assert.Equal(new[] { "page-3" }, fixture.VisibleIds);
        fixture.Search.Text = "函數";
        Assert.Equal(new[] { "page-5" }, fixture.SelectedIds);

        fixture.Document.Pages[4].Title = "Changed after the dialog opened";
        Assert.Equal("函數摘要", Assert.Single(fixture.Dialog.SelectedPages).Title);
    });

    [Fact]
    public void BookmarkedOnlyExcludesUnbookmarkedPagesFromSelectAll() => application.Run(() =>
    {
        using var fixture = new SelectionFixture();
        fixture.Scope.SelectedIndex = 0;
        fixture.Click("ClearDocumentPages");
        fixture.Control<CheckBox>("ExportBookmarkedFilter").IsChecked = true;
        Assert.Equal(new[] { "page-2", "page-4" }, fixture.VisibleIds);
        fixture.Click("SelectAllDocumentPages");
        Assert.Equal(fixture.VisibleIds, fixture.SelectedIds);

        fixture.Control<CheckBox>("ExportBookmarkedFilter").IsChecked = false;
        Assert.Equal(5, fixture.VisibleIds.Length);
        Assert.Equal(new[] { "page-2", "page-4" }, fixture.SelectedIds);
    });

    [Fact]
    public void PendingAndInvalidPageRangesCannotExportStaleSelections() => application.Run(() =>
    {
        using var fixture = new SelectionFixture();
        fixture.Scope.SelectedIndex = 0;
        fixture.Range.Text = "4, 2";
        Assert.False(fixture.Accept.IsEnabled);
        fixture.Click("ApplyExportRange");
        Assert.Equal(new[] { "page-2", "page-4" }, fixture.SelectedIds);
        Assert.True(fixture.Accept.IsEnabled);

        fixture.Range.Text = "1, 99";
        Assert.False(fixture.Accept.IsEnabled);
        fixture.Click("ApplyExportRange");
        Assert.Empty(fixture.VisibleIds);
        Assert.Empty(fixture.SelectedIds);
        Assert.False(fixture.Accept.IsEnabled);
        fixture.Click("SelectAllDocumentPages");
        fixture.Search.Text = "shared";
        Assert.Empty(fixture.SelectedIds);
        Assert.False(fixture.Accept.IsEnabled);

        fixture.Range.Text = "";
        fixture.Click("ApplyExportRange");
        Assert.Equal(new[] { "page-2", "page-4" }, fixture.SelectedIds);
        Assert.True(fixture.Accept.IsEnabled);
    });

    [Fact]
    public void EmptySectionsAndUnmatchedSearchesNeverFallBackToExportingTheNotebook() => application.Run(() =>
    {
        using var fixture = new SelectionFixture();
        fixture.Scope.SelectedIndex = 4;
        Assert.Empty(fixture.VisibleIds);
        Assert.Empty(fixture.SelectedIds);
        Assert.False(fixture.Accept.IsEnabled);
        fixture.Click("SelectAllDocumentPages");
        Assert.Empty(fixture.SelectedIds);

        fixture.Scope.SelectedIndex = 0;
        fixture.Search.Text = "no-such-page";
        Assert.Empty(fixture.VisibleIds);
        Assert.Empty(fixture.SelectedIds);
        Assert.False(fixture.Accept.IsEnabled);
        fixture.Click("ResetExportFilters");
        Assert.Equal(5, fixture.SelectedIds.Length);
        Assert.True(fixture.Accept.IsEnabled);
    });

    [Fact]
    public void MissingCurrentSectionShowsAllPagesButMissingCurrentPageProducesNoMatch() => application.Run(() =>
    {
        using var fixture = new SelectionFixture(currentSectionId: null, currentPageId: null);
        Assert.Equal(0, fixture.Scope.SelectedIndex);
        Assert.Equal(5, fixture.VisibleIds.Length);
        fixture.Scope.SelectedIndex = 1;
        Assert.Empty(fixture.VisibleIds);
        Assert.Empty(fixture.SelectedIds);
        Assert.False(fixture.Accept.IsEnabled);
    });

    [Fact]
    public void ImportRangeRemainsASelectionAndDoesNotFilterTheSourcePageList() => application.Run(() =>
    {
        using var fixture = new SelectionFixture(importing: true);
        Assert.Equal(5, fixture.VisibleIds.Length);
        Assert.Equal(fixture.VisibleIds, fixture.SelectedIds);
        fixture.Control<TextBox>("ImportRangeSelection").Text = "4, 2";
        fixture.Click("ApplyImportRange");
        Assert.Equal(new[] { "page-2", "page-4" }, fixture.SelectedIds);
        Assert.Equal(5, fixture.VisibleIds.Length);
        fixture.Click("SelectAllDocumentPages");
        Assert.Equal(fixture.VisibleIds, fixture.SelectedIds);
    });

    private sealed class SelectionFixture : IDisposable
    {
        private readonly PageSpreadInteractionTests.ShellRepository _repository;
        public NotebookDocument Document { get; } = new()
        {
            Title = "Synthetic export notebook",
            Sections = [new() { Id = "section-a", Title = "Algebra" }, new() { Id = "section-b", Title = "Calculus" }, new() { Id = "section-empty", Title = "Revision" }],
            Pages =
            [
                new() { Id = "page-1", SectionId = "section-a", Title = "Alpha basics" },
                new() { Id = "page-2", SectionId = "section-a", Title = "Shared review", IsBookmarked = true },
                new() { Id = "page-3", SectionId = "section-b", Title = "Calculus basics" },
                new() { Id = "page-4", SectionId = "section-b", Title = "Shared review", IsBookmarked = true },
                new() { Id = "page-5", SectionId = "section-b", Title = "函數摘要" }
            ]
        };
        public DocumentSelectionDialog Dialog { get; }
        public ComboBox Scope => Control<ComboBox>("ExportSectionFilter");
        public TextBox Search => Control<TextBox>("ExportSearchFilter");
        public TextBox Range => Control<TextBox>("ExportRangeFilter");
        public Button Accept => Control<Button>("AcceptDocumentPages");
        public string[] SelectedIds => Dialog.SelectedPages.Select(page => page.Id).ToArray();
        public string[] VisibleIds => Control<ListBox>("DocumentPageList").Items.Cast<object>()
            .Select(choice => ((NotePage)choice.GetType().GetProperty("Page")!.GetValue(choice)!).Id).ToArray();

        public SelectionFixture(bool importing = false, string? currentSectionId = "section-a", string? currentPageId = "page-2")
        {
            _repository = new(Document);
            Dialog = new DocumentSelectionDialog(null!, "Synthetic page selection", Document.Pages,
                new PagePreviewRenderer(new PdfService(_repository), _repository.GetAssetAsync), Document,
                currentPageId, currentSectionId, importing);
        }

        public T Control<T>(string id) where T : FrameworkElement => Assert.Single(Descendants(Dialog).OfType<T>(),
            element => AutomationProperties.GetAutomationId(element) == id);
        public void Click(string id) => Control<Button>(id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        public void Dispose() { Dialog.Close(); _repository.Dispose(); }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
    }
}
