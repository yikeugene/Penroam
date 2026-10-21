using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Moye.Controls;
using Moye.Models;
using L = Moye.Localization.LocalizationService;

namespace Moye;

public partial class MainWindow
{
    internal static readonly string[] FeatureCategories =
        ["Notebook", "Pages", "Insert & export", "Search", "Study", "View", "Tools", "Backup", "Help"];

    private static string FeatureCategoryKey(string category) => "Features.Category." + category.Replace(" & ", "").Replace(" ", "");

    private void InitializeFeatureNavigation()
    {
        foreach (var category in FeatureCategories)
        {
            var item = new MenuItem { MinHeight = 44, MinWidth = 44 };
            var categoryKey = FeatureCategoryKey(category);
            L.Bind(item, HeaderedItemsControl.HeaderProperty, categoryKey);
            L.Bind(item, AutomationProperties.NameProperty, "Features.CategoryActions", L.M(categoryKey));
            item.Items.Add(new MenuItem());
            item.SubmenuOpened += (_, e) =>
            {
                if (!ReferenceEquals(e.OriginalSource, item)) return;
                ClearTouches(); CloseSettingsPopups();
                item.Items.Clear();
                foreach (var command in CreateFeatureCommands().Where(command => command.Category == L.Text(FeatureCategoryKey(category)) && command.ParentId is null))
                    item.Items.Add(CreateFeatureMenuItem(command));
            };
            FeatureMenu.Items.Add(item);
        }
        ViewModel.PropertyChanged += (_, _) => UpdateFeatureNavigation();
        ReferenceHost.IsVisibleChanged += (_, _) => UpdateFeatureNavigation();
        UpdateFeatureNavigation();
    }

