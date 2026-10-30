using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using L = Moye.Localization.LocalizationService;

namespace Moye;

public partial class MainWindow
{
    private bool _paperOnlyMode, _floatingToolsExpanded;
    private bool _referenceBeforePaperOnly, _chromeWasHidden;
    private bool ChromeHidden => _focusMode || _paperOnlyMode;
    private bool FloatingToolsVisible => ChromeHidden && (!_paperOnlyMode || _floatingToolsExpanded);
    private Button FloatingToolAnchor(Button button) => _paperOnlyMode && !_floatingToolsExpanded ? FloatingToolsButton : button;

    private void PaperOnlyClick(object sender, RoutedEventArgs e) => TogglePaperOnly();
    private void FloatingToolsClick(object sender, RoutedEventArgs e) => ToggleFloatingTools();
    private void ExitWritingModesClick(object sender, RoutedEventArgs e) => ExitWritingModes();

    private void PrepareChromeChange()
    {
        ClearTouches(); CommitEditors(); CloseSettingsPopups();
    }

    private void TogglePaperOnly()
    {
        if (ViewModel.IsLibraryVisible || ViewModel.IsBusy || AnyPenDown) return;
        PrepareChromeChange();
        if (!_paperOnlyMode)
        {
            if (!ChromeHidden) _sidebarBeforeFocus = Sidebar.Visibility == Visibility.Visible;
            _referenceBeforePaperOnly = ReferenceHost.Visibility == Visibility.Visible;
            ReferenceHost.Visibility = Visibility.Collapsed;
            _paperOnlyMode = true;
        }
        else
        {
            _paperOnlyMode = false;
            RestorePaperReference();
        }
        _floatingToolsExpanded = false;
        ApplyFocusChrome();
        if (_paperOnlyMode) FloatingToolsButton.Focus();
        else if (_focusMode) ExitFocusButton.Focus();
        else PaperOnlyButton.Focus();
    }

    private void ToggleFloatingTools()
    {
        if (!_paperOnlyMode || ViewModel.IsBusy || AnyPenDown) return;
        PrepareChromeChange();
        _floatingToolsExpanded = !_floatingToolsExpanded;
        ApplyFocusChrome();
        if (_floatingToolsExpanded) FocusPenButton.Focus();
        else FloatingToolsButton.Focus();
    }

    private void RestorePaperReference()
    {
        if (_referenceBeforePaperOnly && ReferenceHost.Content is not null)
            ReferenceHost.Visibility = Visibility.Visible;
        _referenceBeforePaperOnly = false;
    }

    private void ExitWritingModes()
    {
        PrepareChromeChange();
        if (_paperOnlyMode) RestorePaperReference();
        _paperOnlyMode = false;
        _floatingToolsExpanded = false;
        if (_focusMode) ToggleFocus();
        else ApplyFocusChrome();
        if (ViewModel.IsEditorVisible) PaperOnlyButton.Focus();
    }

    private void ToggleFocus()
    {
        if (!_focusMode && (ViewModel.IsBusy || AnyPenDown)) return;
        PrepareChromeChange();
        if (!_focusMode)
        {
            if (ViewModel.IsLibraryVisible) return;
            if (!ChromeHidden) _sidebarBeforeFocus = Sidebar.Visibility == Visibility.Visible;
            _oldWindowState = WindowState;
            _focusMode = true;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
        }
        else
        {
            _focusMode = false;
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _oldWindowState;
        }
        FocusButton.Content = _focusMode ? "\uE73F" : "\uE740";
        L.Bind(FocusButton, ToolTipProperty, _focusMode ? "Core.ExitFocus" : "Core.Focus");
        ApplyFocusChrome();
    }

    private void ApplyFocusChrome()
    {
        NotebookHeader.Visibility = ChromeHidden ? Visibility.Collapsed : Visibility.Visible;
        WritingHeader.Visibility = ChromeHidden ? Visibility.Collapsed : Visibility.Visible;
        EditorFooter.Visibility = ChromeHidden && !ViewModel.HasSaveError ? Visibility.Collapsed : Visibility.Visible;
        if (ChromeHidden)
        {
            Sidebar.Visibility = Visibility.Collapsed;
            SidebarColumn.Width = new GridLength(0);
        }
        else if (_chromeWasHidden)
        {
            Sidebar.Visibility = _sidebarBeforeFocus ? Visibility.Visible : Visibility.Collapsed;
            SidebarColumn.Width = new GridLength(_sidebarBeforeFocus ? 224 : 0);
        }
        _chromeWasHidden = ChromeHidden;
        FocusToolbar.Visibility = FloatingToolsVisible ? Visibility.Visible : Visibility.Collapsed;
        FloatingToolsButton.Visibility = _paperOnlyMode ? Visibility.Visible : Visibility.Collapsed;
        FloatingToolsButton.HorizontalAlignment = _workspace.FocusToolsOnRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        var circleLabel = _floatingToolsExpanded ? "Shell.HideFloatingTools" : "Shell.ShowFloatingTools";
        L.Bind(FloatingToolsButton, ToolTipProperty, circleLabel);
        L.Bind(FloatingToolsButton, AutomationProperties.NameProperty, circleLabel);
        FloatingToolsButton.Content = _floatingToolsExpanded ? "\uE70E" : "\uE713";
        // Keep the shared palette outside the paper's input routes and bounds.
        // Reserve a narrow gutter for the collapsed circle as well.
        if (FloatingToolsVisible)
        {
            FocusToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var inset = Math.Ceiling(FocusToolbar.DesiredSize.Width) + 8;
            EditingSurface.Margin = _workspace.FocusToolsOnRight ? new Thickness(0, 0, inset, 0) : new Thickness(inset, 0, 0, 0);
        }
        else EditingSurface.Margin = !_paperOnlyMode ? new Thickness(0) :
            _workspace.FocusToolsOnRight ? new Thickness(0, 0, 80, 0) : new Thickness(80, 0, 0, 0);
        UpdateFeatureNavigation();
    }
}
