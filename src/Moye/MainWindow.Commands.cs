using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Moye.Controls;
using Moye.Models;

namespace Moye;

public partial class MainWindow
{
    internal static readonly string[] FeatureCategories =
        ["Notebook", "Pages", "Insert & export", "Search", "Study", "View", "Tools", "Backup", "Help"];

    private void InitializeFeatureNavigation()
    {
        foreach (var category in FeatureCategories)
        {
            var item = new MenuItem { Header = category, MinHeight = 44, MinWidth = 44 };
            AutomationProperties.SetName(item, category + " actions");
            item.Items.Add(new MenuItem());
            item.SubmenuOpened += (_, e) =>
            {
                if (!ReferenceEquals(e.OriginalSource, item)) return;
                ClearTouches(); CloseSettingsPopups();
                item.Items.Clear();
                foreach (var command in CreateFeatureCommands().Where(command => command.Category == category && command.ParentId is null))
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
                else if (!command.TryExecute()) ViewModel.Status = command.UnavailableReason ?? "This tool is not available here.";
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
        SetState(TwoPagesButton, _twoPageView, "Two pages");
        SetState(ReferenceViewButton, ReferenceHost.Visibility == Visibility.Visible, "Reference view");
        void SetState(Button button, bool active, string name)
        {
            button.Background = (Brush)FindResource(active ? "AccentSoft" : "Surface");
            button.BorderBrush = (Brush)FindResource(active ? "Accent" : "Stroke");
            button.Foreground = (Brush)FindResource(active ? "Accent" : "Ink");
            AutomationProperties.SetHelpText(button, name + (active ? " is on. Click to turn off." : " is off. Click to turn on."));
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

        string? Ready() => _closing ? "This window is closing." : !_ready ? "Wait for the library to finish loading." :
            ViewModel.IsBusy ? "Finish the current operation first." : AnyPenDown ? "Lift your pen before using this action." : null;
        string? Notebook() => Ready() ?? (document is null || ViewModel.IsLibraryVisible ? "Open a notebook first." :
            !ReferenceEquals(document, ViewModel.Document) ? "The notebook changed. Open its tools again." : null);
        string? Page() => Notebook() ?? (pageTarget is null ? "Add or select a page first." :
            ResolvePageActionTarget(ViewModel, pageTarget) is null ? "The page changed. Open its actions again." : null);
        string? Section() => Notebook() ?? (sectionTarget is null ? "Select a section first." :
            ResolveSectionActionTarget(ViewModel, sectionTarget) is null ? "The section is no longer available." : null);
        string? EditingPage() => Page() ?? (editor is null || !ReferenceEquals(editor, CurrentEditor) ? "Select the original page in the writing area." :
            editor.IsTextComposing ? "Finish composing text first." : null);
        string? Selection() => EditingPage() ?? (editor?.HasSelection == true ? null : "Select ink, text or images with Lasso or Select.");
        string? Text() => EditingPage() ?? (selectedText is null || !ReferenceEquals(editor?.SelectedText, selectedText) ? "Select a text item first." : null);
        string? Zoom() => Page() ?? (_workspace.LockZoom ? "Unlock zoom in Workspace settings first." : null);
        string? Library() => Ready() ?? (ViewModel.IsLibraryVisible ? null : "Return to My notebooks first.");

        void Add(string id, string name, string category, string detail, Action? execute, Func<string?>? available = null,
            string shortcut = "", string keywords = "", string? parent = null, string? location = null, Func<bool>? check = null)
        {
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

        Add("new-notebook", "New notebook…", "Notebook", "Choose a notebook name, category and one of seven paper templates.", () => NewNoteClick(this, new()));
        Add("open-notebooks", "My notebooks", "Notebook", "Save your work and open the library. Click a cover or choose a notebook from the sidebar.", () => ShowNotebooksClick(this, new()));
        Add("continue-notebook", "Continue last notebook", "Notebook", "Return to the saved page, reading position and zoom on this device.", () => ContinueLastClick(this, new()),
            () => Ready() ?? (string.IsNullOrWhiteSpace(_workspace.LastNotebookId) ? "Open a notebook to save a reading position first." : null));
        Add("quick-note", "Quick note", "Notebook", "Create a ruled page in Quick Notes, in the Inbox category, ready to write with Pen.", () => QuickNoteClick(this, new()));
        Add("rename-notebook", "Rename and category…", "Notebook", "Change the open notebook's title and its single category.", () => RenameClick(this, new()), Notebook);
        Add("pin-notebook", "Pin / unpin notebook", "Notebook", "Keep this notebook above other notebooks. On the home screen use its Notebook actions button.",
            () => { if (summary is not null) ToggleNotebookPin(summary); }, Notebook, check: () => document?.IsPinned == true);
        Add("cover-color", "Choose cover color…", "Notebook", "Choose a cover color and pinning in Notebook appearance. Also available from each card's Notebook actions.",
            () => { if (summary is not null) EditNotebookAppearance(summary); }, Notebook);
        Add("delete-notebook", "Delete notebook…", "Notebook", "Confirm deletion of the open notebook. A complete recovery copy is kept for up to 30 days.", () => DeleteNotebookClick(this, new()), Notebook);
        Add("sort-library", "Sort & filter library…", "Notebook", "Filter by category or sort by name; pinned notebooks remain first.", () => LibraryOrganizationClick(this, new()));
        Add("find-notebook", "Find a notebook", "Notebook", "Filter notebook titles and categories, not page contents. Ctrl+F focuses this filter on the home screen. Clear search also clears the category filter.",
            () => { if (!ViewModel.IsLibraryVisible) ShowSidebarTab(true); var field = ViewModel.IsLibraryVisible ? HomeSearch : LibrarySearch; field.Focus(); field.SelectAll(); });
        Add("clear-library-search", "Clear notebook search", "Notebook", "Clear notebook title and category filtering.", () => ClearSearchClick(this, new()),
            () => Ready() ?? (ViewModel.HasSearch ? null : "No notebook filter is active."), parent: "find-notebook");

        Add("add-page", "Add page…", "Pages", "Choose Blank, Ruled, Grid, Dot Grid, Cornell, Graph or Meeting paper. Insert an A4 page after the current page.", () => ShowPaperPicker(true), Notebook);
        Add("page-actions", "Page actions", "Pages", "Rename, bookmark, duplicate, reorder, move or delete the selected page. The same actions are available beside the thumbnails.",
            () => { if (pageTarget is not null) ShowCapturedPageActions(pageTarget); }, Page);
        PageAction("rename-page", "Page title & bookmark…", "Set an optional page title and bookmark without changing its contents.", RenamePage);
        PageAction("bookmark-page", "Bookmark / remove bookmark", "Mark this page for later review. Search → Bookmarks lists marked pages across the library.", TogglePageBookmark,
            check: () => pageTarget is not null && ResolvePageActionTarget(ViewModel, pageTarget)?.Page.IsBookmarked == true);
        PageAction("duplicate-page", "Duplicate page", "Copy this page and its editable contents immediately after the original.", target => RunPageAction(target, ViewModel.DuplicatePage));
        PageAction("move-page-up", "Move page up", "Move the page earlier within this section.", target => RunPageAction(target, () => ViewModel.MovePage(-1)),
            () => Page() ?? (pageTarget is not null && ViewModel.Pages.IndexOf(ResolvePageActionTarget(ViewModel, pageTarget)!) > 0 ? null : "This is the first page in its section."));
        PageAction("move-page-down", "Move page down", "Move the page later within this section.", target => RunPageAction(target, () => ViewModel.MovePage(1)),
            () => Page() ?? (pageTarget is not null && ViewModel.Pages.IndexOf(ResolvePageActionTarget(ViewModel, pageTarget)!) < ViewModel.Pages.Count - 1 ? null : "This is the last page in its section."));
        PageAction("move-page-section", "Move page to section…", "Choose another existing section; the editor follows the moved page.", ShowMovePageToSection,
            () => Page() ?? (ViewModel.Sections.Count > 1 ? null : "Add another section first."));
        PageAction("delete-page", "Delete page", "Delete this page. Undo is available while the notebook stays open; Recently deleted keeps a notebook copy.", DeletePage);
        PageAction("paper-style", "Paper style…", "Change this ordinary page's paper template while retaining notes. PDF pages retain their original background.", ShowPageSettings,
            () => Page() ?? (pageTarget is not null && ResolvePageActionTarget(ViewModel, pageTarget)?.Page.Pdf is not null ? "PDF pages keep their background. Add a paper page instead." : null), parent: null);
        PageAction("extend-page", "Extend page…", "Add blank space on any edge or corner, including around PDFs, without shrinking existing content. Drag the preview or enter margins in mm.", ExtendPage, parent: null);
        Add("add-section", "Add section…", "Pages", "Name a topic and create its first paper page.", () => AddSectionClick(this, new()), Notebook);
        Add("section-actions", "Section actions", "Pages", "Rename, reorder or delete the current section. At least one section must remain.",
            () => { if (sectionTarget is not null && ResolveSectionActionTarget(ViewModel, sectionTarget) is { } target)
                OpenSectionContextMenu(target, FeatureActionAnchor(SectionOptionsButton), FeatureMenuPlacement); }, Section);
        Add("rename-section", "Rename section…", "Pages", "Change this section's name.", () => { if (sectionTarget is not null) RenameSection(sectionTarget); }, Section, parent: "section-actions");
        Add("move-section-up", "Move section up", "Pages", "Move this topic earlier in the notebook.", () => { if (sectionTarget is not null) RunSectionAction(sectionTarget, () => ViewModel.MoveSection(-1)); },
            () => Section() ?? (ViewModel.Sections.IndexOf(ResolveSectionActionTarget(ViewModel, sectionTarget!)!) > 0 ? null : "This is the first section."), parent: "section-actions");
        Add("move-section-down", "Move section down", "Pages", "Move this topic later in the notebook.", () => { if (sectionTarget is not null) RunSectionAction(sectionTarget, () => ViewModel.MoveSection(1)); },
            () => Section() ?? (ViewModel.Sections.IndexOf(ResolveSectionActionTarget(ViewModel, sectionTarget!)!) < ViewModel.Sections.Count - 1 ? null : "This is the last section."), parent: "section-actions");
        Add("delete-section", "Delete section…", "Pages", "Confirm deletion of this section and its pages. Undo and a recovery copy remain available.", () => { if (sectionTarget is not null) DeleteSection(sectionTarget); },
            () => Section() ?? (ViewModel.Sections.Count > 1 ? null : "Keep at least one section in the notebook."), parent: "section-actions");
        Add("manage-pages", "Manage pages…", "Pages", "Select page checkboxes or use Ctrl/Shift-click. Drag, reorder, duplicate, delete or move pages; Apply changes commits the batch.", () => ManagePagesClick(this, new()), Notebook);
        Add("transfer-pages", "Move / copy to another notebook…", "Pages", "In Manage pages, select pages then Move / copy to notebook. Choose the notebook and section; Keep a copy selects copying instead of moving.",
            () => ManagePagesClick(this, new()), Notebook, parent: "manage-pages");
        Add("undo-transfer", "Undo last page transfer", "Pages", "Reverse the latest transfer in this session only if both notebooks remain unchanged since it completed.", () => UndoPageTransferClick(this, new()),
            () => Notebook() ?? (transfer is null ? "No page transfer to undo in this session." : !ReferenceEquals(transfer, _lastPageTransfer) ? "The transfer changed. Open its tools again." : null));

        Add("import-document", "Import document…", "Insert & export", "Preview PDF, DOCX, PPTX, PPSX, ODT or ODP pages; choose ranges and a destination section or notebook. Office formats need a local converter.", () => ImportDocumentWorkflowClick(this, new()), Notebook,
            keywords: "Word PowerPoint LibreOffice slides page ranges selected pages");
        Add("import-folder", "Import folder…", "Insert & export", "From My notebooks, import one folder's first-level PDF/PPTX files into a new notebook with one section per file. This is not folder synchronization.", () => ImportFolderClick(this, new()), Library);
        Add("insert-image", "Insert image…", "Insert & export", "Insert PNG or JPEG on the current page. Select exposes move and resize handles; original image bytes stay intact.", () => RunOnCapturedPage(() => ImageClick(this, new())), Page,
            keywords: "photo screenshot picture");
        Add("export-pdf", "Export PDF…", "Insert & export", "Choose current page, section, whole notebook or page ranges, then original-size pages or actual-size A4 sheets. Export does not replace an editable backup.", () => ExportDocumentWorkflowClick(this, new()), Notebook);
        Add("a4-export", "A4 sheets at actual size…", "Insert & export", "Choose export pages, then A4 sheets · actual size. Set automatic/portrait/landscape orientation and 0–30 mm overlap; preview the sheet grid before saving.",
            () => ExportDocumentWorkflowClick(this, new()), Notebook, parent: "export-pdf", keywords: "print printing tile tiling overlap orientation");
        Info("export-overflow", "Check overflowing text before export", "Insert & export", "Export checks selected pages for text overflow. Go to selected text returns to editing; Export visible text deliberately exports only the visible portion. Print the saved PDF in a PDF viewer.", "export-pdf");
        Info("export-completion", "Open the exported file", "Insert & export", "After successful PDF export, choose Open PDF, Show in folder or Done. Original PDF text remains intact; added text exports as vector outlines, not searchable text.", "export-pdf");

        Add("search-content", "Search contents…", "Search", "Search notebook/category/section/page names, typed text and extractable original PDF text across the library. Ctrl+F also works in the editor outside text inputs. No handwriting recognition or scan OCR.", () => SearchContentsClick(this, new()), shortcut: "Ctrl+Shift+F", keywords: "find content text PDF");
        Add("search-appearance", "Find by appearance…", "Search", "Find ink or typed text in this notebook by color family, nine page positions, section and mark type. Does not inspect pixels inside images or PDFs.", () => VisualSearchClick(this, new()), Notebook);
        Add("bookmarks", "Bookmarks…", "Search", "Open the library-wide bookmarked-page list. The Bookmarked pages only filter can also narrow a content search.", () => ShowBookmarksClick(this, new()));
        Add("go-to-page", "Go to page…", "Search", "Enter a page number within the current section, or click the page counter.", () => GoToPageClick(this, new()), Page, "Ctrl+G");
        Add("back-to-page", "Back to previous page", "Search", "Return to a remembered search, question, practice or quick-note visit. This is session history, not every scroll or thumbnail click.", () => NavigateBackClick(this, new()),
            () => Ready() ?? (_organizationBack.Count > 0 ? null : "No earlier search or quick-note location."));

        PageAction("mark-question", "Mark a question…", "Use the selected content or choose a source region. Write the question, explanation and next attempt; manually set Need help, Understood, Try again or Resolved.", AddStudyQuestion, parent: null, category: "Study");
        Add("question-inbox", "Question inbox…", "Study", "Search, edit or remove questions across this notebook. Include resolved questions shows completed items; Open source returns to and briefly outlines the source region.", () => StudyInboxClick(this, new()), Notebook);
        PageAction("resume-hint", "Leave a resume hint…", "Save one next-step note on this page or selected region. Edit or clear an existing hint; saved hints appear in the Continue strip.", SetResumeHint, parent: null, category: "Study");
        Add("resume-thinking", "Resume my thinking…", "Study", "Search, edit or remove this notebook's saved next steps and open their source pages. These notes are not included in ordinary content search.", () => ResumeStudyClick(this, new()), Notebook);
        PageAction("new-practice", "New practice attempt…", "Capture any page region as a saved question image and create a ruled solution page. No marked Question is required. From a practice page, reuse the saved snapshot for another attempt.", NewPracticeAttempt, parent: null, category: "Study");
        Add("practice-attempts", "Practice attempts…", "Study", "Review earlier solutions, record mistakes and a next approach, mark Reviewed, open the Original page or create Another attempt. Save & open saves reflections; Close cancels them.", () => PracticeAttemptsClick(this, new()),
            () => Notebook() ?? (document?.Pages.Any(page => page.Practice is not null) == true ? null : "Create a practice attempt first."));
        PageAction("restore-region", "Restore part of this page…", "Choose an earlier compatible page version and a region. Restore complete ink strokes, text and images inside it; other work stays. History lasts only while this notebook stays open.", RestoreRegion,
            () => Page() ?? (ViewModel.CanUndo ? null : "No earlier notebook edits in this session. Compatible page versions are checked when opening."), parent: null, category: "Study");

        Add("single-page", "Single page", "View", "Show one editable page per row without changing page contents or export order.", () => ChangePageLayout(false), Notebook, check: () => !_twoPageView);
        Add("two-pages", "Two pages", "View", "Edit adjacent pairs 1–2, 3–4 within this section. Both pages remain editable; an odd last page stays on its own.", () => ChangePageLayout(true), Notebook, check: () => _twoPageView);
        Add("fit-page", _twoPageView ? "Fit spread" : "Fit page", "View", "Fit the complete page or two-page spread in the writing area.", FitPage, Zoom);
        Add("fit-width", "Fit width", "View", "Fit page/spread width and keep fitting when the window or sidebar changes.", FitWidth, Zoom, check: () => _fitWidthActive);
        Add("actual-size", "Actual size · 100%", "View", "Return to 100% scale. This changes the view, not stored page coordinates.", () => ActualSizeClick(this, new()), Zoom);
        Add("zoom-in", "Zoom in", "View", "Increase scale up to 400%. Ctrl+mouse wheel and pinch zoom are also available.", () => ZoomInClick(this, new()), Zoom);
        Add("zoom-out", "Zoom out", "View", "Reduce scale down to 25% without shrinking saved notes.", () => ZoomOutClick(this, new()), Zoom);
        Add("sidebar", "Show / hide sidebar", "View", "Show Contents and Notebooks beside the writing area.", ToggleSidebar, Notebook, "F9", check: () => Sidebar.Visibility == Visibility.Visible);
        Add("focus-mode", "Focus mode", "View", "Use full-screen writing with compact tools and All tools. F11 or Exit Focus returns to the normal workspace.", ToggleFocus, Notebook, "F11", check: () => _focusMode);
        Add("reference-view", "Reference view", "View", "Open a read-only snapshot of another notebook/page beside writing. Choose pages, zoom or Fit independently; Refresh reloads it, Back to writing returns focus, and Close hides the pane.", () => ShowReferenceClick(this, new()), Notebook,
            check: () => ReferenceHost.Visibility == Visibility.Visible);
        Add("workspace-settings", "Workspace settings…", "View", "Set two-finger navigation, zoom lock, compact favorite toolbar and focus-tool docking. Changes stay on this device.", () => ShowWorkspaceSettings());
        Setting("two-finger-navigation", "Require two fingers to move pages", "Ignore one-finger page drags. Fingers navigate rather than draw ink.", "TwoFingerNavigation");
        Setting("zoom-lock", "Lock zoom while writing", "Lock zoom, including Fit Page, Fit Width and Actual Size, until you turn this setting off.", "LockZoom");
        Setting("compact-toolbar", "Show / hide favorite pen row", "Hide only the normal favorite-pen row. Favorites remain available by number and in Manage pens.", "CompactToolbar");
        Setting("focus-docking", "Dock focus tools left / right", "Choose the side used by the floating focus toolbar; writing and reference content keep clear of it.", "FocusToolsDock");

        Add("pen", "Pen", "Tools", "Write with pressure-sensitive ink using an active pen, or draw with the mouse.", () => SetTool(InkTool.Pen), Notebook, "B", check: () => _tool == InkTool.Pen);
        Add("highlighter", "Highlighter", "Tools", "Highlight with native 50% transparency. Pen and Highlighter retain their working colors separately.", () => SetTool(InkTool.Highlighter), Notebook, "H", check: () => _tool == InkTool.Highlighter);
        Add("eraser", "Eraser", "Tools", "Recall the last pixel/stroke eraser mode. Use Eraser settings to choose mode, size and highlighter-only erasing.", () => SetTool(_eraserTool), Notebook, "E", check: () => _tool is InkTool.PointEraser or InkTool.StrokeEraser);
        Add("lasso", "Lasso", "Tools", "Circle ink, text and images together. Drag to move, use edge handles to resize, or press Esc during a move to cancel.", () => SetTool(InkTool.Lasso), Notebook, "L", check: () => _tool == InkTool.Lasso);
        Add("type", "Type", "Tools", "Begin or resume free typing. Click empty paper for another text item; ruled paper aligns its lines. Ctrl+Enter finishes typing.", () => RunOnCapturedPage(() => TypeClick(this, new())), Page, "T", check: () => _tool == InkTool.Text);
        Add("select", "Select", "Tools", "Select a text item or image, then drag its move/resize handles. Free typing hides these handles until Select is active.", () => SetTool(InkTool.Select), Notebook, "V", check: () => _tool == InkTool.Select);
        Add("pen-settings", "Pen settings…", "Tools", "Choose ink color and thickness, save a preset or enable Draw and Hold. A selected ink stroke can be recolored or resized here.", () => OpenPenSettings(), Notebook);
        Add("ink-color", "Ink color…", "Tools", "Choose a swatch or More Colors. The color picker includes hue and saturation/brightness, Current/New previews, Apply and Cancel.", () => OpenPenSettings(ColorPalette), Notebook, parent: "pen-settings");
        Add("more-colors", "More ink colors…", "Tools", "Open the full color picker for the current pen or selected ink/text. No hex-input or eyedropper is provided.", () => CustomColorClick(this, new()), Notebook, parent: "pen-settings");
        Add("ink-width", "Ink thickness…", "Tools", "Choose one of 21 thickness levels with a live sample. Selected ink updates as one undoable change after the slider drag.", () => OpenPenSettings(WidthPicker), Notebook, parent: "pen-settings");
        Add("draw-hold", "Draw and Hold", "Tools", "Hold a roughly straight line's endpoint for about 0.65 seconds to straighten it, then keep dragging to adjust. Works with Pen and Highlighter; not a general shape recognizer.", () => OpenPenSettings(HoldToStraightenToggle), Notebook,
            parent: "pen-settings", check: () => HoldToStraightenToggle.IsChecked == true);
        Add("save-pen-preset", "Save current as preset…", "Tools", "Name a new Pen/Highlighter preset using the current working settings. At most 40 presets can be saved.", () => SavePresetClick(this, new()),
            () => Notebook() ?? (_preferences.Presets.Count < 40 ? null : "Remove a preset in Manage pens first."), parent: "pen-settings");
        Add("manage-pens", "Manage pens…", "Tools", "Rename, duplicate, delete or reorder presets; choose tool, color and width; set opacity, pressure, smoothing and favorites. Save and Use applies the selected preset, including hidden ones.", () => ShowPresetManager(), Notebook);
        Add("pen-opacity", "Pen opacity…", "Tools", "Set 10–100% opacity for Pen presets. Highlighter uses fixed native 50% transparency.", () => ShowPresetManager("Opacity"), Notebook, parent: "manage-pens");
        Add("pressure", "Pressure sensitivity…", "Tools", "Enable or disable pressure for a saved pen preset. Actual pressure requires compatible pen hardware.", () => ShowPresetManager("Pressure"), Notebook, parent: "manage-pens");
        Add("smoothing", "Stroke smoothing…", "Tools", "Enable or disable smoothing for a saved pen preset.", () => ShowPresetManager("Smoothing"), Notebook, parent: "manage-pens");
        Add("favorite-pens", "Favorite pen toolbar…", "Tools", "Choose Show in favourite toolbar, reorder with arrows, or drag visible favorites. The first nine favorites get number shortcuts; Focus shows the first three.", () => ShowPresetManager("Favorite"), Notebook, parent: "manage-pens");
        var favorites = _preferences.Presets.Where(preset => preset.IsFavorite).Take(9).ToArray();
        for (var index = 0; index < 9; index++)
        {
            var preset = favorites.ElementAtOrDefault(index);
            Add($"favorite-{index + 1}", preset is null ? $"Favorite pen {index + 1}" : $"Favorite {index + 1}: {preset.Name}", "Tools",
                "Use this saved favorite. The same shortcut works even when the favorite row is hidden.",
                () => { if (preset is not null) ApplyPreset(preset); },
                () => Notebook() ?? (preset is null || !_preferences.Presets.Any(current => current.Id == preset.Id && current.IsFavorite) ? "Choose this favorite in Manage pens first." : null),
                (index + 1).ToString(), parent: "manage-pens");
        }
        Add("eraser-settings", "Eraser settings…", "Tools", "Choose Pixel or Stroke Eraser, change its independent size, or protect pen strokes with Erase highlighter only.", () => OpenEraserSettings(), Notebook);
        Add("pixel-eraser", "Pixel eraser", "Tools", "Erase only the touched portion of ink; remaining fragments stay editable. It does not erase text, images or backgrounds.", () => SetTool(InkTool.PointEraser), Notebook,
            parent: "eraser-settings", check: () => _eraserTool == InkTool.PointEraser);
        Add("stroke-eraser", "Stroke eraser", "Tools", "Touch part of an ink stroke to remove the entire stroke.", () => SetTool(InkTool.StrokeEraser), Notebook,
            parent: "eraser-settings", check: () => _eraserTool == InkTool.StrokeEraser);
        Add("eraser-size", "Eraser size…", "Tools", "Adjust the eraser footprint from 12 to 120 page DIP, independently of pen width.", () => OpenEraserSettings(EraserSizeSlider), Notebook, parent: "eraser-settings");
        Add("erase-highlighter-only", "Erase highlighter only", "Tools", "Protect ordinary pen strokes while erasing highlights; available with both eraser modes.", () => OpenEraserSettings(HighlightOnlyToggle), Notebook,
            parent: "eraser-settings", check: () => HighlightOnlyToggle.IsChecked == true);

        Add("text-formatting", "Text formatting…", "Tools", "Show fonts, size, bold, italic, color, alignment, lists and text continuation. Formatting applies to the whole text item, not selected words. Browsing these tools does not create text.", () => ShowTextFormatting(), Notebook);
        Add("add-text-box", "Add fixed text box", "Tools", "Create a fixed-layout text box rather than free typing. Use separate boxes for different formatting.", () => RunOnCapturedPage(() => AddTextBoxClick(this, new())), Page, parent: "text-formatting");
        Format("text-font", "Text font…", "Choose Segoe UI, Arial, Calibri, Microsoft JhengHei, Consolas or Times New Roman for the whole text item.", TextFontPicker);
        Format("text-size", "Text size…", "Choose or enter a size from 6 to 96 pt for the whole text item.", TextSizePicker);
        Add("text-bold", "Bold text", "Tools", "Toggle bold for the entire selected text item. Use Ctrl+B while typing.", () => TextBoldClick(this, new()), Text, "Ctrl+B", parent: "text-formatting");
        Add("text-italic", "Italic text", "Tools", "Toggle italic for the entire selected text item. Use Ctrl+I while typing.", () => TextItalicClick(this, new()), Text, "Ctrl+I", parent: "text-formatting");
        Add("text-color", "Text color…", "Tools", "Choose the selected text item's color. This is a color control, not underline formatting.", () => TextColorClick(this, new()), Text, parent: "text-formatting");
        Format("text-alignment", "Text alignment…", "Align the selected text item Left, Center or Right.", TextAlignmentPicker);
        Add("bullet-list", "Bullet list", "Tools", "Toggle plain-text bullet prefixes on current or selected lines. Enter continues a list; an empty marked line ends it.", () => TextBulletsClick(this, new()), Text, parent: "text-formatting");
        Add("numbered-list", "Numbered list", "Tools", "Toggle plain-text numbering on current or selected lines. Markers remain editable characters.", () => TextNumberingClick(this, new()), Text, parent: "text-formatting");
        Add("continue-text", "Continue text on next page", "Tools", "Keep visible text on this page and move overflow, with formatting, to a new page. This is explicit pagination, not linked automatic text flow.", () => ContinueTextOnNextPageClick(this, new()),
            () => Text() ?? (editor?.HasTextOverflow == true ? null : "The selected text already fits on its page."), parent: "text-formatting");
        Add("finish-typing", "Finish typing", "Tools", "Commit text editing and return to Pen. Native text undo and IME keep their own keys while typing.", FinishTyping, Text, "Ctrl+Enter", parent: "text-formatting");
        Add("selection-actions", "Selection & clipboard", "Tools", "Copy, cut, paste, select all, duplicate, delete or nudge editable page content. Native text controls keep their text clipboard behavior.", () => OpenFeatureGroup("selection-actions", commands), Page);
        Add("select-all", "Select all page content", "Tools", "Select all ink, text and images on this page with Lasso; PDF backgrounds are not selectable objects.", () => RunOnCapturedPage(() => { SetTool(InkTool.Lasso); CurrentEditor?.SelectAllContent(); }), Page, "Ctrl+A", parent: "selection-actions");
        Add("copy-selection", "Copy selection", "Tools", "Copy editable mixed ink, text and images to another page or notebook, preserving image assets and ink attributes.", () => CopyInk(false), Selection, "Ctrl+C", parent: "selection-actions");
        Add("cut-selection", "Cut selection", "Tools", "Copy the editable selection and remove it from this page after copying succeeds.", () => CopyInk(true), Selection, "Ctrl+X", parent: "selection-actions");
        Add("paste", "Paste", "Tools", "Paste a Penroam selection, legacy ink, plain text or screenshot/image outside native text editing.", () => PasteContentClick(this, new()), EditingPage, "Ctrl+V", parent: "selection-actions");
        Add("duplicate-selection", "Duplicate selection", "Tools", "Create another editable copy of the selected ink, text or image.", () => editor?.DuplicateSelection(), Selection, "Ctrl+D", parent: "selection-actions");
        Add("delete-selection", "Delete selection", "Tools", "Remove the selected ink, text or images. Notebook Undo restores the completed change.", () => editor?.DeleteSelection(), Selection, "Delete", parent: "selection-actions");
        foreach (var (id, name, x, y, shortcut) in new[] { ("left", "left", -1, 0, "Left"), ("right", "right", 1, 0, "Right"), ("up", "up", 0, -1, "Up"), ("down", "down", 0, 1, "Down") })
            Add("nudge-" + id, "Move selection " + name, "Tools", "Move by one page DIP. Shift+arrow keys move by ten; drag selection handles to resize.",
                () => editor?.NudgeSelection(x, y), Selection, shortcut, parent: "selection-actions");
        Add("undo", "Undo", "Tools", "Undo a completed notebook edit while this notebook remains open. While typing, Ctrl+Z belongs to the native text editor.", () => UndoClick(this, new()),
            () => Notebook() ?? (ViewModel.CanUndo ? null : "No notebook edit to undo."), "Ctrl+Z");
        Add("redo", "Redo", "Tools", "Redo an undone notebook edit. A new edit clears redo history.", () => RedoClick(this, new()),
            () => Notebook() ?? (ViewModel.CanRedo ? null : "No notebook edit to redo."), "Ctrl+Y / Ctrl+Shift+Z");

        Add("save-now", "Save now", "Backup", "Commit pending editor input and save this notebook locally. Completed edits also save automatically.", SaveNow, Notebook, "Ctrl+S");
        Add("retry-save", "Retry save", "Backup", "Retry failed notebook and writing-preference saves. Pending content stays in memory on failure.", () => RetrySaveClick(this, new()));
        Add("backup-notebook", "Back up this notebook…", "Backup", "Save every section and its editable content, including pending edits, as a .moye backup.", () => BackupNoteClick(this, new()), Notebook);
        Add("backup-all", "Back up all notebooks…", "Backup", "Create an editable .moye backup of the library. Backups are not encrypted and do not include device-local writing/workspace preferences.", () => BackupAllClick(this, new()));
        Add("restore-backup", "Restore .moye backup…", "Backup", "Validate a backup, select notebooks and restore them as new copies without overwriting existing notebooks.", () => RestoreSelectedClick(this, new()));
        Add("backup-recovery", "Backup & recovery…", "Backup", "Check automatic backup status, restore backups, recover interrupted drafts or restore Recently deleted notebook copies.", () => SafetyCenterClick(this, new()));
        Add("recover-drafts", "Recover interrupted drafts…", "Backup", "In Interrupted drafts, recover a selected snapshot as a new copy or discard it. Drafts depend on local library assets and are best effort, not portable backups.", () => SafetyCenterClick(this, new()), parent: "backup-recovery");
        Add("recently-deleted", "Recently deleted…", "Backup", "Restore a complete notebook copy kept before page/section/notebook deletion, for up to 30 days; move wanted pages back yourself. Delete permanently requires confirmation.", () => SafetyCenterClick(this, new()), parent: "backup-recovery");
        Add("automatic-backups", "Automatic local backups…", "Backup", "Enable backups while Penroam is open, choose a folder, set a 1–168 hour interval and keep 1–100 completed backups. This is not a Windows background task.", () => ShowWorkspaceSettings("BackupEnabled"));
        Setting("backup-folder", "Automatic backup folder…", "Choose where automatic backups are stored; another storage device gives better protection against disk failure.", "BackupFolder", "Backup", "automatic-backups");
        Setting("backup-interval", "Automatic backup interval…", "Choose an interval from 1 to 168 hours. Backups defer during busy work or pen input.", "BackupInterval", "Backup", "automatic-backups");
        Setting("backup-retention", "Automatic backup retention…", "Keep 1–100 completed automatic backups. Manual backup files are not deleted by this retention policy.", "BackupRetention", "Backup", "automatic-backups");

        Add("check-updates", "Check for updates…", "Help", "Check GitHub stable releases. Update now downloads and verifies the installer, then saves before installing; Later and Cancel download keep you working.", () => CheckForUpdatesClick(this, new()),
            () => Ready() ?? (_updateService is null ? "Updates are disabled for this isolated library." : null));
        Add("writing-settings-warning", "Writing settings status…", "Help", "Inspect a protected/unreadable preference-file warning or retry a pending settings save. A newer settings file is not overwritten.", () => RetryPreferencesClick(this, new()));
        Info("keyboard-help", "Keyboard shortcuts", "Help", "B/H/E/L/T/V choose tools; 1–9 choose favorites. Ctrl+Shift+F searches content; Ctrl+G goes to a page; F9 toggles the sidebar and F11 Focus. Outside text/selection editing, arrows and PageUp/PageDown navigate pages and Home/End reach section ends. Shift+F10 opens a focused page/section menu. Ctrl+Shift+P opens All tools.", keywords: "hotkeys shortcuts navigation");
        Info("pan-touch-help", "Pan, zoom and pen input", "Help", "Hold Space and drag with the left mouse button to pan temporarily. One finger scrolls with inertia; two fingers pan and pinch zoom. Fingers navigate, not draw. Pen contact pauses navigation; a compatible pen tail eraser follows the selected eraser mode. Hardware pressure and palm rejection depend on the device and drivers.", keywords: "Space mouse touch palm tail eraser pressure");
        Info("typing-help", "Free typing, lists and IME", "Help", "Type creates/resumes editable text; click empty paper for more. Free text aligns to horizontal guides on Ruled/Cornell/Meeting, but added blank margins are free placement. Use fixed Text box for unsnapped layouts. Windows IME works natively. Formatting applies to the whole text item; selected words cannot have independent styles.", keywords: "Chinese input text boxes baseline");
        Info("autosave-help", "Automatic saving and reading position", "Help", "Completed edits queue local saves; the footer reports success or failure. Opening a notebook restores its saved page, zoom and reading position on this device. Preferences are separate from notebook backups. Failed saves keep pending content; interrupted-draft recovery cannot guarantee the final stroke or active composition.", keywords: "offline autosave scroll position preferences");
        Info("document-help", "Document import and export limits", "Help", "PDF import needs no Office app. DOCX requires Word or LibreOffice; PPTX/PPSX require PowerPoint or LibreOffice; ODT/ODP require LibreOffice. Converted pages are static PDF backgrounds, not editable Office objects. Passwords, macros, linked external resources and unsupported PDF forms/signatures are rejected. Sources stay unchanged; folder import only accepts first-level PDF/PPTX.", keywords: "Word PowerPoint PDF LibreOffice ODT ODP DOCX PPSX conversion");
        Info("isolated-library-help", "Use a separate library", "Help", "The advanced --data-dir launch argument selects an independent library and preference directory, and disables update checks. There is no in-app library-file switcher. Never move an active SQLite database without its journal files.", keywords: "data directory database portable");
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
        CommitEditors(); await RunAsync("Saving…", () => ViewModel.Autosave.FlushAsync());
    }
}
