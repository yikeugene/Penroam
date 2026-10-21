using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Moye.Controls;
using Moye.Models;
using ShellFixture = Moye.Tests.PageSpreadInteractionTests.ShellFixture;

namespace Moye.Tests;

[Collection("Page spread shell")]
public sealed class FeatureDiscoveryTests(PageSpreadTestApplication application)
{
    // An independent inventory, not an expectation generated from the catalog being tested.
    private static readonly (string Category, string Ids)[] RootInventory =
    [
        ("Notebook", "new-notebook open-notebooks continue-notebook quick-note rename-notebook pin-notebook cover-color delete-notebook sort-library find-notebook"),
        ("Pages", "add-page page-actions paper-style extend-page add-section section-actions manage-pages undo-transfer"),
        ("Insert & export", "import-document import-folder insert-image export-pdf"),
        ("Search", "search-content search-appearance bookmarks go-to-page back-to-page"),
        ("Study", "mark-question question-inbox resume-hint resume-thinking new-practice practice-attempts restore-region"),
        ("View", "single-page two-pages fit-page fit-width actual-size zoom-in zoom-out sidebar focus-mode reference-view workspace-settings"),
        ("Tools", "pen highlighter eraser lasso type select pen-settings manage-pens eraser-settings text-formatting selection-actions undo redo"),
        ("Backup", "save-now retry-save backup-notebook backup-all restore-backup backup-recovery automatic-backups"),
        ("Help", "check-updates writing-settings-warning keyboard-help pan-touch-help typing-help autosave-help document-help isolated-library-help")
    ];

    private static readonly (string Parent, string Ids)[] ChildInventory =
    [
        ("find-notebook", "clear-library-search"),
        ("page-actions", "rename-page bookmark-page duplicate-page move-page-up move-page-down move-page-section delete-page"),
        ("section-actions", "rename-section move-section-up move-section-down delete-section"),
        ("manage-pages", "transfer-pages"),
        ("export-pdf", "a4-export export-overflow export-completion"),
        ("workspace-settings", "two-finger-navigation zoom-lock compact-toolbar focus-docking"),
        ("pen-settings", "ink-color more-colors ink-width draw-hold save-pen-preset"),
        ("manage-pens", "pen-opacity pressure smoothing favorite-pens favorite-1 favorite-2 favorite-3 favorite-4 favorite-5 favorite-6 favorite-7 favorite-8 favorite-9"),
        ("eraser-settings", "pixel-eraser stroke-eraser eraser-size erase-highlighter-only"),
        ("text-formatting", "add-text-box text-font text-size text-bold text-italic text-color text-alignment bullet-list numbered-list continue-text finish-typing"),
        ("selection-actions", "select-all copy-selection cut-selection paste duplicate-selection delete-selection nudge-left nudge-right nudge-up nudge-down"),
        ("backup-recovery", "recover-drafts recently-deleted"),
        ("automatic-backups", "backup-folder backup-interval backup-retention")
    ];