    private MenuItem CreateFeatureMenuItem(WorkspaceCommand command)
    {
        var reason = command.UnavailableReason;
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = command.Name, MaxWidth = 320, TextWrapping = TextWrapping.Wrap });
        if (!command.IsInstruction && reason is not null)
            heading.Children.Add(new TextBlock { Text = reason, MaxWidth = 320, TextWrapping = TextWrapping.Wrap,
                FontSize = 12, Foreground = (Brush)FindResource("MutedInk"), Margin = new Thickness(0, 3, 0, 0) });
        var item = new MenuItem { Header = heading, InputGestureText = command.Shortcut,
            IsEnabled = command.IsInstruction || command.CanExecute, IsCheckable = command.GetChecked is not null,
            IsChecked = command.IsChecked, ToolTip = command.Location + "\n" + command.Detail, Tag = command.Id };
        AutomationProperties.SetName(item, command.Name);
        ToolTipService.SetShowOnDisabled(item, true);
        if (command.Id is "delete-notebook" or "delete-page" or "delete-section" or "delete-selection")
            item.Style = (Style)FindResource("DangerMenuItem");
        item.Click += (_, e) =>
        {
            e.Handled = true;
            // Native menus close before another dialog or writing popup opens.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (_closing) return;
                if (command.IsInstruction) WorkspaceCommandDialog.Show(this, [command]);
                else if (!command.TryExecute()) ViewModel.Status = command.UnavailableReason ?? L.Text("Features.ThisToolIsNotAvailableHere");
            }));
        };
        return item;
    }

    private PlacementMode FeatureMenuPlacement => _focusMode
        ? (_workspace.FocusToolsOnRight ? PlacementMode.Left : PlacementMode.Right) : PlacementMode.Bottom;

    private Button FeatureActionAnchor(Button preferred) => _focusMode ? FocusAllToolsButton :
        Sidebar.Visibility == Visibility.Visible && PagesPanel.Visibility == Visibility.Visible ? preferred : AllToolsButton;

    private void OpenFeatureGroup(string parentId, IReadOnlyList<WorkspaceCommand> commands)
    {
        if (!CanOrganize) return;
        var menu = new ContextMenu { PlacementTarget = _focusMode ? FocusAllToolsButton : AllToolsButton,
            Placement = FeatureMenuPlacement };
        foreach (var command in commands.Where(command => command.ParentId == parentId)) menu.Items.Add(CreateFeatureMenuItem(command));
        CloseSettingsPopups(); ClearTouches(); menu.IsOpen = true;
    }

    private void UpdateFeatureNavigation()
    {
        if (TwoPagesButton is null || ReferenceViewButton is null) return;
        SetState(TwoPagesButton, _twoPageView, L.Text("Features.Command.two-pages.Name"));
        SetState(ReferenceViewButton, ReferenceHost.Visibility == Visibility.Visible, L.Text("Features.Command.reference-view.Name"));
        void SetState(Button button, bool active, string name)
        {
            button.Background = (Brush)FindResource(active ? "AccentSoft" : "Surface");
            button.BorderBrush = (Brush)FindResource(active ? "Accent" : "Stroke");
            button.Foreground = (Brush)FindResource(active ? "Accent" : "Ink");
            AutomationProperties.SetHelpText(button, L.Text(active ? "Features.ToggleOn" : "Features.ToggleOff", name));
        }
    }

    private void ToggleTwoPagesClick(object sender, RoutedEventArgs e)
    {
        if (!CanOrganize || ViewModel.IsLibraryVisible) return;
        ChangePageLayout(!_twoPageView); UpdateFeatureNavigation();
    }

    // A catalog captures identities, not row numbers. Availability is checked again
    // after the browser closes, so delayed dispatch cannot edit a replacement page.
    internal IReadOnlyList<WorkspaceCommand> CreateFeatureCommands()
    {
        var commands = new List<WorkspaceCommand>();
        var document = ViewModel.IsEditorVisible ? ViewModel.Document : null;
        var pageTarget = CapturePageActionTarget();
        var sectionTarget = document is not null && ViewModel.SelectedSection is { } section
            ? new SectionActionTarget(document, section.Id) : null;
        var editor = ViewModel.IsEditorVisible ? CurrentEditor : null;
        var selectedText = editor?.SelectedText;
        var transfer = _lastPageTransfer;
        var summary = document is null ? null : new NotebookSummary { Id = document.Id, Title = document.Title,
            Folder = document.Folder, IsPinned = document.IsPinned, CoverColor = document.CoverColor };

        string? Ready() => _closing ? L.Text("Features.ThisWindowIsClosing") : !_ready ? L.Text("Features.WaitForTheLibraryToFinishLoading") :
            ViewModel.IsBusy ? L.Text("Features.FinishTheCurrentOperationFirst") : AnyPenDown ? L.Text("Features.LiftYourPenBeforeUsingThisAction") : null;
        string? Notebook() => Ready() ?? (document is null || ViewModel.IsLibraryVisible ? L.Text("Messages.Transfer.OpenNotebookFirst") :
            !ReferenceEquals(document, ViewModel.Document) ? L.Text("Features.TheNotebookChangedOpenItsToolsAgain") : null);
        string? Page() => Notebook() ?? (pageTarget is null ? L.Text("Features.AddOrSelectAPageFirst") :
            ResolvePageActionTarget(ViewModel, pageTarget) is null ? L.Text("Features.ThePageChangedOpenItsActionsAgain") : null);
        string? Section() => Notebook() ?? (sectionTarget is null ? L.Text("Features.SelectASectionFirst") :
            ResolveSectionActionTarget(ViewModel, sectionTarget) is null ? L.Text("Features.TheSectionIsNoLongerAvailable") : null);
        string? EditingPage() => Page() ?? (editor is null || !ReferenceEquals(editor, CurrentEditor) ? L.Text("Features.SelectTheOriginalPageInTheWritingArea") :
            editor.IsTextComposing ? L.Text("Features.FinishComposingTextFirst") : null);
        string? Selection() => EditingPage() ?? (editor?.HasSelection == true ? null : L.Text("Features.SelectInkTextOrImagesWithLassoOr"));
        string? Text() => EditingPage() ?? (selectedText is null || !ReferenceEquals(editor?.SelectedText, selectedText) ? L.Text("Features.SelectATextItemFirst") : null);
        string? Zoom() => Page() ?? (_workspace.LockZoom ? L.Text("Features.UnlockZoomInWorkspaceSettingsFirst") : null);
        string? Library() => Ready() ?? (ViewModel.IsLibraryVisible ? null : L.Text("Features.ReturnToMyNotebooksFirst"));

        void Add(string id, string name, string category, string detail, Action? execute, Func<string?>? available = null,
            string shortcut = "", string keywords = "", string? parent = null, string? location = null, Func<bool>? check = null)
        {
            category = L.Text(FeatureCategoryKey(category));
            var parentPath = parent is null ? null : commands.FirstOrDefault(command => command.Id == parent)?.Location;
            commands.Add(new(name, detail, execute)
            {
                Id = id, Category = category, Shortcut = shortcut, Keywords = keywords, ParentId = parent,
                Location = location ?? (parentPath is null ? category + " → " + name : parentPath + " → " + name),
                GetUnavailableReason = available ?? Ready, GetChecked = check
            });
        }
        void Info(string id, string name, string category, string detail, string? parent = null, string keywords = "")
            => Add(id, name, category, detail, null, keywords: keywords, parent: parent);
        void PageAction(string id, string name, string detail, Action<PageActionTarget> action, Func<string?>? available = null,
            string? parent = "page-actions", string category = "Pages", Func<bool>? check = null)
            => Add(id, name, category, detail, () => { if (pageTarget is not null) action(pageTarget); }, available ?? Page, parent: parent, check: check);
        void Setting(string id, string name, string detail, string focus, string category = "View", string parent = "workspace-settings")
            => Add(id, name, category, detail, () => ShowWorkspaceSettings(focus), parent: parent);
        void Format(string id, string name, string detail, Control control, string keywords = "")
            => Add(id, name, "Tools", detail, () => ShowTextFormatting(control), Text, parent: "text-formatting", keywords: keywords);

        Add("new-notebook", L.Text("Features.Command.new-notebook.Name"), "Notebook", L.Text("Features.Command.new-notebook.Detail"), () => NewNoteClick(this, new()));
        Add("open-notebooks", L.Text("Features.Command.open-notebooks.Name"), "Notebook", L.Text("Features.Command.open-notebooks.Detail"), () => ShowNotebooksClick(this, new()));
        Add("continue-notebook", L.Text("Features.Command.continue-notebook.Name"), "Notebook", L.Text("Features.Command.continue-notebook.Detail"), () => ContinueLastClick(this, new()),
            () => Ready() ?? (string.IsNullOrWhiteSpace(_workspace.LastNotebookId) ? L.Text("Features.OpenANotebookToSaveAReadingPosition") : null));
        Add("quick-note", L.Text("Features.Command.quick-note.Name"), "Notebook", L.Text("Features.Command.quick-note.Detail"), () => QuickNoteClick(this, new()));
        Add("rename-notebook", L.Text("Features.Command.rename-notebook.Name"), "Notebook", L.Text("Features.Command.rename-notebook.Detail"), () => RenameClick(this, new()), Notebook);
        Add("pin-notebook", L.Text("Features.Command.pin-notebook.Name"), "Notebook", L.Text("Features.Command.pin-notebook.Detail"),
            () => { if (summary is not null) ToggleNotebookPin(summary); }, Notebook, check: () => document?.IsPinned == true);
        Add("cover-color", L.Text("Features.Command.cover-color.Name"), "Notebook", L.Text("Features.Command.cover-color.Detail"),
            () => { if (summary is not null) EditNotebookAppearance(summary); }, Notebook);
        Add("delete-notebook", L.Text("Features.Command.delete-notebook.Name"), "Notebook", L.Text("Features.Command.delete-notebook.Detail"), () => DeleteNotebookClick(this, new()), Notebook);
        Add("sort-library", L.Text("Features.Command.sort-library.Name"), "Notebook", L.Text("Features.Command.sort-library.Detail"), () => LibraryOrganizationClick(this, new()));
        Add("find-notebook", L.Text("Features.Command.find-notebook.Name"), "Notebook", L.Text("Features.Command.find-notebook.Detail"),
            () => { if (!ViewModel.IsLibraryVisible) ShowSidebarTab(true); var field = ViewModel.IsLibraryVisible ? HomeSearch : LibrarySearch; field.Focus(); field.SelectAll(); });
        Add("clear-library-search", L.Text("Features.Command.clear-library-search.Name"), "Notebook", L.Text("Features.Command.clear-library-search.Detail"), () => ClearSearchClick(this, new()),
            () => Ready() ?? (ViewModel.HasSearch ? null : L.Text("Features.NoNotebookFilterIsActive")), parent: "find-notebook");

        Add("add-page", L.Text("Features.Command.add-page.Name"), "Pages", L.Text("Features.Command.add-page.Detail"), () => ShowPaperPicker(true), Notebook);
        Add("page-actions", L.Text("Features.Command.page-actions.Name"), "Pages", L.Text("Features.Command.page-actions.Detail"),
            () => { if (pageTarget is not null) ShowCapturedPageActions(pageTarget); }, Page);
        PageAction("rename-page", L.Text("Features.Command.rename-page.Name"), L.Text("Features.Command.rename-page.Detail"), RenamePage);
        PageAction("bookmark-page", L.Text("Features.Command.bookmark-page.Name"), L.Text("Features.Command.bookmark-page.Detail"), TogglePageBookmark,
            check: () => pageTarget is not null && ResolvePageActionTarget(ViewModel, pageTarget)?.Page.IsBookmarked == true);
        PageAction("duplicate-page", L.Text("Features.Command.duplicate-page.Name"), L.Text("Features.Command.duplicate-page.Detail"), target => RunPageAction(target, ViewModel.DuplicatePage));
        PageAction("move-page-up", L.Text("Features.Command.move-page-up.Name"), L.Text("Features.Command.move-page-up.Detail"), target => RunPageAction(target, () => ViewModel.MovePage(-1)),
            () => Page() ?? (pageTarget is not null && ViewModel.Pages.IndexOf(ResolvePageActionTarget(ViewModel, pageTarget)!) > 0 ? null : L.Text("Features.ThisIsTheFirstPageInItsSection")));
        PageAction("move-page-down", L.Text("Features.Command.move-page-down.Name"), L.Text("Features.Command.move-page-down.Detail"), target => RunPageAction(target, () => ViewModel.MovePage(1)),
            () => Page() ?? (pageTarget is not null && ViewModel.Pages.IndexOf(ResolvePageActionTarget(ViewModel, pageTarget)!) < ViewModel.Pages.Count - 1 ? null : L.Text("Features.ThisIsTheLastPageInItsSection")));
        PageAction("move-page-section", L.Text("Features.Command.move-page-section.Name"), L.Text("Features.Command.move-page-section.Detail"), ShowMovePageToSection,
            () => Page() ?? (ViewModel.Sections.Count > 1 ? null : L.Text("Features.AddAnotherSectionFirst")));
        PageAction("delete-page", L.Text("Features.Command.delete-page.Name"), L.Text("Features.Command.delete-page.Detail"), DeletePage);
        PageAction("paper-style", L.Text("Features.Command.paper-style.Name"), L.Text("Features.Command.paper-style.Detail"), ShowPageSettings,
            () => Page() ?? (pageTarget is not null && ResolvePageActionTarget(ViewModel, pageTarget)?.Page.Pdf is not null ? L.Text("Features.PDFPagesKeepTheirBackgroundAddAPaper") : null), parent: null);
        PageAction("extend-page", L.Text("Features.Command.extend-page.Name"), L.Text("Features.Command.extend-page.Detail"), ExtendPage, parent: null);
        Add("add-section", L.Text("Features.Command.add-section.Name"), "Pages", L.Text("Features.Command.add-section.Detail"), () => AddSectionClick(this, new()), Notebook);
        Add("section-actions", L.Text("Features.Command.section-actions.Name"), "Pages", L.Text("Features.Command.section-actions.Detail"),
            () => { if (sectionTarget is not null && ResolveSectionActionTarget(ViewModel, sectionTarget) is { } target)
                OpenSectionContextMenu(target, FeatureActionAnchor(SectionOptionsButton), FeatureMenuPlacement); }, Section);
        Add("rename-section", L.Text("Features.Command.rename-section.Name"), "Pages", L.Text("Features.Command.rename-section.Detail"), () => { if (sectionTarget is not null) RenameSection(sectionTarget); }, Section, parent: "section-actions");
        Add("move-section-up", L.Text("Features.Command.move-section-up.Name"), "Pages", L.Text("Features.Command.move-section-up.Detail"), () => { if (sectionTarget is not null) RunSectionAction(sectionTarget, () => ViewModel.MoveSection(-1)); },
            () => Section() ?? (ViewModel.Sections.IndexOf(ResolveSectionActionTarget(ViewModel, sectionTarget!)!) > 0 ? null : L.Text("Features.ThisIsTheFirstSection")), parent: "section-actions");
        Add("move-section-down", L.Text("Features.Command.move-section-down.Name"), "Pages", L.Text("Features.Command.move-section-down.Detail"), () => { if (sectionTarget is not null) RunSectionAction(sectionTarget, () => ViewModel.MoveSection(1)); },
            () => Section() ?? (ViewModel.Sections.IndexOf(ResolveSectionActionTarget(ViewModel, sectionTarget!)!) < ViewModel.Sections.Count - 1 ? null : L.Text("Features.ThisIsTheLastSection")), parent: "section-actions");
        Add("delete-section", L.Text("Features.Command.delete-section.Name"), "Pages", L.Text("Features.Command.delete-section.Detail"), () => { if (sectionTarget is not null) DeleteSection(sectionTarget); },
            () => Section() ?? (ViewModel.Sections.Count > 1 ? null : L.Text("Features.KeepAtLeastOneSectionInTheNotebook")), parent: "section-actions");
        Add("manage-pages", L.Text("Features.Command.manage-pages.Name"), "Pages", L.Text("Features.Command.manage-pages.Detail"), () => ManagePagesClick(this, new()), Notebook);
        Add("transfer-pages", L.Text("Features.Command.transfer-pages.Name"), "Pages", L.Text("Features.Command.transfer-pages.Detail"),
            () => ManagePagesClick(this, new()), Notebook, parent: "manage-pages");
        Add("undo-transfer", L.Text("Features.Command.undo-transfer.Name"), "Pages", L.Text("Features.Command.undo-transfer.Detail"), () => UndoPageTransferClick(this, new()),
            () => Notebook() ?? (transfer is null ? L.Text("Features.NoPageTransferToUndoInThisSession") : !ReferenceEquals(transfer, _lastPageTransfer) ? L.Text("Features.TheTransferChangedOpenItsToolsAgain") : null));

        Add("import-document", L.Text("Features.Command.import-document.Name"), "Insert & export", L.Text("Features.Command.import-document.Detail"), () => ImportDocumentWorkflowClick(this, new()), Notebook,
            keywords: "Word PowerPoint LibreOffice slides page ranges selected pages");
        Add("import-folder", L.Text("Features.Command.import-folder.Name"), "Insert & export", L.Text("Features.Command.import-folder.Detail"), () => ImportFolderClick(this, new()), Library);
        Add("insert-image", L.Text("Features.Command.insert-image.Name"), "Insert & export", L.Text("Features.Command.insert-image.Detail"), () => RunOnCapturedPage(() => ImageClick(this, new())), Page,
            keywords: "photo screenshot picture");
        Add("export-pdf", L.Text("Features.Command.export-pdf.Name"), "Insert & export", L.Text("Features.Command.export-pdf.Detail"), () => ExportDocumentWorkflowClick(this, new()), Notebook);
        Add("a4-export", L.Text("Features.Command.a4-export.Name"), "Insert & export", L.Text("Features.Command.a4-export.Detail"),
            () => ExportDocumentWorkflowClick(this, new()), Notebook, parent: "export-pdf", keywords: "print printing tile tiling overlap orientation");
        Info("export-overflow", L.Text("Features.Command.export-overflow.Name"), "Insert & export", L.Text("Features.Command.export-overflow.Detail"), "export-pdf");
        Info("export-completion", L.Text("Features.Command.export-completion.Name"), "Insert & export", L.Text("Features.Command.export-completion.Detail"), "export-pdf");

        Add("search-content", L.Text("Features.Command.search-content.Name"), "Search", L.Text("Features.Command.search-content.Detail"), () => SearchContentsClick(this, new()), shortcut: "Ctrl+Shift+F", keywords: "find content text PDF");
        Add("search-appearance", L.Text("Features.Command.search-appearance.Name"), "Search", L.Text("Features.Command.search-appearance.Detail"), () => VisualSearchClick(this, new()), Notebook);
        Add("bookmarks", L.Text("Features.Command.bookmarks.Name"), "Search", L.Text("Features.Command.bookmarks.Detail"), () => ShowBookmarksClick(this, new()));
        Add("go-to-page", L.Text("Features.Command.go-to-page.Name"), "Search", L.Text("Features.Command.go-to-page.Detail"), () => GoToPageClick(this, new()), Page, "Ctrl+G");
        Add("back-to-page", L.Text("Features.Command.back-to-page.Name"), "Search", L.Text("Features.Command.back-to-page.Detail"), () => NavigateBackClick(this, new()),
            () => Ready() ?? (_organizationBack.Count > 0 ? null : L.Text("Shell.NoPreviousLocation")));

        PageAction("mark-question", L.Text("Features.Command.mark-question.Name"), L.Text("Features.Command.mark-question.Detail"), AddStudyQuestion, parent: null, category: "Study");
        Add("question-inbox", L.Text("Features.Command.question-inbox.Name"), "Study", L.Text("Features.Command.question-inbox.Detail"), () => StudyInboxClick(this, new()), Notebook);
        PageAction("resume-hint", L.Text("Features.Command.resume-hint.Name"), L.Text("Features.Command.resume-hint.Detail"), SetResumeHint, parent: null, category: "Study");
        Add("resume-thinking", L.Text("Features.Command.resume-thinking.Name"), "Study", L.Text("Features.Command.resume-thinking.Detail"), () => ResumeStudyClick(this, new()), Notebook);
        PageAction("new-practice", L.Text("Features.Command.new-practice.Name"), L.Text("Features.Command.new-practice.Detail"), NewPracticeAttempt, parent: null, category: "Study");
        Add("practice-attempts", L.Text("Features.Command.practice-attempts.Name"), "Study", L.Text("Features.Command.practice-attempts.Detail"), () => PracticeAttemptsClick(this, new()),
            () => Notebook() ?? (document?.Pages.Any(page => page.Practice is not null) == true ? null : L.Text("Features.CreateAPracticeAttemptFirst")));
        PageAction("restore-region", L.Text("Features.Command.restore-region.Name"), L.Text("Features.Command.restore-region.Detail"), RestoreRegion,
            () => Page() ?? (ViewModel.CanUndo ? null : L.Text("Features.NoEarlierNotebookEditsInThisSessionCompatible")), parent: null, category: "Study");

        Add("single-page", L.Text("Features.Command.single-page.Name"), "View", L.Text("Features.Command.single-page.Detail"), () => ChangePageLayout(false), Notebook, check: () => !_twoPageView);
        Add("two-pages", L.Text("Features.Command.two-pages.Name"), "View", L.Text("Features.Command.two-pages.Detail"), () => ChangePageLayout(true), Notebook, check: () => _twoPageView);
        Add("fit-page", _twoPageView ? "Fit spread" : "Fit page", "View", L.Text("Features.FitTheCompletePageOrTwoPageSpread"), FitPage, Zoom);
        Add("fit-width", L.Text("Features.Command.fit-width.Name"), "View", L.Text("Features.Command.fit-width.Detail"), FitWidth, Zoom, check: () => _fitWidthActive);
        Add("actual-size", L.Text("Features.Command.actual-size.Name"), "View", L.Text("Features.Command.actual-size.Detail"), () => ActualSizeClick(this, new()), Zoom);
        Add("zoom-in", L.Text("Features.Command.zoom-in.Name"), "View", L.Text("Features.Command.zoom-in.Detail"), () => ZoomInClick(this, new()), Zoom);
        Add("zoom-out", L.Text("Features.Command.zoom-out.Name"), "View", L.Text("Features.Command.zoom-out.Detail"), () => ZoomOutClick(this, new()), Zoom);
        Add("sidebar", L.Text("Features.Command.sidebar.Name"), "View", L.Text("Features.Command.sidebar.Detail"), ToggleSidebar, Notebook, "F9", check: () => Sidebar.Visibility == Visibility.Visible);
        Add("focus-mode", L.Text("Features.Command.focus-mode.Name"), "View", L.Text("Features.Command.focus-mode.Detail"), ToggleFocus, Notebook, "F11", check: () => _focusMode);
        Add("reference-view", L.Text("Features.Command.reference-view.Name"), "View", L.Text("Features.Command.reference-view.Detail"), () => ShowReferenceClick(this, new()), Notebook,
            check: () => ReferenceHost.Visibility == Visibility.Visible);
        Add("workspace-settings", L.Text("Features.Command.workspace-settings.Name"), "View", L.Text("Features.Command.workspace-settings.Detail"), () => ShowWorkspaceSettings());
        Setting("two-finger-navigation", L.Text("Features.Command.two-finger-navigation.Name"), L.Text("Features.Command.two-finger-navigation.Detail"), "TwoFingerNavigation");
        Setting("zoom-lock", L.Text("Features.Command.zoom-lock.Name"), L.Text("Features.Command.zoom-lock.Detail"), "LockZoom");
        Setting("compact-toolbar", L.Text("Features.Command.compact-toolbar.Name"), L.Text("Features.Command.compact-toolbar.Detail"), "CompactToolbar");
        Setting("focus-docking", L.Text("Features.Command.focus-docking.Name"), L.Text("Features.Command.focus-docking.Detail"), "FocusToolsDock");

        Add("pen", L.Text("Features.Command.pen.Name"), "Tools", L.Text("Features.Command.pen.Detail"), () => SetTool(InkTool.Pen), Notebook, "B", check: () => _tool == InkTool.Pen);
        Add("highlighter", L.Text("Features.Command.highlighter.Name"), "Tools", L.Text("Features.Command.highlighter.Detail"), () => SetTool(InkTool.Highlighter), Notebook, "H", check: () => _tool == InkTool.Highlighter);
        Add("eraser", L.Text("Features.Command.eraser.Name"), "Tools", L.Text("Features.Command.eraser.Detail"), () => SetTool(_eraserTool), Notebook, "E", check: () => _tool is InkTool.PointEraser or InkTool.StrokeEraser);
        Add("lasso", L.Text("Features.Command.lasso.Name"), "Tools", L.Text("Features.Command.lasso.Detail"), () => SetTool(InkTool.Lasso), Notebook, "L", check: () => _tool == InkTool.Lasso);
        Add("type", L.Text("Features.Command.type.Name"), "Tools", L.Text("Features.Command.type.Detail"), () => RunOnCapturedPage(() => TypeClick(this, new())), Page, "T", check: () => _tool == InkTool.Text);
        Add("select", L.Text("Features.Command.select.Name"), "Tools", L.Text("Features.Command.select.Detail"), () => SetTool(InkTool.Select), Notebook, "V", check: () => _tool == InkTool.Select);
        Add("pen-settings", L.Text("Features.Command.pen-settings.Name"), "Tools", L.Text("Features.Command.pen-settings.Detail"), () => OpenPenSettings(), Notebook);
        Add("ink-color", L.Text("Features.Command.ink-color.Name"), "Tools", L.Text("Features.Command.ink-color.Detail"), () => OpenPenSettings(ColorPalette), Notebook, parent: "pen-settings");
        Add("more-colors", L.Text("Features.Command.more-colors.Name"), "Tools", L.Text("Features.Command.more-colors.Detail"), () => CustomColorClick(this, new()), Notebook, parent: "pen-settings");
        Add("ink-width", L.Text("Features.Command.ink-width.Name"), "Tools", L.Text("Features.Command.ink-width.Detail"), () => OpenPenSettings(WidthPicker), Notebook, parent: "pen-settings");
        Add("draw-hold", L.Text("Features.Command.draw-hold.Name"), "Tools", L.Text("Features.Command.draw-hold.Detail"), () => OpenPenSettings(HoldToStraightenToggle), Notebook,
            parent: "pen-settings", check: () => HoldToStraightenToggle.IsChecked == true);
        Add("save-pen-preset", L.Text("Features.Command.save-pen-preset.Name"), "Tools", L.Text("Features.Command.save-pen-preset.Detail"), () => SavePresetClick(this, new()),
            () => Notebook() ?? (_preferences.Presets.Count < 40 ? null : L.Text("Features.RemoveAPresetInManagePensFirst")), parent: "pen-settings");
        Add("manage-pens", L.Text("Features.Command.manage-pens.Name"), "Tools", L.Text("Features.Command.manage-pens.Detail"), () => ShowPresetManager(), Notebook);
        Add("pen-opacity", L.Text("Features.Command.pen-opacity.Name"), "Tools", L.Text("Features.Command.pen-opacity.Detail"), () => ShowPresetManager("Opacity"), Notebook, parent: "manage-pens");
        Add("pressure", L.Text("Features.Command.pressure.Name"), "Tools", L.Text("Features.Command.pressure.Detail"), () => ShowPresetManager("Pressure"), Notebook, parent: "manage-pens");
        Add("smoothing", L.Text("Features.Command.smoothing.Name"), "Tools", L.Text("Features.Command.smoothing.Detail"), () => ShowPresetManager("Smoothing"), Notebook, parent: "manage-pens");
        Add("favorite-pens", L.Text("Features.Command.favorite-pens.Name"), "Tools", L.Text("Features.Command.favorite-pens.Detail"), () => ShowPresetManager("Favorite"), Notebook, parent: "manage-pens");
        var favorites = _preferences.Presets.Where(preset => preset.IsFavorite).Take(9).ToArray();
        for (var index = 0; index < 9; index++)
        {
            var preset = favorites.ElementAtOrDefault(index);
            Add($"favorite-{index + 1}", preset is null ? L.Text("Features.FavoriteEmpty", index + 1) : L.Text("Features.FavoriteNamed", index + 1, preset.Name), "Tools",
                L.Text("Features.UseThisSavedFavoriteTheSameShortcutWorks"),
                () => { if (preset is not null) ApplyPreset(preset); },
                () => Notebook() ?? (preset is null || !_preferences.Presets.Any(current => current.Id == preset.Id && current.IsFavorite) ? L.Text("Features.ChooseThisFavoriteInManagePensFirst") : null),
                (index + 1).ToString(), parent: "manage-pens");
        }
        Add("eraser-settings", L.Text("Features.Command.eraser-settings.Name"), "Tools", L.Text("Features.Command.eraser-settings.Detail"), () => OpenEraserSettings(), Notebook);
        Add("pixel-eraser", L.Text("Features.Command.pixel-eraser.Name"), "Tools", L.Text("Features.Command.pixel-eraser.Detail"), () => SetTool(InkTool.PointEraser), Notebook,
            parent: "eraser-settings", check: () => _eraserTool == InkTool.PointEraser);
        Add("stroke-eraser", L.Text("Features.Command.stroke-eraser.Name"), "Tools", L.Text("Features.Command.stroke-eraser.Detail"), () => SetTool(InkTool.StrokeEraser), Notebook,
            parent: "eraser-settings", check: () => _eraserTool == InkTool.StrokeEraser);
        Add("eraser-size", L.Text("Features.Command.eraser-size.Name"), "Tools", L.Text("Features.Command.eraser-size.Detail"), () => OpenEraserSettings(EraserSizeSlider), Notebook, parent: "eraser-settings");
        Add("erase-highlighter-only", L.Text("Features.Command.erase-highlighter-only.Name"), "Tools", L.Text("Features.Command.erase-highlighter-only.Detail"), () => OpenEraserSettings(HighlightOnlyToggle), Notebook,
            parent: "eraser-settings", check: () => HighlightOnlyToggle.IsChecked == true);

        Add("text-formatting", L.Text("Features.Command.text-formatting.Name"), "Tools", L.Text("Features.Command.text-formatting.Detail"), () => ShowTextFormatting(), Notebook);
        Add("add-text-box", L.Text("Features.Command.add-text-box.Name"), "Tools", L.Text("Features.Command.add-text-box.Detail"), () => RunOnCapturedPage(() => AddTextBoxClick(this, new())), Page, parent: "text-formatting");
        Format("text-font", L.Text("Features.Command.text-font.Name"), L.Text("Features.Command.text-font.Detail"), TextFontPicker);
        Format("text-size", L.Text("Features.Command.text-size.Name"), L.Text("Features.Command.text-size.Detail"), TextSizePicker);
        Add("text-bold", L.Text("Features.Command.text-bold.Name"), "Tools", L.Text("Features.Command.text-bold.Detail"), () => TextBoldClick(this, new()), Text, "Ctrl+B", parent: "text-formatting");
        Add("text-italic", L.Text("Features.Command.text-italic.Name"), "Tools", L.Text("Features.Command.text-italic.Detail"), () => TextItalicClick(this, new()), Text, "Ctrl+I", parent: "text-formatting");
        Add("text-color", L.Text("Features.Command.text-color.Name"), "Tools", L.Text("Features.Command.text-color.Detail"), () => TextColorClick(this, new()), Text, parent: "text-formatting");
        Format("text-alignment", L.Text("Features.Command.text-alignment.Name"), L.Text("Features.Command.text-alignment.Detail"), TextAlignmentPicker);
        Add("bullet-list", L.Text("Features.Command.bullet-list.Name"), "Tools", L.Text("Features.Command.bullet-list.Detail"), () => TextBulletsClick(this, new()), Text, parent: "text-formatting");
        Add("numbered-list", L.Text("Features.Command.numbered-list.Name"), "Tools", L.Text("Features.Command.numbered-list.Detail"), () => TextNumberingClick(this, new()), Text, parent: "text-formatting");
        Add("continue-text", L.Text("Features.Command.continue-text.Name"), "Tools", L.Text("Features.Command.continue-text.Detail"), () => ContinueTextOnNextPageClick(this, new()),
            () => Text() ?? (editor?.HasTextOverflow == true ? null : L.Text("Features.TheSelectedTextAlreadyFitsOnItsPage")), parent: "text-formatting");
        Add("finish-typing", L.Text("Features.Command.finish-typing.Name"), "Tools", L.Text("Features.Command.finish-typing.Detail"), FinishTyping, Text, "Ctrl+Enter", parent: "text-formatting");
        Add("selection-actions", L.Text("Features.Command.selection-actions.Name"), "Tools", L.Text("Features.Command.selection-actions.Detail"), () => OpenFeatureGroup("selection-actions", commands), Page);
        Add("select-all", L.Text("Features.Command.select-all.Name"), "Tools", L.Text("Features.Command.select-all.Detail"), () => RunOnCapturedPage(() => { SetTool(InkTool.Lasso); CurrentEditor?.SelectAllContent(); }), Page, "Ctrl+A", parent: "selection-actions");
        Add("copy-selection", L.Text("Features.Command.copy-selection.Name"), "Tools", L.Text("Features.Command.copy-selection.Detail"), () => CopyInk(false), Selection, "Ctrl+C", parent: "selection-actions");
        Add("cut-selection", L.Text("Features.Command.cut-selection.Name"), "Tools", L.Text("Features.Command.cut-selection.Detail"), () => CopyInk(true), Selection, "Ctrl+X", parent: "selection-actions");
        Add("paste", L.Text("Features.Command.paste.Name"), "Tools", L.Text("Features.Command.paste.Detail"), () => PasteContentClick(this, new()), EditingPage, "Ctrl+V", parent: "selection-actions");
        Add("duplicate-selection", L.Text("Features.Command.duplicate-selection.Name"), "Tools", L.Text("Features.Command.duplicate-selection.Detail"), () => editor?.DuplicateSelection(), Selection, "Ctrl+D", parent: "selection-actions");
        Add("delete-selection", L.Text("Features.Command.delete-selection.Name"), "Tools", L.Text("Features.Command.delete-selection.Detail"), () => editor?.DeleteSelection(), Selection, "Delete", parent: "selection-actions");
        foreach (var (id, x, y, shortcut) in new[] { ("left", -1, 0, "Left"), ("right", 1, 0, "Right"), ("up", 0, -1, "Up"), ("down", 0, 1, "Down") })
            Add("nudge-" + id, L.Text("Features.Nudge." + id), "Tools", L.Text("Features.MoveByOnePageDIPShiftArrowKeys"),
                () => editor?.NudgeSelection(x, y), Selection, shortcut, parent: "selection-actions");
        Add("undo", L.Text("Features.Command.undo.Name"), "Tools", L.Text("Features.Command.undo.Detail"), () => UndoClick(this, new()),
            () => Notebook() ?? (ViewModel.CanUndo ? null : L.Text("Features.NoNotebookEditToUndo")), "Ctrl+Z");
        Add("redo", L.Text("Features.Command.redo.Name"), "Tools", L.Text("Features.Command.redo.Detail"), () => RedoClick(this, new()),
            () => Notebook() ?? (ViewModel.CanRedo ? null : L.Text("Features.NoNotebookEditToRedo")), "Ctrl+Y / Ctrl+Shift+Z");

        Add("save-now", L.Text("Features.Command.save-now.Name"), "Backup", L.Text("Features.Command.save-now.Detail"), SaveNow, Notebook, "Ctrl+S");
        Add("retry-save", L.Text("Features.Command.retry-save.Name"), "Backup", L.Text("Features.Command.retry-save.Detail"), () => RetrySaveClick(this, new()));
        Add("backup-notebook", L.Text("Features.Command.backup-notebook.Name"), "Backup", L.Text("Features.Command.backup-notebook.Detail"), () => BackupNoteClick(this, new()), Notebook);
        Add("backup-all", L.Text("Features.Command.backup-all.Name"), "Backup", L.Text("Features.Command.backup-all.Detail"), () => BackupAllClick(this, new()));
        Add("restore-backup", L.Text("Features.Command.restore-backup.Name"), "Backup", L.Text("Features.Command.restore-backup.Detail"), () => RestoreSelectedClick(this, new()));
        Add("backup-recovery", L.Text("Features.Command.backup-recovery.Name"), "Backup", L.Text("Features.Command.backup-recovery.Detail"), () => SafetyCenterClick(this, new()));
        Add("recover-drafts", L.Text("Features.Command.recover-drafts.Name"), "Backup", L.Text("Features.Command.recover-drafts.Detail"), () => SafetyCenterClick(this, new()), parent: "backup-recovery");
        Add("recently-deleted", L.Text("Features.Command.recently-deleted.Name"), "Backup", L.Text("Features.Command.recently-deleted.Detail"), () => SafetyCenterClick(this, new()), parent: "backup-recovery");
        Add("automatic-backups", L.Text("Features.Command.automatic-backups.Name"), "Backup", L.Text("Features.Command.automatic-backups.Detail"), () => ShowWorkspaceSettings("BackupEnabled"));
        Setting("backup-folder", L.Text("Features.Command.backup-folder.Name"), L.Text("Features.Command.backup-folder.Detail"), "BackupFolder", "Backup", "automatic-backups");
        Setting("backup-interval", L.Text("Features.Command.backup-interval.Name"), L.Text("Features.Command.backup-interval.Detail"), "BackupInterval", "Backup", "automatic-backups");
        Setting("backup-retention", L.Text("Features.Command.backup-retention.Name"), L.Text("Features.Command.backup-retention.Detail"), "BackupRetention", "Backup", "automatic-backups");

        Add("check-updates", L.Text("Features.Command.check-updates.Name"), "Help", L.Text("Features.Command.check-updates.Detail"), () => CheckForUpdatesClick(this, new()),
            () => Ready() ?? (_updateService is null ? L.Text("Features.UpdatesAreDisabledForThisIsolatedLibrary") : null));
        Add("writing-settings-warning", L.Text("Features.Command.writing-settings-warning.Name"), "Help", L.Text("Features.Command.writing-settings-warning.Detail"), () => RetryPreferencesClick(this, new()));
        Info("keyboard-help", L.Text("Features.Command.keyboard-help.Name"), "Help", L.Text("Features.Command.keyboard-help.Detail"), keywords: "hotkeys shortcuts navigation");
        Info("pan-touch-help", L.Text("Features.Command.pan-touch-help.Name"), "Help", L.Text("Features.Command.pan-touch-help.Detail"), keywords: "Space mouse touch palm tail eraser pressure");
        Info("typing-help", L.Text("Features.Command.typing-help.Name"), "Help", L.Text("Features.Command.typing-help.Detail"), keywords: "Chinese input text boxes baseline");
        Info("autosave-help", L.Text("Features.Command.autosave-help.Name"), "Help", L.Text("Features.Command.autosave-help.Detail"), keywords: "offline autosave scroll position preferences");
        Info("document-help", L.Text("Features.Command.document-help.Name"), "Help", L.Text("Features.Command.document-help.Detail"), keywords: "Word PowerPoint PDF LibreOffice ODT ODP DOCX PPSX conversion");
        Info("isolated-library-help", L.Text("Features.Command.isolated-library-help.Name"), "Help", L.Text("Features.Command.isolated-library-help.Detail"), keywords: "data directory database portable");
        return commands;

        void RunOnCapturedPage(Action action)
        {
            if (pageTarget is not null) RunPageAction(pageTarget, action);
        }
    }

    private void ShowCapturedPageActions(PageActionTarget target)
    {
        if (!CanOrganize || ResolvePageActionTarget(ViewModel, target) is not { } page || CreatePageContextMenu(page) is not { } menu) return;
        menu.PlacementTarget = FeatureActionAnchor(PageActionsButton);
        menu.Placement = FeatureMenuPlacement;
        ClearTouches(); CloseSettingsPopups(); menu.IsOpen = true;
    }

    private void OpenPenSettings(FrameworkElement? focus = null)
    {
        if (!CanOrganize || ViewModel.IsLibraryVisible) return;
        CommitEditors(); CloseSettingsPopups();
        PlaceWritingPopup(PenSettingsPopup, _focusMode ? FocusPenSettingsButton : PenSettingsButton);
        UpdateTool(); PenSettingsPopup.IsOpen = true;
        FocusSetting(focus);
    }

    private void OpenEraserSettings(FrameworkElement? focus = null)
    {
        if (!CanOrganize || ViewModel.IsLibraryVisible) return;
        SetTool(_eraserTool);
        PlaceWritingPopup(EraserSettingsPopup, _focusMode ? FocusEraserButton : EraserButton);
        EraserSettingsPopup.IsOpen = true;
        FocusSetting(focus);
    }

    private void FocusSetting(FrameworkElement? element)
    {
        if (element is null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (_closing || !element.IsVisible || !element.IsEnabled) return;
            element.BringIntoView();
            if (!element.Focus()) Descendants<Control>(element).FirstOrDefault(control => control.Focusable && control.IsEnabled)?.Focus();
        }));
    }

    private async void SaveNow()
    {
        if (!CanOrganize || ViewModel.IsLibraryVisible) return;
        CommitEditors(); await RunAsync(L.Text("Messages.Status.Saving"), () => ViewModel.Autosave.FlushAsync());
    }
}
