using System.IO;
using System.Reflection;
using Microsoft.Win32;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Moye.Controls;
using Moye.Models;
using Moye.Services;

namespace Moye.UiPreview;

/// <summary>Detached update notification states; no network, installer, native window, or user notes.</summary>
internal static class UpdatePreviewScenarios
{
    public static void Run(string output)
    {
        VerifyActions();
        VerifyWindowChecks(output);
        var reports = new List<object>();
        foreach (var (width, height) in new[] { (1024, 700), (1400, 960) })
        foreach (var state in new[] { "available", "downloading", "ready", "preparing", "error", "current", "checking" })
        {
            var card = new UpdateNotification();
            var host = new Grid { Background = (Brush)Application.Current.Resources["Canvas"] };
            host.Children.Add(new TextBlock
            {
                Text = "Penroam", FontSize = 28, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["Ink"], Margin = new Thickness(32),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
            });
            host.Children.Add(card);
            var focused = Keyboard.FocusedElement;
            switch (state)
            {
                case "available":
                    card.ShowAvailable("1.2.0", "A more comfortable place for your notes.\n\n• Type freely on ruled paper.\n• Record meetings with typing and handwriting.\n• Keep questions with their source page.\n\n" + string.Join("\n", Enumerable.Repeat("Additional fixes and improvements for writing, pages, and exports.", 25)));
                    break;
                case "downloading": card.ShowDownloading(.37); break;
                case "ready": card.ShowReady("1.2.0"); break;
                case "preparing": card.ShowPreparing(); break;
                case "error": card.ShowError("The download could not be verified. Your notes are safe and Penroam is still open. Check your connection and try again."); break;
                case "current": card.ShowCurrent("1.2.0"); break;
                case "checking": card.ShowChecking(); break;
            }
            if (!ReferenceEquals(focused, Keyboard.FocusedElement)) throw new InvalidOperationException("Showing an update notification moved keyboard focus.");
            host.Measure(new Size(width, height)); host.Arrange(new Rect(0, 0, width, height)); host.UpdateLayout();
            var cardBounds = card.TransformToAncestor(host).TransformBounds(new Rect(card.RenderSize));
            if (cardBounds.Left < 0 || cardBounds.Top < 0 || cardBounds.Right > width || cardBounds.Bottom > height || cardBounds.Height > 470)
                throw new InvalidOperationException($"The {state} update notification does not fit the {width} x {height} workspace.");
            var buttons = Descendants<Button>(card).Where(button => button.Visibility == Visibility.Visible).Select(button =>
            {
                var bounds = button.TransformToAncestor(host).TransformBounds(new Rect(button.RenderSize));
                if (bounds.Width < 44 || bounds.Height < 44 || !cardBounds.Contains(bounds))
                    throw new InvalidOperationException($"The {state} update notification has an unreachable action.");
                var name = AutomationProperties.GetName(button);
                if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("An update action has no accessible name.");
                return new { Name = name, bounds.X, bounds.Y, bounds.Width, bounds.Height };
            }).ToArray();
            if (state != "preparing" && buttons.Length == 0) throw new InvalidOperationException($"The {state} notification has no action.");
            if (state == "available" && Descendants<ScrollViewer>(card).Single().ScrollableHeight <= 0)
                throw new InvalidOperationException("Long release notes are not scrollable.");
            var fileName = $"ui-preview-update-{state}-{width}.png";
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(host);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(output, fileName))) encoder.Save(file);
            reports.Add(new { State = state, Width = width, Height = height, Image = fileName, Card = new { cardBounds.X, cardBounds.Y, cardBounds.Width, cardBounds.Height }, Buttons = buttons });
        }
        File.WriteAllText(Path.Combine(output, "update-preview-report.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void VerifyWindowChecks(string output)
    {
        var reports = new List<string>();
        var previousContext = SynchronizationContext.Current;
        // All fake completions below run synchronously on this STA thread. A
        // Dispatcher pump is unnecessary and could run unrelated preview work.
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            WithWindow(output, (window, service, card) =>
            {
                var release = Release("1.2.0"); service.Check = () => Task.FromResult<AppUpdate?>(release);
                Complete(Check(window, false));
                Require(card.Visibility == Visibility.Visible && TextContains(card, "1.2.0 is available"), "Startup did not offer the newer release.");
                Require(ReferenceEquals(Field<AppUpdate?>(window, "_availableUpdate"), release), "The offered release was not retained for installation.");
                Require(!window.ViewModel.IsBusy, "Checking for updates blocked notebook editing.");
            });
            reports.Add("Startup offers a new release without blocking editing");

            WithWindow(output, (window, service, card) =>
            {
                service.Check = () => Task.FromException<AppUpdate?>(new IOException("Synthetic offline connection"));
                Complete(Check(window, false));
                Require(card.Visibility == Visibility.Collapsed, "An offline startup interrupted the user.");
                Complete(Check(window, true));
                Require(card.Visibility == Visibility.Visible && TextContains(card, "Could not check GitHub"), "An explicit failed check did not explain the problem.");
                Require(Logical<Button>(card).Any(button => Equals(button.Content, "Try again") && button.Visibility == Visibility.Visible), "A failed manual check did not offer retry.");
            });
            reports.Add("Offline startup stays quiet; a manual check explains the failure and offers retry");

            foreach (var outcome in new[] { "update", "current", "error" })
                WithWindow(output, (window, service, card) =>
                {
                    var result = new TaskCompletionSource<AppUpdate?>(); service.Check = () => result.Task;
                    var pending = Check(window, true);
                    Require(!pending.IsCompleted && TextContains(card, "Checking for updates"), "The delayed check did not expose its checking state.");
                    Click(card, "Dismiss");
                    if (outcome == "error") result.SetException(new IOException("Synthetic offline connection"));
                    else result.SetResult(outcome == "update" ? Release("1.2.0") : null);
                    Complete(pending);
                    Require(card.Visibility == Visibility.Collapsed, $"A dismissed check reopened its {outcome} result.");
                });
            reports.Add("Dismissed checks stay hidden for update, current, and error outcomes");

            WithWindow(output, (window, service, card) =>
            {
                service.Check = () => Task.FromResult<AppUpdate?>(Release("1.2.0")); Complete(Check(window, false));
                SetField(window, "_downloadedUpdatePath", "synthetic-payload-never-opened.exe");
                Complete(Check(window, true));
                Require(Field<string?>(window, "_downloadedUpdatePath") is not null, "Rechecking the same release unnecessarily discarded the payload.");
                service.Check = () => Task.FromResult<AppUpdate?>(Release("1.3.0")); Complete(Check(window, true));
                Require(Field<string?>(window, "_downloadedUpdatePath") is null, "A new version retained the old version's cached payload.");
                SetField(window, "_downloadedUpdatePath", "synthetic-payload-never-opened.exe");
                service.Check = () => Task.FromResult<AppUpdate?>(Release("1.3.0", 'b')); Complete(Check(window, true));
                Require(Field<string?>(window, "_downloadedUpdatePath") is null, "A replacement release asset retained a payload with a different checksum.");
            });
            reports.Add("Rechecks retain matching payloads and invalidate changed versions or checksums");

            WithWindow(output, (window, service, card) =>
            {
                var result = new TaskCompletionSource<AppUpdate?>(); service.Check = () => result.Task;
                var startup = Check(window, false); Complete(Check(window, true));
                Require(service.CheckCount == 1, "An explicit check duplicated the in-flight startup request.");
                result.SetResult(null); Complete(startup);
                Require(card.Visibility == Visibility.Visible && TextContains(card, "up to date"), "Joining an in-flight check did not show its current-version result.");
            });
            reports.Add("A manual check joins the startup request and reports the current version");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        File.WriteAllText(Path.Combine(output, "update-integration-report.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WithWindow(string output, Action<MainWindow, PreviewUpdateService, UpdateNotification> check)
    {
        using var repository = new EmptyUpdateRepository();
        var service = new PreviewUpdateService();
        var directory = Path.Combine(output, "update-preview-unused", Guid.NewGuid().ToString("N"));
        var window = new MainWindow(repository, new WritingPreferencesStore(Path.Combine(directory, "preferences.json")),
            new ErrorReporter(Path.Combine(directory, "error.log")), service, new NoUpdateInstaller());
        try
        {
            Require(new WindowInteropHelper(window).Handle == IntPtr.Zero, "An update check created a native window.");
            check(window, service, (UpdateNotification)window.FindName("UpdateNotice"));
            Require(new WindowInteropHelper(window).Handle == IntPtr.Zero, "An update check showed a native window.");
            Require(!Directory.Exists(directory), "An update check wrote to a preview library.");
        }
        finally
        {
            // Close bypasses the real save/install path, since this fixture was
            // never loaded and owns no library. Remove the static system event
            // subscription that the normal close path would release.
            var powerHandler = (PowerModeChangedEventHandler)typeof(MainWindow).GetMethod("PowerModeChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate(typeof(PowerModeChangedEventHandler), window);
            SystemEvents.PowerModeChanged -= powerHandler;
            foreach (var field in typeof(MainWindow).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                if (field.GetValue(window) is DispatcherTimer timer) timer.Stop();
            SetField(window, "_closing", true);
            window.ViewModel.Dispose(); window.Close();
        }
    }

    private static Task Check(MainWindow window, bool manual) => (Task)typeof(MainWindow)
        .GetMethod("CheckForUpdatesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [manual])!;
    private static void Complete(Task task)
    {
        Require(task.IsCompleted, "A fake update check escaped the synchronous STA preview.");
        task.GetAwaiter().GetResult();
    }
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static void SetField(MainWindow window, string name, object? value) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
    private static bool TextContains(UpdateNotification card, string value) => Logical<TextBlock>(card).Any(block => block.Text.Contains(value, StringComparison.OrdinalIgnoreCase));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static AppUpdate Release(string version, char checksum = 'a')
    {
        var tag = "v" + version; var file = $"Penroam-{version}-Setup-win-x64.exe";
        return new AppUpdate(Version.Parse(version), tag, "Synthetic release notes", new Uri($"https://github.com/yikeugene/Penroam/releases/tag/{tag}"),
            new Uri($"https://github.com/yikeugene/Penroam/releases/download/{tag}/{file}"), file, 16, new string(checksum, 64), null);
    }
    private sealed class PreviewUpdateService : IAppUpdateService
    {
        public Func<Task<AppUpdate?>> Check { get; set; } = () => Task.FromResult<AppUpdate?>(null);
        public int CheckCount { get; private set; }
        public Task<AppUpdate?> CheckAsync(CancellationToken cancellationToken = default) { CheckCount++; cancellationToken.ThrowIfCancellationRequested(); return Check(); }
        public Task<string> DownloadAsync(AppUpdate update, IProgress<double>? progress = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("A read-only update check attempted to download an installer.");
    }
    private sealed class NoUpdateInstaller : IUpdateInstaller
    {
        public void Launch(string installerPath) => throw new InvalidOperationException("A read-only update check attempted to launch an installer.");
    }
    private sealed class EmptyUpdateRepository : INotebookRepository
    {
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<NotebookSummary>> ListAsync() => Task.FromResult<IReadOnlyList<NotebookSummary>>([]);
        public Task<NotebookDocument?> LoadAsync(string id) => Task.FromResult<NotebookDocument?>(null);
        public Task SaveAsync(NotebookDocument document) => throw new NotSupportedException();
        public Task DeleteAsync(string id) => throw new NotSupportedException();
        public Task<AssetData> PutAssetAsync(string fileName, string contentType, byte[] bytes) => throw new NotSupportedException();
        public Task<AssetData> GetAssetAsync(string id) => throw new NotSupportedException();
        public void Dispose() { }
    }
    private static void VerifyActions()
    {
        var card = new UpdateNotification();
        if (card.Visibility != Visibility.Collapsed) throw new InvalidOperationException("An idle update card must be hidden.");
        var update = 0; var dismiss = 0; var cancel = 0;
        card.UpdateRequested += (_, _) => update++;
        card.DismissRequested += (_, _) => dismiss++;
        card.CancelRequested += (_, _) => cancel++;
        card.ShowAvailable("1.2.0", "Release notes");
        Click(card, "Update now"); Click(card, "Later");
        card.ShowDownloading(.5); Click(card, "Cancel download");
        card.ShowError("Try again when your connection is restored."); Click(card, "Try again"); Click(card, "Later");
        card.ShowReady("1.2.0"); Click(card, "Install update"); Click(card, "Later");
        card.ShowCurrent("1.2.0"); Click(card, "Done");
        card.ShowChecking(); Click(card, "Dismiss");
        if (update != 3 || dismiss != 5 || cancel != 1) throw new InvalidOperationException("Update card actions were routed incorrectly.");
        card.ShowDownloading(double.NaN);
        var progress = Logical<ProgressBar>(card).Single();
        if (!progress.IsIndeterminate) throw new InvalidOperationException("Unknown download sizes must use an indeterminate progress bar.");
        card.ShowDownloading(2);
        if (progress.IsIndeterminate || progress.Value != 1) throw new InvalidOperationException("Download progress was not clamped to a valid range.");
        card.ShowPreparing();
        if (Logical<Button>(card).Any(button => button.Visibility == Visibility.Visible)) throw new InvalidOperationException("Preparing an install exposed a second update action.");
        var peer = UIElementAutomationPeer.CreatePeerForElement(card);
        if (peer?.GetAutomationControlType() != AutomationControlType.Group || AutomationProperties.GetLiveSetting(card) != AutomationLiveSetting.Polite)
            throw new InvalidOperationException("Update state changes are not accessible to a screen reader.");
    }

    private static void Click(UpdateNotification card, string label) => Logical<Button>(card).Single(button => Equals(button.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static IEnumerable<T> Logical<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        { if (child is T item) yield return item; foreach (var nested in Logical<T>(child)) yield return nested; }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        { var child = VisualTreeHelper.GetChild(root, index); if (child is T item) yield return item; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
}