    [Fact]
    public void ProductionCatalogCoversTheFeatureInventoryWithUniqueIdsAndUsableLocations() => application.Run(() =>
    {
        using var fixture = new ShellFixture(2);
        var commands = fixture.Window.CreateFeatureCommands();
        Assert.Equal(commands.Count, commands.Select(command => command.Id).Distinct(StringComparer.Ordinal).Count());
        var catalog = commands.ToDictionary(command => command.Id);
        var expectedIds = RootInventory.SelectMany(group => Ids(group.Ids))
            .Concat(ChildInventory.SelectMany(group => Ids(group.Ids))).Order(StringComparer.Ordinal);
        Assert.Equal(expectedIds, catalog.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(RootInventory.Select(group => group.Category), MainWindow.FeatureCategories);

        foreach (var (category, ids) in RootInventory)
            foreach (var id in Ids(ids))
            {
                Assert.Equal(category, catalog[id].Category);
                Assert.Null(catalog[id].ParentId);
            }
        foreach (var (parent, ids) in ChildInventory)
            foreach (var id in Ids(ids))
            {
                Assert.Equal(parent, catalog[id].ParentId);
                Assert.Equal(catalog[parent].Category, catalog[id].Category);
                Assert.StartsWith(catalog[parent].Location + " → ", catalog[id].Location);
            }
        Assert.All(commands, command =>
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Name));
            Assert.False(string.IsNullOrWhiteSpace(command.Detail));
            Assert.False(string.IsNullOrWhiteSpace(command.Location));
            Assert.StartsWith(command.Category + " → ", command.Location);
            Assert.NotNull(command.GetUnavailableReason);
            Assert.Contains(command.Category, MainWindow.FeatureCategories);
            if (command.ParentId is { } parent)
            {
                Assert.NotEqual(command.Id, parent);
                Assert.True(catalog.ContainsKey(parent));
                Assert.Null(catalog[parent].ParentId);
            }
        });
        foreach (var id in Ids("keyboard-help pan-touch-help typing-help autosave-help document-help isolated-library-help export-overflow export-completion"))
        {
            Assert.True(catalog[id].IsInstruction);
            Assert.False(catalog[id].CanExecute);
            Assert.False(catalog[id].TryExecute());
        }
        Assert.Equal("Ctrl+Shift+F", catalog["search-content"].Shortcut);
        Assert.Equal("Ctrl+G", catalog["go-to-page"].Shortcut);
        Assert.Equal("F11", catalog["focus-mode"].Shortcut);
        for (var number = 1; number <= 9; number++) Assert.Equal(number.ToString(), catalog[$"favorite-{number}"].Shortcut);
    });

    [Fact]
    public void CategoryMenusShowOnlyRootCommandsWhileChildrenRemainSearchable() => application.Run(() =>
    {
        using var fixture = new ShellFixture(2);
        var menu = Find<Menu>(fixture, "FeatureMenu");
        Assert.Equal(RootInventory.Select(group => group.Category), menu.Items.Cast<MenuItem>().Select(item => Assert.IsType<string>(item.Header)));
        foreach (var (category, ids) in RootInventory)
        {
            var item = menu.Items.Cast<MenuItem>().Single(candidate => Equals(candidate.Header, category));
            Assert.True(item.MinHeight >= 44);
            Assert.True(item.MinWidth >= 44);
            Assert.Equal(category + " actions", AutomationProperties.GetName(item));
            // Populate the real menu without showing a popup or dispatching a command.
            item.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, item));
            Assert.False(item.IsSubmenuOpen);
            Assert.Equal(Ids(ids), item.Items.Cast<MenuItem>().Select(child => Assert.IsType<string>(child.Tag)));
            Assert.All(item.Items.Cast<MenuItem>(), child => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(child))));
        }
        var catalog = fixture.Window.CreateFeatureCommands();
        foreach (var (_, ids) in ChildInventory)
            foreach (var id in Ids(ids))
            {
                var child = catalog.Single(command => command.Id == id);
                Assert.Contains(child, catalog.Where(command => command.Matches(child.Name)));
            }
    });

    [Fact]
    public void SemanticQueriesFindDetailedFeaturesAcrossAllCategories() => application.Run(() =>
    {
        using var fixture = new ShellFixture(2);
        var catalog = fixture.Window.CreateFeatureCommands();
        foreach (var (query, id) in new[]
        {
            ("pressure", "pressure"), ("two pages", "two-pages"), ("restore backup", "restore-backup"),
            ("restore part", "restore-region"), ("opacity", "pen-opacity"), ("smoothing", "smoothing"),
            ("highlighter only", "erase-highlighter-only"), ("favorite toolbar", "favorite-pens"),
            ("text alignment", "text-alignment"), ("continue text", "continue-text"),
            ("Ctrl+C", "copy-selection"), ("selection ten", "nudge-left"),
            ("Word ranges", "import-document"), ("A4 overlap", "a4-export"),
            ("interrupted drafts", "recover-drafts"), ("automatic interval", "backup-interval"),
            ("GitHub", "check-updates"), ("palm pressure", "pan-touch-help")
        }) Assert.Contains(catalog.Where(command => command.Matches(query)), command => command.Id == id);
        Assert.DoesNotContain(catalog, command => command.Matches("no-such-feature-9a62"));
    });

    [Fact]
    public void ConstructingFilteringAndReadingAvailabilityDoesNotEditTheNotebookOrSettings() => application.Run(() =>
    {
        using var fixture = new ShellFixture(2, document => document.Pages[0].Texts.Add(new() { Text = "Synthetic existing text" }));
        var document = JsonSerializer.Serialize(fixture.Model.Document);
        var writing = JsonSerializer.Serialize(Get<WritingPreferences>(fixture.Window, "_preferences"));
        var workspace = JsonSerializer.Serialize(Get<WorkspacePreferences>(fixture.Window, "_workspace"));
        var selectedPage = fixture.Model.SelectedPage;
        var selectedSection = fixture.Model.SelectedSection;
        var tool = Get<InkTool>(fixture.Window, "_tool");
        var typingRequest = Get<int>(fixture.Window, "_typingRequest");
        var historyChanges = 0;
        fixture.Model.HistoryChanged += (_, _) => historyChanges++;

        for (var pass = 0; pass < 3; pass++)
        {
            var catalog = fixture.Window.CreateFeatureCommands();
            foreach (var query in new[] { "", "pressure", "restore", "two pages", "text", "not-a-command" })
                foreach (var command in catalog.Where(command => command.Matches(query)))
                {
                    _ = command.CanExecute; _ = command.UnavailableReason; _ = command.IsChecked;
                }
        }

        Assert.Equal(document, JsonSerializer.Serialize(fixture.Model.Document));
        Assert.Equal(writing, JsonSerializer.Serialize(Get<WritingPreferences>(fixture.Window, "_preferences")));
        Assert.Equal(workspace, JsonSerializer.Serialize(Get<WorkspacePreferences>(fixture.Window, "_workspace")));
        Assert.Same(selectedPage, fixture.Model.SelectedPage);
        Assert.Same(selectedSection, fixture.Model.SelectedSection);
        Assert.Equal(tool, Get<InkTool>(fixture.Window, "_tool"));
        Assert.Equal(typingRequest, Get<int>(fixture.Window, "_typingRequest"));
        Assert.Equal(0, historyChanges);
        Assert.False(fixture.Model.CanUndo); Assert.False(fixture.Model.CanRedo);
        Assert.False(fixture.Model.Autosave.IsDirty);
        Assert.Null(fixture.Repository.Saved);
    });

    [Fact]
    public Task HomeDoesNotEnableCommandsForTheDocumentRetainedInMemory() => application.RunAsync(async () =>
    {
        using var fixture = new ShellFixture(2);
        var captured = fixture.Window.CreateFeatureCommands();
        var document = fixture.Model.Document;
        await fixture.Model.ReturnToLibraryAsync(); fixture.Layout();
        Assert.True(fixture.Model.IsLibraryVisible);
        Assert.Same(document, fixture.Model.Document);
        var before = JsonSerializer.Serialize(document);
        var home = fixture.Window.CreateFeatureCommands();
        foreach (var catalog in new[] { captured, home })
            foreach (var command in catalog.Where(command => command.Category is "Pages" or "Study" or "Tools" ||
                         Ids("rename-notebook pin-notebook cover-color delete-notebook import-document insert-image export-pdf a4-export single-page two-pages fit-page fit-width actual-size zoom-in zoom-out sidebar focus-mode reference-view save-now backup-notebook").Contains(command.Id)))
                AssertUnavailable(command, "Open a notebook first.", tryExecute: true);
        Assert.True(home.Single(command => command.Id == "import-folder").CanExecute);
        Assert.True(home.Single(command => command.Id == "restore-backup").CanExecute);
        Assert.True(home.Single(command => command.Id == "new-notebook").CanExecute);
        Assert.Equal(before, JsonSerializer.Serialize(fixture.Model.Document));
        Assert.False(fixture.Model.CanUndo); Assert.False(fixture.Model.Autosave.IsDirty);
    });

    [Fact]
    public void EmptySectionCanAddPagesButCannotRunPageOrEditorCommands() => application.Run(() =>
    {
        using var fixture = new ShellFixture(2);
        fixture.Model.AddSection("Synthetic empty section");
        fixture.Model.DeletePage(); fixture.Layout();
        Assert.NotNull(fixture.Model.SelectedSection);
        Assert.Empty(fixture.Model.Pages); Assert.Null(fixture.Model.SelectedPage);
        var catalog = fixture.Window.CreateFeatureCommands().ToDictionary(command => command.Id);
        Assert.True(catalog["add-page"].CanExecute);
        Assert.True(catalog["add-section"].CanExecute);
        Assert.True(catalog["section-actions"].CanExecute);
        foreach (var id in Ids("page-actions bookmark-page duplicate-page paper-style extend-page type add-text-box insert-image go-to-page mark-question resume-hint new-practice restore-region selection-actions paste text-bold"))
            AssertUnavailable(catalog[id], "Add or select a page first.", tryExecute: true);
    });

    [Fact]
    public void UnavailableActionsExplainSelectionTextHistoryTransferAndBoundaryRequirements() => application.Run(() =>
    {
        using var fixture = new ShellFixture(1);
        var catalog = fixture.Window.CreateFeatureCommands().ToDictionary(command => command.Id);
        foreach (var id in Ids("copy-selection cut-selection duplicate-selection delete-selection nudge-left nudge-right nudge-up nudge-down"))
            AssertUnavailable(catalog[id], "Select ink, text or images with Lasso or Select.");
        foreach (var id in Ids("text-font text-size text-bold text-italic text-color text-alignment bullet-list numbered-list continue-text finish-typing"))
            AssertUnavailable(catalog[id], "Select a text item first.");
        AssertUnavailable(catalog["restore-region"], "No earlier notebook edits in this session. Compatible page versions are checked when opening.");
        AssertUnavailable(catalog["undo-transfer"], "No page transfer to undo in this session.");
        AssertUnavailable(catalog["undo"], "No notebook edit to undo.");
        AssertUnavailable(catalog["redo"], "No notebook edit to redo.");
        AssertUnavailable(catalog["practice-attempts"], "Create a practice attempt first.");
        AssertUnavailable(catalog["move-page-up"], "This is the first page in its section.");
        AssertUnavailable(catalog["move-page-down"], "This is the last page in its section.");
        AssertUnavailable(catalog["move-page-section"], "Add another section first.");
        AssertUnavailable(catalog["delete-section"], "Keep at least one section in the notebook.");
        AssertUnavailable(catalog["check-updates"], "Updates are disabled for this isolated library.");
        Assert.True(catalog["text-formatting"].CanExecute);
        Assert.True(catalog["select-all"].CanExecute);
        Assert.True(catalog["paste"].CanExecute); // Availability must not inspect the system clipboard.
    });

    [Fact]
    public void AvailabilityIsRecheckedForBusyPenAndZoomLockWithoutChangingTheCapturedTarget() => application.Run(() =>
    {
        using var fixture = new ShellFixture(2);
        var catalog = fixture.Window.CreateFeatureCommands().ToDictionary(command => command.Id);
        var bookmark = catalog["bookmark-page"];
        Assert.True(bookmark.CanExecute);
        fixture.Model.IsBusy = true;
        AssertUnavailable(bookmark, "Finish the current operation first.", tryExecute: true);
        fixture.Model.IsBusy = false;
        Set(fixture.Window, "_viewportPenDeviceId", (int?)7);
        AssertUnavailable(bookmark, "Lift your pen before using this action.", tryExecute: true);
        Set(fixture.Window, "_viewportPenDeviceId", null);
        Assert.True(bookmark.CanExecute);
        Get<WorkspacePreferences>(fixture.Window, "_workspace").LockZoom = true;
        foreach (var id in Ids("fit-page fit-width actual-size zoom-in zoom-out"))
            AssertUnavailable(catalog[id], "Unlock zoom in Workspace settings first.", tryExecute: true);
        Get<WorkspacePreferences>(fixture.Window, "_workspace").LockZoom = false;
        Assert.True(catalog["fit-width"].CanExecute);
        Assert.False(fixture.Model.CanUndo); Assert.False(fixture.Model.Autosave.IsDirty);
    });

    [Fact]
    public void CapturedTextFormattingRejectsANewTextSelectionInsteadOfFormattingIt() => application.Run(() =>
    {
        using var fixture = new ShellFixture(1, document =>
        {
            document.Pages[0].Texts.Add(new() { Text = "First synthetic text", Y = 100 });
            document.Pages[0].Texts.Add(new() { Text = "Second synthetic text", Y = 300 });
        });
        var page = fixture.Model.SelectedPage!;
        var editor = fixture.Editor(page);
        Assert.True(editor.BeginTypingText(page.Page.Texts[0].Id));
        var bold = fixture.Window.CreateFeatureCommands().Single(command => command.Id == "text-bold");
        Assert.True(bold.CanExecute);
        Assert.True(editor.BeginTypingText(page.Page.Texts[1].Id));
        AssertUnavailable(bold, "Select a text item first.", tryExecute: true);
        Assert.All(page.Page.Texts, text => Assert.False(text.Bold));
        Assert.False(fixture.Model.CanUndo); Assert.False(fixture.Model.Autosave.IsDirty);
    });

    [Fact]
    public void CapturedPageCommandsKeepIdentityAfterReorderAndDoNotUseTheSelectedPage() => application.Run(() =>
    {
        using var fixture = new ShellFixture(3);
        fixture.Navigate(fixture.Model.Pages[1]);
        var target = fixture.Model.SelectedPage!.Page;
        var catalog = fixture.Window.CreateFeatureCommands().ToDictionary(command => command.Id);
        fixture.Model.MovePage(-1);
        fixture.Navigate(fixture.Model.Pages[2]);
        var other = fixture.Model.SelectedPage!.Page;
        Assert.Equal(target.Id, fixture.Model.Pages[0].Page.Id);
        Assert.True(catalog["bookmark-page"].TryExecute());
        Assert.True(target.IsBookmarked); Assert.False(other.IsBookmarked);
        Assert.Equal(target.Id, fixture.Model.SelectedPage!.Page.Id);
        Assert.True(catalog["bookmark-page"].IsChecked);
        fixture.Navigate(fixture.Model.Pages[2]);
        Assert.True(catalog["duplicate-page"].TryExecute());
        Assert.Equal(4, fixture.Model.Pages.Count);
        Assert.Equal(target.Id, fixture.Model.Pages[0].Page.Id);
        Assert.NotEqual(target.Id, fixture.Model.Pages[1].Page.Id);
        Assert.True(fixture.Model.Pages[1].Page.IsBookmarked);
        Assert.False(other.IsBookmarked);
    });

    [Theory]
    [InlineData("delete")]
    [InlineData("section")]
    [InlineData("home")]
    [InlineData("undo")]
    [InlineData("reopen")]
    public Task InvalidatedPageCommandsNeverFallBackToTheCurrentSelection(string transition) => application.RunAsync(async () =>
    {
        using var fixture = new ShellFixture(3);
        fixture.Navigate(fixture.Model.Pages[1]);
        var capturedDocument = fixture.Model.Document!;
        var capturedId = fixture.Model.SelectedPage!.Page.Id;
        var commands = fixture.Window.CreateFeatureCommands().Where(command => command.Id is "bookmark-page" or "duplicate-page").ToArray();
        Assert.All(commands, command => Assert.True(command.CanExecute));
        switch (transition)
        {
            case "delete": fixture.Model.DeletePage(); break;
            case "section": fixture.Model.AddSection("Another synthetic section"); break;
            case "home": await fixture.Model.ReturnToLibraryAsync(); break;
            case "undo": fixture.Model.DuplicatePage(); fixture.Model.Undo(); break;
            case "reopen": await fixture.Model.OpenAsync(capturedDocument.Id); break;
        }
        fixture.Layout();
        Assert.NotNull(fixture.Model.SelectedPage);
        if (transition is "undo" or "reopen")
        {
            Assert.NotSame(capturedDocument, fixture.Model.Document);
            Assert.Equal(capturedDocument.Id, fixture.Model.Document!.Id);
            Assert.Contains(fixture.Model.Pages, page => page.Page.Id == capturedId);
        }
        var before = JsonSerializer.Serialize(fixture.Model.Document);
        var selected = fixture.Model.SelectedPage;
        var historyChanges = 0;
        fixture.Model.HistoryChanged += (_, _) => historyChanges++;
        foreach (var command in commands)
        {
            Assert.False(command.CanExecute);
            Assert.False(string.IsNullOrWhiteSpace(command.UnavailableReason));
            Assert.False(command.TryExecute());
        }
        Assert.Same(selected, fixture.Model.SelectedPage);
        Assert.Equal(before, JsonSerializer.Serialize(fixture.Model.Document));
        Assert.Equal(0, historyChanges);
    });

    [Fact]
    public void CapturedSectionCommandReordersItsOriginalSectionAndRejectsUndoReplacement() => application.Run(() =>
    {
        using var fixture = new ShellFixture(1);
        fixture.Model.AddSection("Second synthetic section");
        fixture.Model.AddSection("Third synthetic section");
        fixture.Model.SelectedSection = fixture.Model.Sections[0]; fixture.Layout();
        var originalId = fixture.Model.SelectedSection!.Id;
        var command = fixture.Window.CreateFeatureCommands().Single(candidate => candidate.Id == "move-section-down");
        fixture.Model.SelectedSection = fixture.Model.Sections[2]; fixture.Layout();
        Assert.True(command.TryExecute());
        Assert.Equal(originalId, fixture.Model.Sections[1].Id);
        Assert.Equal(originalId, fixture.Model.SelectedSection!.Id);
        fixture.Model.Undo(); fixture.Layout();
        AssertUnavailable(command, "The notebook changed. Open its tools again.", tryExecute: true);
        Assert.Equal(originalId, fixture.Model.Sections[0].Id);
    });

    [Fact]
    public void ShowingTextFormattingDoesNotStartTypingAndOffersANoSelectionHint() => application.Run(() =>
    {
        using var fixture = new ShellFixture(1);
        var before = JsonSerializer.Serialize(fixture.Model.Document);
        var tool = Get<InkTool>(fixture.Window, "_tool");
        var request = Get<int>(fixture.Window, "_typingRequest");
        var changes = 0;
        fixture.Model.HistoryChanged += (_, _) => changes++;
        foreach (var control in new Control?[] { null, Find<ComboBox>(fixture, "TextFontPicker") })
        {
            Invoke(fixture.Window, "ShowTextFormatting", control); fixture.Layout();
            Assert.Equal(Visibility.Visible, Find<FrameworkElement>(fixture, "TextToolbar").Visibility);
            Assert.True(Get<bool?>(fixture.Window, "_textPanelOverride"));
            var hint = Find<TextBlock>(fixture, "TextSelectionHint");
            Assert.Equal(Visibility.Visible, hint.Visibility);
            Assert.Contains("Select a text item", hint.Text);
            foreach (var name in Ids("TextFontPicker TextSizePicker TextBoldButton TextItalicButton TextColorButton TextAlignmentPicker TextBulletsButton TextNumbersButton ContinueTextButton"))
                Assert.False(Find<Control>(fixture, name).IsEnabled);
            Assert.Equal(tool, Get<InkTool>(fixture.Window, "_tool"));
            Assert.Equal(request, Get<int>(fixture.Window, "_typingRequest"));
            Assert.Empty(fixture.Model.SelectedPage!.Page.Texts);
            Assert.Null(fixture.Editor(fixture.Model.SelectedPage).SelectedText);
            Invoke(fixture.Window, "HideTextFormattingClick", fixture.Window, new RoutedEventArgs());
            Invoke(fixture.Window, "UpdateTextToolbar");
            Assert.Equal(Visibility.Collapsed, Find<FrameworkElement>(fixture, "TextToolbar").Visibility);
            Assert.False(Get<bool?>(fixture.Window, "_textPanelOverride"));
        }
        Assert.Equal(before, JsonSerializer.Serialize(fixture.Model.Document));
        Assert.Equal(0, changes);
        Assert.False(fixture.Model.CanUndo); Assert.False(fixture.Model.Autosave.IsDirty);
    });

    [Fact]
    public Task TypingAndApplyingAPresetResetAnExplicitFormattingPanelOverride() => application.RunAsync(async () =>
    {
        using var fixture = new ShellFixture(1);
        Invoke(fixture.Window, "HideTextFormattingClick", fixture.Window, new RoutedEventArgs());
        Assert.False(Get<bool?>(fixture.Window, "_textPanelOverride"));
        await (Task)Invoke(fixture.Window, "StartTypingAsync", false)!;
        Assert.Null(Get<bool?>(fixture.Window, "_textPanelOverride"));
        Assert.Equal(InkTool.Text, Get<InkTool>(fixture.Window, "_tool"));
        Assert.Equal(Visibility.Visible, Find<FrameworkElement>(fixture, "TextToolbar").Visibility);
        Invoke(fixture.Window, "ShowTextFormatting", (object?)null);
        Assert.True(Get<bool?>(fixture.Window, "_textPanelOverride"));
        var preset = Get<WritingPreferences>(fixture.Window, "_preferences").Presets.First(candidate => candidate.Tool == InkTool.Pen);
        Invoke(fixture.Window, "ApplyPreset", preset, false);
        Assert.Null(Get<bool?>(fixture.Window, "_textPanelOverride"));
        Assert.Equal(InkTool.Pen, Get<InkTool>(fixture.Window, "_tool"));
        Assert.Equal(Visibility.Collapsed, Find<FrameworkElement>(fixture, "TextToolbar").Visibility);
    });

    [Theory]
    [InlineData(Key.Space)]
    [InlineData(Key.Enter)]
    public void FocusAllToolsRetainsNativeActivationKeysWithoutStartingTemporaryPan(Key key) => application.Run(() =>
    {
        using var fixture = new ShellFixture(1);
        // Apply only focus chrome; never maximize or show the detached Window.
        Set(fixture.Window, "_focusMode", true);
        Invoke(fixture.Window, "ApplyFocusChrome"); fixture.Layout();
        var button = Find<Button>(fixture, "FocusAllToolsButton");
        Assert.Same(button, Keyboard.Focus(button));
        Assert.True(Find<FrameworkElement>(fixture, "FocusWritingTools").IsKeyboardFocusWithin);
        var tool = Get<InkTool>(fixture.Window, "_tool");
        var args = fixture.PreviewWindowKey(key, button);
        Assert.False(args.Handled);
        Assert.Null(Get<InkTool?>(fixture.Window, "_toolBeforePan"));
        Assert.Equal(tool, Get<InkTool>(fixture.Window, "_tool"));
        Assert.False(fixture.Model.CanUndo); Assert.Empty(fixture.Model.SelectedPage!.Page.Texts);
        Keyboard.ClearFocus();
    });

    [Fact]
    public void FeatureMenuKeepsToolLettersAndSpaceOutOfEditorShortcutHandling() => application.Run(() =>
    {
        using var fixture = new ShellFixture(1);
        var menu = Find<Menu>(fixture, "FeatureMenu");
        var item = menu.Items.Cast<MenuItem>().First();
        Assert.Same(item, Keyboard.Focus(item));
        var tool = Get<InkTool>(fixture.Window, "_tool");
        foreach (var key in new[] { Key.H, Key.T, Key.Space, Key.Enter })
        {
            Assert.False(fixture.PreviewWindowKey(key, item).Handled);
            Assert.Equal(tool, Get<InkTool>(fixture.Window, "_tool"));
            Assert.Null(Get<InkTool?>(fixture.Window, "_toolBeforePan"));
        }
        Assert.Empty(fixture.Model.SelectedPage!.Page.Texts);
        Assert.False(fixture.Model.CanUndo);
        Keyboard.ClearFocus();
    });

    private static string[] Ids(string ids) => ids.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    private static void AssertUnavailable(WorkspaceCommand command, string reason, bool tryExecute = false)
    {
        Assert.Equal(reason, command.UnavailableReason);
        Assert.False(command.CanExecute);
        if (tryExecute) Assert.False(command.TryExecute());
    }
    private static T Find<T>(ShellFixture fixture, string name) where T : FrameworkElement
        => Assert.IsAssignableFrom<T>(fixture.Window.FindName(name));
    private static T Get<T>(object target, string name)
        => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Set(object target, string name, object? value)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object?[] args)
    {
        try { return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
    }
}
