using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Moye.Localization;
using L = Moye.Localization.LocalizationService;

namespace Moye.Controls;

/// <summary>A non-modal update card. Showing it never moves focus away from the notebook.</summary>
public sealed class UpdateNotification : Border
{
    private readonly TextBlock _heading = new() { Name = "UpdateHeading", FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _message = new() { Name = "UpdateMessage", FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _notes = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly ScrollViewer _notesScroll;
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 6, Margin = new Thickness(0, 16, 0, 0) };
    private readonly TextBlock _progressLabel = new() { FontSize = 12, Margin = new Thickness(0, 7, 0, 0) };
    private readonly Button _primary = new() { Name = "UpdatePrimaryButton", MinWidth = 118, MinHeight = 44, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _secondary = new() { Name = "UpdateSecondaryButton", MinWidth = 80, MinHeight = 44, Margin = new Thickness(0) };
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
        L.Bind(this, AutomationProperties.NameProperty, "Controls.Update.AccessibleName");
        L.Bind(_progress, AutomationProperties.NameProperty, "Controls.Update.ProgressName");
        L.Bind(_notes, AutomationProperties.NameProperty, "Controls.Update.ReleaseNotes");
        _notesScroll = new ScrollViewer
        {
            Content = _notes, MaxHeight = 105, Margin = new Thickness(0, 12, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly
        };
        L.Bind(_notesScroll, AutomationProperties.NameProperty, "Controls.Update.ReleaseNotes");
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
        SetState(L.M("Controls.Update.AvailableHeading", _version),
            L.M("Controls.Update.AvailableMessage"),
            "Controls.Update.UpdateNow", "Controls.Update.Later");
        _notes.Text = ShortText(notes, 2400);
        _notesScroll.Visibility = _notes.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <param name="progress">Download completion from 0 to 1; NaN means the total size is unknown.</param>
    public void ShowDownloading(double progress)
    {
        SetState(L.M("Controls.Update.DownloadingHeading"),
            L.M("Controls.Update.DownloadingMessage"),
            null, "Controls.Update.CancelDownload", announce: false);
        _canCancel = true;
        _progress.Visibility = _progressLabel.Visibility = Visibility.Visible;
        _progress.IsIndeterminate = !double.IsFinite(progress);
        _progress.Value = double.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 0;
        if (_progress.IsIndeterminate) L.Bind(_progressLabel, TextBlock.TextProperty, "Controls.Update.Downloading");
        else L.Bind(_progressLabel, TextBlock.TextProperty, "Controls.Update.Downloaded", _progress.Value);
        L.Bind(this, AutomationProperties.NameProperty, "Controls.Update.DownloadingName");
    }

    public void ShowReady(string version)
    {
        _version = ShortText(version, 64);
        SetState(L.M("Controls.Update.ReadyHeading", _version),
            L.M("Controls.Update.ReadyMessage"),
            "Controls.Update.Install", "Controls.Update.Later");
    }
    public void ShowPreparing()
    {
        SetState(L.M("Controls.Update.PreparingHeading"),
            L.M("Controls.Update.PreparingMessage"), null, null);
        _progress.Visibility = Visibility.Visible;
        _progress.IsIndeterminate = true;
    }

    public void ShowError(string message) => ShowError(L.M("Controls.Update.RawError", ShortText(message, 600)));

    public void ShowError(LocalizedMessage message)
    {
        SetState(L.M("Controls.Update.ErrorHeading"), L.M("Controls.Update.RawError", new BoundedError(message)), "Controls.Update.Retry", "Controls.Update.Later");
    }

    public void ShowCurrent(string version)
    {
        _version = ShortText(version, 64);
        SetState(L.M("Controls.Update.CurrentHeading"),
            L.M("Controls.Update.CurrentMessage", _version), null, "Controls.Common.Done");
    }

    public void ShowChecking()
    {
        SetState(L.M("Controls.Update.CheckingHeading"),
            L.M("Controls.Update.CheckingMessage"), null, "Controls.Update.Dismiss");
        _progress.Visibility = Visibility.Visible;
        _progress.IsIndeterminate = true;
    }

    private void SetState(LocalizedMessage heading, LocalizedMessage message, string? primary, string? secondary, bool announce = true)
    {
        L.Bind(_heading, TextBlock.TextProperty, heading.Key, heading.Arguments);
        L.Bind(_message, TextBlock.TextProperty, message.Key, message.Arguments);
        _notesScroll.Visibility = _progress.Visibility = _progressLabel.Visibility = Visibility.Collapsed;
        _canCancel = false;
        SetButton(_primary, primary);
        SetButton(_secondary, secondary);
        Visibility = Visibility.Visible;
        BindingOperations.SetBinding(this, AutomationProperties.NameProperty, L.FormatBinding("Controls.Update.AccessibleMessage",
            new Binding(nameof(TextBlock.Text)) { Source = _heading }, new Binding(nameof(TextBlock.Text)) { Source = _message }));
        if (announce && UIElementAutomationPeer.FromElement(this) is { } peer)
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private static void SetButton(Button button, string? content)
    {
        button.Visibility = content is null ? Visibility.Collapsed : Visibility.Visible;
        if (content is null)
        {
            button.Content = null;
            AutomationProperties.SetName(button, "");
        }
        else
        {
            L.Bind(button, ContentControl.ContentProperty, content);
            L.Bind(button, AutomationProperties.NameProperty, content);
        }
    }

    private sealed record BoundedError(LocalizedMessage Message)
    {
        public override string ToString() => ShortText(Message.ToString(), 600);
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
