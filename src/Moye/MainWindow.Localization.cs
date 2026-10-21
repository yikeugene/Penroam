using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Moye.Models;
using L = Moye.Localization.LocalizationService;

namespace Moye;

public partial class MainWindow
{
    private bool _syncingLanguage;
    private Task _languageSaveTask = Task.CompletedTask;

    private void InitializeLocalization()
    {
        PropertyChangedEventManager.AddHandler(L.Current, InterfaceLanguageChanged, nameof(L.LanguageCode));
        SynchronizeLanguageSelection();
    }

    private void InterfaceLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        SynchronizeLanguageSelection();
        UpdateFeatureNavigation();
    }

    private void SynchronizeLanguageSelection()
    {
        if (HomeLanguageSelector is null) return;
        _syncingLanguage = true;
        try { HomeLanguageSelector.SelectedValue = L.Current.LanguageCode; }
        finally { _syncingLanguage = false; }
    }

    private void UpdateLocalizedToolLabels()
    {
        var pixel = _eraserTool == InkTool.PointEraser;
        L.Bind(EraserModeLabel, TextBlock.TextProperty, "Core.Eraser");
        foreach (var button in new[] { EraserButton, FocusEraserButton })
        {
            L.Bind(button, ToolTipProperty, pixel ? "Core.EraserPixelTooltip" : "Core.EraserStrokeTooltip");
            L.Bind(button, AutomationProperties.HelpTextProperty, pixel ? "Core.EraserPixelHelp" : "Core.EraserStrokeHelp");
        }
        var key = _tool switch
        {
            InkTool.Pen => "Core.ToolPen", InkTool.Highlighter => "Core.ToolHighlighter",
            InkTool.PointEraser => "Core.ToolPixel", InkTool.StrokeEraser => "Core.ToolStroke",
            InkTool.Lasso => "Core.ToolLasso", InkTool.Text => "Core.ToolText",
            InkTool.Select => "Core.ToolSelect", _ => "Core.ToolBrowse"
        };
        L.Bind(ToolStatus, TextBlock.TextProperty, key);
    }

    private void UpdateWorkspaceLabels()
    {
        var guarded = _workspace.TwoFingerNavigationOnly || _workspace.LockZoom;
        L.Bind(WorkspaceStateButton, ContentControl.ContentProperty, guarded ? "Core.WritingGuard" : "Core.Workspace");
        var navigation = L.M(_workspace.TwoFingerNavigationOnly ? "Core.TwoFinger" : "Core.OneFinger");
        var zoom = L.M(_workspace.LockZoom ? "Core.Locked" : "Core.Unlocked");
        L.Bind(WorkspaceStateButton, ToolTipProperty, "Core.WorkspaceHint", navigation, zoom);
        foreach (var button in new[] { EditorGuardButton, FocusGuardButton })
        {
            button.Visibility = guarded ? Visibility.Visible : Visibility.Collapsed;
            L.Bind(button, ToolTipProperty, "Core.WorkspaceHint", navigation, zoom);
        }
    }

    private async void HomeLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingLanguage || !_workspaceLoaded || _closing || ViewModel.IsBusy ||
            HomeLanguageSelector.SelectedValue is not string language) return;
        _workspace.UiLanguage = L.NormalizeLanguage(language);
        L.SetLanguage(_workspace.UiLanguage);
        // Home is intentionally excluded from the editor's reading-position debounce.
        _languageSaveTask = SaveWorkspaceAsync();
        await _languageSaveTask;
    }
}
