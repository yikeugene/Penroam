using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Moye.Controls;
using Moye.Models;
using Moye.Services;

namespace Moye;

public partial class MainWindow
{
    private WorkspacePreferences _workspace = new();
    private WorkspacePreferencesStore? _workspaceStore;
    private DraftRecoveryStore? _draftRecovery;
    private LocalSafetyService? _localSafety;
    private ScheduledBackupService? _scheduledBackups;
    private readonly DispatcherTimer _workspaceTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private readonly DispatcherTimer _backupTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private bool _workspaceLoaded, _restoringReadingPosition, _backupRunning;
    private DateTimeOffset _nextBackupAttempt;
    private CancellationTokenSource? _automaticBackupCancellation;
    private TaskCompletionSource? _automaticBackupCompletion;

    private void InitializeWorkspace()
    {
        var directory = Path.GetDirectoryName(_preferencesStore.FilePath)!;
        _workspaceStore = new(Path.Combine(directory, "workspace-preferences.json"));
        _draftRecovery = new(Path.Combine(directory, "recovery", "drafts"));
        _localSafety = new(Path.Combine(directory, "recovery", "deleted"), ViewModel.Backup);
        _scheduledBackups = new(directory, ViewModel.Backup);
        PreserveOrganizationDeletionAsync = PreserveDeletedAsync;
        _workspaceTimer.Tick += async (_, _) => { _workspaceTimer.Stop(); CaptureReadingPosition(); await SaveWorkspaceAsync(); };
        _backupTimer.Tick += async (_, _) => await TryScheduledBackupAsync();
        PageList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => QueueWorkspaceSave()));
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ViewModel.SelectedPage) or nameof(ViewModel.Zoom)) QueueWorkspaceSave();
        };
    }

    private async Task LoadWorkspaceAsync()
    {
        if (_workspaceStore is null) return;
        _workspace = await _workspaceStore.LoadAsync(); _workspaceLoaded = true;
        ViewModel.Autosave.RecoveryJournal = _draftRecovery;
        ApplyWorkspaceOptions();
        await RefreshRecoveryNoticeAsync();
        if (_workspaceStore.Warning is not null) ViewModel.Status = _workspaceStore.Warning;
        _backupTimer.Start();
        _ = Dispatcher.BeginInvoke(new Action(async () => await TryScheduledBackupAsync()), DispatcherPriority.ContextIdle);
    }

    private void QueueWorkspaceSave()
    {
        if (!_workspaceLoaded || _restoringReadingPosition || _closing || !ViewModel.IsEditorVisible) return;
        _workspaceTimer.Stop(); _workspaceTimer.Start();
    }

    private void CaptureReadingPosition()
    {
        if (!_workspaceLoaded || _restoringReadingPosition || ViewModel.IsLibraryVisible || ViewModel.Document is not { } document ||
            ViewModel.SelectedPage is not { } selected) return;
        var viewportPoint = new Point(Viewport.ActualWidth / 2, Math.Min(80, Viewport.ActualHeight / 4));
        var anchor = CapturePageZoomAnchor(viewportPoint);
        var page = anchor?.Page.Page ?? selected.Page;
        var point = anchor?.PagePoint ?? new Point();
        var view = anchor?.ViewportPoint ?? viewportPoint;
        _workspace.ReadingPositions[document.Id] = new(page.SectionId, page.Id,
            point.X / page.Width, point.Y / page.Height,
            Viewport.ActualWidth > 0 ? view.X / Viewport.ActualWidth : .5,
            Viewport.ActualHeight > 0 ? view.Y / Viewport.ActualHeight : 0,
            ViewModel.Zoom, _fitWidthActive, DateTimeOffset.UtcNow);
        _workspace.LastNotebookId = document.Id;
        // Bound device preferences independently of notebook content.
        foreach (var stale in _workspace.ReadingPositions.OrderByDescending(item => item.Value.ViewedUtc).Skip(500).Select(item => item.Key).ToArray())
            _workspace.ReadingPositions.Remove(stale);
        RefreshContinueButton();
    }

    private async Task SaveWorkspaceAsync()
    {
        if (!_workspaceLoaded || _workspaceStore is null) return;
        try { await _workspaceStore.SaveAsync(_workspace); }
        catch (Exception ex) { ViewModel.Status = "Workspace settings not saved · " + ex.Message; }
    }

    private async Task<bool> RestoreReadingPositionAsync()
    {
        if (ViewModel.Document is not { } document || !_workspace.ReadingPositions.TryGetValue(document.Id, out var position)) return false;
        var savedPage = document.Pages.FirstOrDefault(page => page.Id == position.PageId);
        if (savedPage is null) return false;
        _restoringReadingPosition = true;
        try
        {
            ViewModel.SelectedSection = ViewModel.Sections.FirstOrDefault(section => section.Id == savedPage.SectionId);
            ViewModel.SelectedPage = ViewModel.Pages.FirstOrDefault(page => page.Page.Id == savedPage.Id);
            if (ViewModel.SelectedPage is not { } page) return false;
            SelectLibraryCurrent(); ShowSidebarTab(false); ScrollToSelected();
            ViewModel.Zoom = double.IsFinite(position.Zoom) ? position.Zoom : .85;
            await Dispatcher.InvokeAsync(() => PageList.UpdateLayout(), DispatcherPriority.Loaded);
            if (position.FitWidth) FitWidth();
            await Dispatcher.InvokeAsync(() => PageList.UpdateLayout(), DispatcherPriority.Loaded);
            double Unit(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
            RestorePageZoomAnchor(new(page, new(Unit(position.X) * page.Page.Width, Unit(position.Y) * page.Page.Height),
                new(Unit(position.ViewX) * Viewport.ActualWidth, Unit(position.ViewY) * Viewport.ActualHeight)));
            return true;
        }
        finally { _restoringReadingPosition = false; }
    }

    private void RefreshContinueButton()
    {
        if (ContinueLastButton is null) return;
        ContinueLastButton.IsEnabled = !string.IsNullOrWhiteSpace(_workspace.LastNotebookId);
        ContinueLastButton.ToolTip = "Open the last notebook at your saved reading position";
    }

    private async void ContinueLastClick(object sender, RoutedEventArgs e)
    {
        var id = _workspace.LastNotebookId;
        if (string.IsNullOrWhiteSpace(id)) return;
        await OpenNotebookAsync(id);
    }

    private void ApplyWorkspaceOptions()
    {
        _touchNavigation.RequireTwoFingers = _workspace.TwoFingerNavigationOnly;
        _touchNavigation.ZoomLocked = _workspace.LockZoom;
        FavouriteToolbar.Visibility = _workspace.CompactToolbar ? Visibility.Collapsed : Visibility.Visible;
        FocusToolbar.HorizontalAlignment = _workspace.FocusToolsOnRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        WorkspaceStateButton.Content = _workspace.TwoFingerNavigationOnly || _workspace.LockZoom ? "Writing guard ●" : "Workspace";
        WorkspaceStateButton.ToolTip = $"{(_workspace.TwoFingerNavigationOnly ? "Two-finger navigation" : "One-finger navigation")} · {(_workspace.LockZoom ? "Zoom locked" : "Zoom unlocked")} · click to change";
        foreach (var button in new[] { EditorGuardButton, FocusGuardButton })
        {
            button.Visibility = _workspace.TwoFingerNavigationOnly || _workspace.LockZoom ? Visibility.Visible : Visibility.Collapsed;
            button.ToolTip = WorkspaceStateButton.ToolTip;
        }
        UpdateTextToolbar(); RefreshFocusPresets(); RefreshContinueButton(); ApplyFocusChrome();
    }

    private async void WorkspaceSettingsClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy) return;
        ClearTouches(true); CommitEditors();
        if (WorkspaceOptionsDialog.Show(this, _workspace))
        {
            ApplyWorkspaceOptions(); await SaveWorkspaceAsync(); await TryScheduledBackupAsync();
        }
    }

    private void RefreshFocusPresets()
    {
        if (FocusPresetPanel is null) return;
        FocusPresetPanel.Children.Clear();
        foreach (var preset in _preferences.Presets.Where(preset => preset.IsFavorite).Take(3))
        {
            var button = new Button
            {
                Content = new System.Windows.Shapes.Ellipse { Width = 20, Height = 20, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(preset.Color)), Stroke = Brushes.Gray, StrokeThickness = .5 },
                Style = (Style)FindResource("FocusToolButton"), ToolTip = preset.Name
            };
            System.Windows.Automation.AutomationProperties.SetName(button, preset.Name);
            button.Click += (_, _) => ApplyPreset(preset, true); FocusPresetPanel.Children.Add(button);
        }
        if (_focusMode) ApplyFocusChrome();
    }

    private void ShowCommandsClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy) return;
        ClearTouches(); CommitEditors();
        var commands = new List<WorkspaceCommand>
        {
            new("Quick note", "Start writing immediately in your Inbox; organize the page later.", () => QuickNoteClick(this, new())),
            new("Search notebook contents · Ctrl+F", "Find page names, bookmarks, topics and typed notes, then jump straight to the result.", () => SearchContentsClick(this, new())),
            new("Workspace settings", "Two-finger navigation, zoom lock, compact toolbar, focus docking and scheduled local backups.", () => WorkspaceSettingsClick(this, new())),
            new("Backup & recovery", "Recover deleted notebooks and pages, restore pending drafts, or choose which notebooks to restore from a backup.", () => SafetyCenterClick(this, new())),
            new("New notebook", "Choose a name, category and paper style.", () => NewNoteClick(this, new())),
            new("Continue last notebook", "Return to the page and zoom you last used.", () => ContinueLastClick(this, new()))
        };
        if (ViewModel.IsEditorVisible)
        {
            commands.AddRange([
                new("Manage pages", "Select multiple thumbnails; reorder, move, duplicate or delete them as one change.", () => ManagePagesClick(this, new())),
                new("Reference view", "Read another notebook beside your writing area, with independent navigation and zoom.", () => ShowReferenceClick(this, new())),
                new("Import document", "Preview a PDF or locally converted Office document and choose its pages and destination.", () => ImportDocumentWorkflowClick(this, new())),
                new("Export PDF", "Choose pages and check text overflow before sharing.", () => ExportDocumentWorkflowClick(this, new())),
                new("Continue text on next page", "Move overflowing text into a new page while retaining its formatting.", () => ContinueTextOnNextPageClick(this, new())),
                new("Go to page · Ctrl+G", "Enter the page number within this section.", () => GoToPageClick(this, new())),
                new("Focus mode · F11", "More writing space, with dockable tools and favorite pens.", () => ToggleFocus()),
                new("Pen · B", "Write with pressure-sensitive ink. Hold a line briefly to straighten it.", () => SetTool(InkTool.Pen)),
                new("Highlighter · H", "Mark important material using your selected highlighter.", () => SetTool(InkTool.Highlighter)),
                new("Eraser · E", "Choose pixel or stroke erasing, size, and highlighter-only mode.", () => SetTool(_eraserTool)),
                new("Lasso · L", "Circle ink, text and images to move or copy them together.", () => SetTool(InkTool.Lasso)),
                new("Type · T", "Start or resume typed notes. Ctrl+Enter returns to Pen.", () => TypeClick(this, new())),
                new("Undo · Ctrl+Z", "Undo a completed edit while this notebook stays open.", () => UndoClick(this, new())),
                new("Fit width", "Fill the writing area. Unlock zoom in Workspace settings if necessary.", () => FitWidth())
            ]);
        }
        WorkspaceCommandDialog.Show(this, commands);
    }

    private void GoToPageClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsEditorVisible || ViewModel.Pages.Count == 0 || ViewModel.IsBusy) return;
        var dialog = new InputDialog(this, "Go to page", ($"Page number (1–{ViewModel.Pages.Count})", ViewModel.SelectedPage?.Number.ToString() ?? "1"));
        if (dialog.ShowDialog() != true) return;
        if (!int.TryParse(dialog.Values[0], out var number) || number < 1 || number > ViewModel.Pages.Count)
        { MessageBox.Show(this, "Enter a page number in this section.", "Go to page"); return; }
        CommitEditors(); ClearTouches(); ViewModel.SelectedPage = ViewModel.Pages[number - 1]; ScrollToSelected();
    }
}
