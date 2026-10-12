using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Moye.Controls;

/// <summary>A non-modal update card. Showing it never moves focus away from the notebook.</summary>
public sealed class UpdateNotification : Border
{
    private readonly TextBlock _heading = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _message = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _notes = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly ScrollViewer _notesScroll;
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 6, Margin = new Thickness(0, 16, 0, 0) };
    private readonly TextBlock _progressLabel = new() { FontSize = 12, Margin = new Thickness(0, 7, 0, 0) };
    private readonly Button _primary = new() { MinWidth = 118, MinHeight = 44, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _secondary = new() { MinWidth = 80, MinHeight = 44, Margin = new Thickness(0) };
    private bool _canCancel;
    private string? _version;

    public event EventHandler? UpdateRequested;
    public event EventHandler? DismissRequested;
    public event EventHandler? CancelRequested;

    public UpdateNotification()
    {
        Name = "UpdateNotification";
        Width = 380;
        MaxWidth = 380;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(16);
        Padding = new Thickness(20);
        CornerRadius = new CornerRadius(14);
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(BorderBrushProperty, "Stroke");
        TextBlock.SetForeground(this, ResourceBrush("Ink", Color.FromRgb(36, 51, 47)));
        TextBlock.SetFontFamily(this, new FontFamily("Segoe UI, Microsoft JhengHei UI"));
        _message.Foreground = _notes.Foreground = _progressLabel.Foreground = ResourceBrush("MutedInk", Color.FromRgb(101, 115, 109));
        _progress.Foreground = ResourceBrush("Accent", Color.FromRgb(35, 100, 81));
        _primary.SetResourceReference(StyleProperty, "PrimaryButton");
        _secondary.SetResourceReference(StyleProperty, "SecondaryButton");
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Continue);
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(this, "Software updates");
        AutomationProperties.SetName(_progress, "Update download progress");
        AutomationProperties.SetName(_notes, "Release notes");
        _notesScroll = new ScrollViewer
        {
            Content = _notes, MaxHeight = 105, Margin = new Thickness(0, 12, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly
        };
        AutomationProperties.SetName(_notesScroll, "Release notes");
        var panel = new StackPanel();
        panel.Children.Add(_heading);
        panel.Children.Add(_message);
        panel.Children.Add(_notesScroll);
        panel.Children.Add(_progress);
        panel.Children.Add(_progressLabel);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        actions.Children.Add(_secondary);
        actions.Children.Add(_primary);
        panel.Children.Add(actions);
        Child = panel;
        _primary.Click += (_, _) => UpdateRequested?.Invoke(this, EventArgs.Empty);
        _secondary.Click += (_, _) =>
        {
            if (_canCancel) CancelRequested?.Invoke(this, EventArgs.Empty);
            else DismissRequested?.Invoke(this, EventArgs.Empty);
        };
        Visibility = Visibility.Collapsed;
    }

    public void ShowAvailable(string version, string notes)
    {
        _version = ShortText(version, 64);
        SetState($"Penroam {_version} is available", "Update to the latest version. Your notes will be saved before Penroam closes for installation.", "Update now", "Later");
        _notes.Text = ShortText(notes, 2400);
        _notesScroll.Visibility = _notes.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <param name="progress">Download completion from 0 to 1; NaN means the total size is unknown.</param>
    public void ShowDownloading(double progress)
    {
        SetState("Downloading update…", "You can keep working while the update downloads. Penroam will save your notes before installation.", null, "Cancel download", announce: false);
        _canCancel = true;
        _progress.Visibility = _progressLabel.Visibility = Visibility.Visible;
        _progress.IsIndeterminate = !double.IsFinite(progress);
        _progress.Value = double.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 0;
        _progressLabel.Text = _progress.IsIndeterminate ? "Downloading…" : $"{_progress.Value:P0} downloaded";
        AutomationProperties.SetName(this, "Downloading Penroam update");
    }

    public void ShowReady(string version)
    {
        _version = ShortText(version, 64);
        SetState($"Penroam {_version} is ready to install", "Finish your current work, then install. Your notes will be saved and Penroam will reopen.", "Install update", "Later");
    }
    public void ShowPreparing()
    {
        SetState("Preparing to update…", "Saving your notes and starting the installer. Penroam will reopen after installation.", null, null);
        _progress.Visibility = Visibility.Visible;
        _progress.IsIndeterminate = true;
    }

    public void ShowError(string message)
    {
        SetState("Update could not finish", ShortText(message, 600), "Try again", "Later");
    }

    public void ShowCurrent(string version)
    {
        _version = ShortText(version, 64);
        SetState("You’re up to date", $"You have Penroam {_version}. No newer release is available.", null, "Done");
    }

    public void ShowChecking()
    {
        SetState("Checking for updates…", "Looking for a new Penroam release on GitHub.", null, "Dismiss");
        _progress.Visibility = Visibility.Visible;
        _progress.IsIndeterminate = true;
    }

    private void SetState(string heading, string message, string? primary, string? secondary, bool announce = true)
    {
        _heading.Text = heading;
        _message.Text = message;
        _notesScroll.Visibility = _progress.Visibility = _progressLabel.Visibility = Visibility.Collapsed;
        _canCancel = false;
        SetButton(_primary, primary);
        SetButton(_secondary, secondary);
        Visibility = Visibility.Visible;
        AutomationProperties.SetName(this, heading + ". " + message);
        if (announce && UIElementAutomationPeer.FromElement(this) is { } peer)
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private static void SetButton(Button button, string? content)
    {
        button.Content = content;
        button.Visibility = content is null ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(button, content ?? "");
    }

    private static string ShortText(string? value, int maxLength)
    {
        var text = (value ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        return text.Length <= maxLength ? text : text[..maxLength].TrimEnd() + "…";
    }

    private Brush ResourceBrush(string key, Color fallback) => TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
    protected override AutomationPeer OnCreateAutomationPeer() => new UpdateNotificationAutomationPeer(this);

    private sealed class UpdateNotificationAutomationPeer(UpdateNotification owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(UpdateNotification);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    }
}
