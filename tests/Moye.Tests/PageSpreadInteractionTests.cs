using System.Collections.Specialized;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using System.Windows.Threading;
using Microsoft.Win32;
using Moye.Controls;
using Moye.Models;
using Moye.Services;
using Moye.ViewModels;

namespace Moye.Tests;

[CollectionDefinition("Page spread shell", DisableParallelization = true)]
public sealed class PageSpreadShellCollection : ICollectionFixture<PageSpreadTestApplication> { }

/// <summary>Production templates and handlers on an STA thread, without showing a desktop window or opening user data.</summary>
[Collection("Page spread shell")]
public sealed class PageSpreadInteractionTests(PageSpreadTestApplication application)
{
    [Fact]
    public void LongDocumentRecyclesBoundedRowsAndNavigatesToTheLastRightPage() => application.Run(() =>
    {
        using var fixture = new ShellFixture(160);
        fixture.Mode(true);
        Assert.Equal(80, fixture.Window.PageSpreads.Count);
        Assert.True(VirtualizingPanel.GetIsVirtualizing(fixture.List));
        Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(fixture.List));
        Assert.Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(fixture.List));
        Assert.InRange(fixture.Editors.Count, 2, 20);
        var right = fixture.Model.Pages[^1];
        fixture.Navigate(right);
        Assert.Same(right, fixture.Model.SelectedPage);
        Assert.Same(right, Assert.IsType<PageSpreadViewModel>(fixture.List.SelectedItem).Second);
        Assert.Same(right.Page, fixture.Editor(right).Page);
        Assert.InRange(fixture.Editors.Count, 2, 20);
        Assert.True(fixture.Scroll.VerticalOffset > 1000);
    });

    [Fact]
    public void ProductionHostsTopAlignUnequalPagesAndCenterTheOddTail() => application.Run(() =>
    {
        using var fixture = new ShellFixture(3, document =>
        {
            document.Pages[0].Width = 900; document.Pages[0].Height = 450;
            document.Pages[1].Width = 500; document.Pages[1].Height = 1200;
            document.Pages[2].Width = 460; document.Pages[2].Height = 720;
        });
        fixture.Model.Zoom = .5; fixture.Mode(true);
        var first = fixture.Bounds(fixture.Model.Pages[0]);
        var second = fixture.Bounds(fixture.Model.Pages[1]);
        Assert.InRange(Math.Abs(first.Top - second.Top), 0, 1);
        Assert.InRange(second.Left - first.Right, 23, 25);
        Assert.InRange(first.Width, 449, 451); Assert.InRange(second.Width, 249, 251);
        Assert.InRange(first.Height, 224, 226); Assert.InRange(second.Height, 599, 601);
        fixture.Navigate(fixture.Model.Pages[2]);
        var tail = fixture.Bounds(fixture.Model.Pages[2]);
        var viewport = (Rect)Invoke(fixture.Window, "PageViewportBounds")!;
        Assert.InRange(Math.Abs(tail.Left + tail.Width / 2 - viewport.Left - viewport.Width / 2), 0, 2);
        Assert.Null(fixture.Window.PageSpreads[^1].Second);
    });

    [Fact]
    public void ActiveRightPageSurvivesVisibilityReadingCaptureAndBothModeSwitches() => application.Run(() =>
    {
        using var fixture = new ShellFixture(4);
        fixture.Model.Zoom = .5; fixture.Mode(true);
        var right = fixture.Model.Pages[1];
        Invoke(fixture.Window, "ActivatePage", fixture.Host(right));
        Invoke(fixture.Window, "UpdateVisiblePageSelection");
        Assert.Same(right, fixture.Model.SelectedPage);
        Assert.Same(fixture.Window.PageSpreads[0], fixture.List.SelectedItem);

        Set(fixture.Window, "_workspaceLoaded", true);
        Invoke(fixture.Window, "CaptureReadingPosition");
        var workspace = Get<WorkspacePreferences>(fixture.Window, "_workspace");
        Assert.Equal(right.Page.Id, workspace.ReadingPositions[fixture.Model.Document!.Id].PageId);
        // Persistence follows the active page; a physical pointer over the left
        // page must still zoom about the left page, not the active right page.
        var leftBounds = fixture.Bounds(fixture.Model.Pages[0]);
        var pointerAnchor = Invoke(fixture.Window, "CapturePageZoomAnchor", new Point(leftBounds.Left + 30, leftBounds.Top + 30))!;
        Assert.Same(fixture.Model.Pages[0], pointerAnchor.GetType().GetProperty("Page")!.GetValue(pointerAnchor));

        fixture.Mode(false);
        Assert.Same(right, fixture.Model.SelectedPage); Assert.Equal(.5, fixture.Model.Zoom);
        Assert.Same(right, Assert.IsType<PageSpreadViewModel>(fixture.List.SelectedItem).First);
        Assert.Equal("Fit Page", fixture.Window.FindName("FitPageButton") is Button single ? single.Content : null);
        fixture.Mode(true);
        Assert.Same(right, fixture.Model.SelectedPage); Assert.True(workspace.TwoPageView);
        Assert.Equal("Fit Spread", ((Button)fixture.Window.FindName("FitPageButton")).Content);
        Assert.False(fixture.Model.CanUndo); Assert.False(fixture.Model.Autosave.IsDirty);
    });

    [Fact]
    public void RightPageNavigationRevealsTheExactChildAndHorizontalVisibilityChangesSelection() => application.Run(() =>
    {
        using var fixture = new ShellFixture(4, document => document.Pages.ForEach(page => page.Width = 1800));
        fixture.Model.Zoom = 1; fixture.Mode(true);
        var right = fixture.Model.Pages[1];
        fixture.Navigate(right);
        Assert.True(fixture.Scroll.HorizontalOffset > 1000);
        var viewport = (Rect)Invoke(fixture.Window, "PageViewportBounds")!;
        var bounds = fixture.Bounds(right);
        Assert.InRange(Math.Abs(bounds.Left - viewport.Left), 0, 2);
        Assert.Same(right, fixture.Model.SelectedPage);

        fixture.Scroll.ScrollToHorizontalOffset(0); fixture.Layout();
        Invoke(fixture.Window, "UpdateVisiblePageSelection");
        Assert.Same(fixture.Model.Pages[0], fixture.Model.SelectedPage);
    });

    [Fact]
    public void RightThumbnailRevealsShortPageAfterScrollingDownTallPartner() => application.Run(() =>
    {
        using var fixture = new ShellFixture(2, document =>
        {
            document.Pages[0].Height = 3000;
            document.Pages[1].Height = 400;
        });
        fixture.Model.Zoom = .5; fixture.Mode(true);
        var right = fixture.Model.Pages[1];
        fixture.Scroll.ScrollToVerticalOffset(500); fixture.Layout();
        var viewport = (Rect)Invoke(fixture.Window, "PageViewportBounds")!;
        Assert.False(Rect.Intersect(fixture.Bounds(fixture.Model.Pages[0]), viewport).IsEmpty);
        Assert.True(fixture.Bounds(right).Bottom <= viewport.Top);

        ((ListBox)fixture.Window.FindName("ThumbnailList")).SelectedItem = right;
        fixture.Layout();

        var visible = Rect.Intersect(fixture.Bounds(right), viewport);
        Assert.False(visible.IsEmpty);
        Assert.True(visible.Width > 0 && visible.Height > 0);
        Assert.Same(right, fixture.Model.SelectedPage);
        Assert.Same(right, Assert.IsType<PageSpreadViewModel>(fixture.List.SelectedItem).Second);
    });

    [Fact]
    public void LayoutSwitchRespectsBusyAndPenContactAndKeepsTheBringIntoViewGuard() => application.Run(() =>
    {
        using var fixture = new ShellFixture(6);
        fixture.Model.Zoom = 1; fixture.Mode(true);
        fixture.Navigate(fixture.Model.Pages[1]);
        Set(fixture.Window, "_viewportPenDeviceId", (int?)7);
        var offset = new Point(fixture.Scroll.HorizontalOffset, fixture.Scroll.VerticalOffset);
        fixture.Mode(false);
        Assert.True(Get<bool>(fixture.Window, "_twoPageView"));
        fixture.Host(fixture.Model.Pages[0]).BringIntoView(); fixture.Layout();
        Assert.Equal(offset.X, fixture.Scroll.HorizontalOffset, 6);
        Assert.Equal(offset.Y, fixture.Scroll.VerticalOffset, 6);
        Set(fixture.Window, "_viewportPenDeviceId", null);
        fixture.Model.IsBusy = true; fixture.Mode(false);
        Assert.True(Get<bool>(fixture.Window, "_twoPageView"));
        fixture.Model.IsBusy = false; fixture.Mode(false);
        Assert.False(Get<bool>(fixture.Window, "_twoPageView"));
    });

    [Fact]
    public void KeyboardNavigationUsesIndividualPagesAndTextFocusActivatesTheRightEditor() => application.Run(() =>
    {
        using var fixture = new ShellFixture(5, document => document.Pages[1].Texts.Add(new() { Text = "Editable right page" }));
        fixture.Model.Zoom = .5; fixture.Mode(true);
        fixture.Key(Key.Right); Assert.Equal(2, fixture.Model.SelectedPage!.Number);
        fixture.Key(Key.Down); Assert.Equal(4, fixture.Model.SelectedPage!.Number);
        fixture.Key(Key.Left); Assert.Equal(3, fixture.Model.SelectedPage!.Number);
        fixture.Key(Key.End); Assert.Equal(5, fixture.Model.SelectedPage!.Number);
        fixture.Key(Key.Home); Assert.Equal(1, fixture.Model.SelectedPage!.Number);
        var right = fixture.Model.Pages[1];
        var box = Assert.Single(Descendants<TextBox>(fixture.Editor(right)));
        box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, box)
        { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
        Assert.Same(right, fixture.Model.SelectedPage);
        fixture.Key(Key.Right, box);
        Assert.Same(right, fixture.Model.SelectedPage);
    });

    [Fact]
    public void ProjectionBatchesPageChangesAndStaysCurrentThroughSectionDeleteMoveAndUndo() => application.Run(() =>
    {
        using var fixture = new ShellFixture(3);
        fixture.Mode(true);
        var notifications = 0;
        fixture.Window.PageSpreads.CollectionChanged += (_, e) =>
        { Assert.Equal(NotifyCollectionChangedAction.Reset, e.Action); notifications++; };
        fixture.Model.AppendPages(Enumerable.Range(0, 40).Select(_ => new NotePage()).ToArray());
        Assert.Equal(1, notifications);
        AssertProjection(fixture);
        fixture.Navigate(fixture.Model.SelectedPage!);
        fixture.Model.MovePage(1); AssertProjection(fixture);
        fixture.Model.DeletePage(); AssertProjection(fixture);
        fixture.Model.Undo(); AssertProjection(fixture);
        fixture.Model.AddSection("Other topic"); AssertProjection(fixture);
        Assert.Single(fixture.Window.PageSpreads);
        fixture.Model.DeletePage();
        Assert.Empty(fixture.Window.PageSpreads);
        fixture.Model.Undo(); AssertProjection(fixture);
        fixture.Model.SelectedSection = fixture.Model.Sections[0]; AssertProjection(fixture);
        fixture.Navigate(fixture.Model.Pages[^1]);
        Assert.Same(fixture.Model.SelectedPage!.Page, fixture.Editor(fixture.Model.SelectedPage).Page);
    });

    [Fact]
    public void InkAndZoomDoNotRebuildRowsAndModeSwitchCommitsBothEditorsWithoutLayoutHistory() => application.Run(() =>
    {
        using var fixture = new ShellFixture(2, document => document.Pages.ForEach(page => page.Texts.Add(new() { Text = "Original" })));
        fixture.Model.Zoom = .5; fixture.Mode(true);
        var row = fixture.Window.PageSpreads[0];
        fixture.Model.Zoom = .55; fixture.Layout();
        Assert.Same(row, fixture.Window.PageSpreads[0]);
        var historyChanges = 0;
        fixture.Model.HistoryChanged += (_, _) => historyChanges++;
        var left = fixture.Editor(fixture.Model.Pages[0]);
        var right = fixture.Editor(fixture.Model.Pages[1]);
        Assert.Single(Descendants<TextBox>(left)).Text = "Pending left";
        Assert.Single(Descendants<TextBox>(right)).Text = "Pending right";
        left.InkCanvas.Strokes.Add(Stroke(40)); right.InkCanvas.Strokes.Add(Stroke(80));
        Assert.Same(row, fixture.Window.PageSpreads[0]);
        fixture.Mode(false);
        Assert.Equal(2, historyChanges);
        Assert.Equal("Pending left", fixture.Model.Document!.Pages[0].Texts[0].Text);
        Assert.Equal("Pending right", fixture.Model.Document.Pages[1].Texts[0].Text);
        Assert.All(fixture.Model.Document.Pages, page => Assert.NotEmpty(page.InkData));
        fixture.Mode(true);
        Assert.Equal(2, historyChanges);
        fixture.Model.Autosave.FlushAsync().GetAwaiter().GetResult();
        Assert.Equal("Pending right", fixture.Repository.Saved!.Pages[1].Texts[0].Text);
        fixture.Model.Undo(); fixture.Model.Undo(); fixture.Layout();
        Assert.All(fixture.Model.Document!.Pages, page =>
        { Assert.Equal("Original", page.Texts[0].Text); Assert.Empty(page.InkData); });
        Assert.False(fixture.Model.CanUndo);
        fixture.Model.Redo(); fixture.Model.Redo();
        Assert.Equal("Pending left", fixture.Model.Document.Pages[0].Texts[0].Text);
        Assert.NotEmpty(fixture.Model.Document.Pages[1].InkData);
    });

    [Fact]
    public Task OrganizationBackNavigationCapturesTheRightPageAndDoesNotOverwriteItsRestoredAnchor() => application.RunAsync(async () =>
    {
        using var fixture = new ShellFixture(6);
        fixture.Model.Zoom = .5; fixture.Mode(true);
        var right = fixture.Model.Pages[1];
        fixture.Navigate(right);
        fixture.Scroll.ScrollToVerticalOffset(180); fixture.Layout();
        Invoke(fixture.Window, "RememberOrganizationVisit");
        var back = (System.Collections.IList)Get<object>(fixture.Window, "_organizationBack");
        var visit = Assert.Single(back.Cast<object>());
        Assert.Equal(right.Page.Id, visit.GetType().GetProperty("PageId")!.GetValue(visit));
        var fraction = (Point)visit.GetType().GetProperty("PageFraction")!.GetValue(visit)!;
        var view = (Point)visit.GetType().GetProperty("ViewportFraction")!.GetValue(visit)!;
        fixture.Navigate(fixture.Model.Pages[5]);

        Assert.True(await (Task<bool>)Invoke(fixture.Window, "NavigateOrganizationAsync",
            fixture.Model.Document!.Id, right.Page.SectionId, right.Page.Id, visit)!);
        fixture.Layout();

        Assert.Equal(right.Page.Id, fixture.Model.SelectedPage!.Page.Id);
        var host = fixture.Host(fixture.Model.SelectedPage);
        var restored = host.TranslatePoint(new Point(fraction.X * host.ActualWidth, fraction.Y * host.ActualHeight), fixture.Viewport);
        Assert.InRange(Math.Abs(restored.Y - view.Y * fixture.Viewport.ActualHeight), 0, 2);
    });

    [Fact]
    public Task WorkspaceModeLoadsBeforeRestoringTheRightPageAndItsReadingLine() => application.RunAsync(async () =>
    {
        using var fixture = new ShellFixture(4);
        var target = fixture.Model.Pages[1];
        var preferences = new WorkspacePreferences { TwoPageView = true, LastNotebookId = fixture.Model.Document!.Id };
        preferences.ReadingPositions[fixture.Model.Document.Id] = new(target.Page.SectionId, target.Page.Id,
            .5, .6, .75, .3, .5, false, DateTimeOffset.UtcNow);
        var store = new WorkspacePreferencesStore(fixture.WorkspacePath);
        await store.LoadAsync(); await store.SaveAsync(preferences);
        fixture.Navigate(fixture.Model.Pages[3]);

        await (Task)Invoke(fixture.Window, "LoadWorkspaceAsync")!;
        fixture.Layout();
        Assert.True(Get<bool>(fixture.Window, "_twoPageView"));
        Assert.Equal(2, fixture.Window.PageSpreads.Count);
        Assert.True(await (Task<bool>)Invoke(fixture.Window, "RestoreReadingPositionAsync")!);
        fixture.Layout();

        Assert.Equal(target.Page.Id, fixture.Model.SelectedPage!.Page.Id);
        Assert.Same(fixture.Model.SelectedPage, fixture.Window.PageSpreads[0].Second);
        Assert.Equal(.5, fixture.Model.Zoom);
        var host = fixture.Host(fixture.Model.SelectedPage);
        var restored = host.TranslatePoint(new Point(host.ActualWidth * .5, host.ActualHeight * .6), fixture.Viewport);
        Assert.InRange(Math.Abs(restored.Y - fixture.Viewport.ActualHeight * .3), 0, 2);
    });

    [Fact]
    public void FitWidthRemainsStickyAcrossModesWhileFitSpreadIsOneTime() => application.Run(() =>
    {
        using var fixture = new ShellFixture(3);
        fixture.Mode(true); fixture.Navigate(fixture.Model.Pages[1]);
        Invoke(fixture.Window, "FitWidth"); fixture.Layout();
        Assert.True(Get<bool>(fixture.Window, "_fitWidthActive"));
        AssertFitWidth(fixture);
        fixture.Scroll.ScrollToVerticalOffset(200); fixture.Layout();
        var anchor = Invoke(fixture.Window, "CaptureSelectedPageAnchor", new Point(fixture.Viewport.ActualWidth / 2, 80))!;
        var pagePoint = (Point)anchor.GetType().GetProperty("PagePoint")!.GetValue(anchor)!;
        var viewportPoint = (Point)anchor.GetType().GetProperty("ViewportPoint")!.GetValue(anchor)!;
        void AssertReadingLine()
        {
            var host = fixture.Host(fixture.Model.SelectedPage!);
            var actual = host.TranslatePoint(new Point(pagePoint.X * host.ActualWidth / fixture.Model.SelectedPage!.Page.Width,
                pagePoint.Y * host.ActualHeight / fixture.Model.SelectedPage.Page.Height), fixture.Viewport);
            Assert.InRange(Math.Abs(actual.Y - viewportPoint.Y), 0, 2);
        }
        fixture.Mode(false); AssertFitWidth(fixture); AssertReadingLine();
        Assert.Equal(2, fixture.Model.SelectedPage!.Number);
        fixture.Mode(true); AssertFitWidth(fixture); AssertReadingLine();
        Invoke(fixture.Window, "FitPage"); fixture.Layout();
        Assert.False(Get<bool>(fixture.Window, "_fitWidthActive"));
        var spread = Assert.IsType<PageSpreadViewModel>(fixture.List.SelectedItem);
        Assert.Equal(PageSpreadGeometry.FitPageZoom(spread, fixture.Viewport.ActualWidth - 90, fixture.Viewport.ActualHeight - 106), fixture.Model.Zoom, 6);
        var fitZoom = fixture.Model.Zoom;
        fixture.Mode(false);
        Assert.Equal(fitZoom, fixture.Model.Zoom);
    });

    private static void AssertFitWidth(ShellFixture fixture)
    {
        fixture.Layout();
        var spread = Assert.IsType<PageSpreadViewModel>(fixture.List.SelectedItem);
        var width = fixture.Viewport.ActualWidth - fixture.List.Padding.Left - fixture.List.Padding.Right - SystemParameters.VerticalScrollBarWidth - 4;
        Assert.Equal(PageSpreadGeometry.FitWidthZoom(spread, width), fixture.Model.Zoom, 6);
    }

    private static void AssertProjection(ShellFixture fixture) => Assert.Equal(fixture.Model.Pages,
        fixture.Window.PageSpreads.SelectMany(row => row.Second is { } second ? new[] { row.First, second } : [row.First]));

    private static Stroke Stroke(double y) => new(new StylusPointCollection { new StylusPoint(40, y, .3f), new StylusPoint(120, y + 20, .8f) });

    internal sealed class ShellFixture : IDisposable
    {
        private readonly StorageTestDirectory _directory = new();
        private readonly HwndSource _source;
        private readonly FrameworkElement _content;
        public MainWindow Window { get; }
        public MainViewModel Model => Window.ViewModel;
        public ListBox List => (ListBox)Window.FindName("PageList");
        public FrameworkElement Viewport => (FrameworkElement)Window.FindName("Viewport");
        public ScrollViewer Scroll => Descendants<ScrollViewer>(List).First();
        public Dictionary<Border, PageEditor> Editors => Get<Dictionary<Border, PageEditor>>(Window, "_editors");
        public ShellRepository Repository { get; }
        public string WorkspacePath => Path.Combine(_directory.Root, "workspace-preferences.json");

        public ShellFixture(int count, Action<NotebookDocument>? configure = null)
        {
            var document = new NotebookDocument { Title = "Synthetic spread fixture", Pages = Enumerable.Range(0, count).Select(_ => new NotePage { Width = 800, Height = 1100 }).ToList() };
            configure?.Invoke(document); NotebookStructure.Normalize(document);
            Repository = new(document);
            Window = new(Repository, new WritingPreferencesStore(Path.Combine(_directory.Root, "writing.json")),
                new ErrorReporter(Path.Combine(_directory.Root, "errors.log")));
            _content = (FrameworkElement)Window.Content;
            Window.Content = null;
            _content.DataContext = Model;
            _content.Resources.MergedDictionaries.Add(Window.Resources);
            TextElement.SetFontFamily(_content, Window.FontFamily);
            TextElement.SetFontSize(_content, Window.FontSize);
            TextElement.SetForeground(_content, Window.Foreground);
            _content.UseLayoutRounding = Window.UseLayoutRounding;
            _content.SnapsToDevicePixels = Window.SnapsToDevicePixels;
            // A hidden presentation source supplies genuine Loaded/Unloaded and
            // recycling semantics. WS_VISIBLE is absent; no desktop input is sent.
            _source = new(new HwndSourceParameters("Penroam spread test")
            { Width = 1400, Height = 960, WindowStyle = unchecked((int)0x80000000) });
            _source.RootVisual = _content;
            Model.OpenAsync(document.Id).GetAwaiter().GetResult();
            Layout(); Set(Window, "_ready", true);
            Invoke(Window, "EnsurePageBringIntoViewGuard");
        }

        public Border Host(PageViewModel page) => Editors.Keys.Single(host => ReferenceEquals(host.DataContext, page));
        public PageEditor Editor(PageViewModel page) => Editors[Host(page)];
        public Rect Bounds(PageViewModel page)
        {
            var host = Host(page);
            return new(host.TranslatePoint(new Point(), Viewport), host.RenderSize);
        }
        public void Mode(bool twoPages) { Invoke(Window, "ChangePageLayout", twoPages); Layout(); }
        public void Navigate(PageViewModel page)
        { Model.SelectedPage = page; Invoke(Window, "ScrollToSelected"); Layout(); }
        public void Key(Key key, UIElement? source = null)
        {
            (source ?? List).RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, _source, 0, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Layout();
        }
        public KeyEventArgs PreviewWindowKey(Key key, UIElement source)
        {
            // The Window is deliberately detached; invoke only its preview handler
            // so native button activation cannot open a modal dialog in this fixture.
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, _source, 0, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = source };
            Invoke(Window, "WindowKeyDown", Window, args);
            return args;
        }
        public void Layout()
        {
            for (var pass = 0; pass < 3; pass++)
            {
                _content.Measure(new Size(1400, 960));
                _content.Arrange(new Rect(0, 0, 1400, 960));
                _content.UpdateLayout();
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
            }
        }
        public void Dispose()
        {
            Set(Window, "_closing", true);
            foreach (var timer in typeof(MainWindow).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                         .Where(field => field.FieldType == typeof(DispatcherTimer)).Select(field => (DispatcherTimer?)field.GetValue(Window))) timer?.Stop();
            Invoke(Window, "ClearTouches", false);
            foreach (var host in Editors.Keys.ToArray()) Invoke(Window, "DetachPage", host);
            SystemEvents.PowerModeChanged -= (PowerModeChangedEventHandler)Delegate.CreateDelegate(typeof(PowerModeChangedEventHandler), Window,
                typeof(MainWindow).GetMethod("PowerModeChanged", BindingFlags.Instance | BindingFlags.NonPublic)!);
            _source.Dispose(); Model.Dispose(); Window.Close(); _directory.Dispose();
        }
    }

    internal sealed class ShellRepository(NotebookDocument document) : INotebookRepository
    {
        public NotebookDocument? Saved { get; private set; }
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<NotebookSummary>> ListAsync() => Task.FromResult<IReadOnlyList<NotebookSummary>>([]);
        public Task<NotebookDocument?> LoadAsync(string id) => Task.FromResult<NotebookDocument?>(document.Snapshot());
        public Task SaveAsync(NotebookDocument value) { Saved = value.Snapshot(); return Task.CompletedTask; }
        public Task DeleteAsync(string id) => Task.CompletedTask;
        public Task<AssetData> PutAssetAsync(string name, string contentType, byte[] bytes) => throw new InvalidOperationException("No fixture assets");
        public Task<AssetData> GetAssetAsync(string id) => throw new InvalidOperationException("No fixture assets");
        public void Dispose() { }
    }

    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object?[] args)
    {
        try { return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}

public sealed class PageSpreadTestApplication : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private Dispatcher? _dispatcher;
    private Exception? _failure;
    private Application? _application;

    public PageSpreadTestApplication()
    {
        _thread = new Thread(() =>
        {
            try
            {
                // Like the detached preview, load only App.xaml's resource dictionary.
                // A plain Application can never execute the real library startup.
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, Resources = ReadApplicationResources() };
                _application = application;
                _dispatcher = Dispatcher.CurrentDispatcher;
                _started.Set(); application.Run();
            }
            catch (Exception exception) { _failure = exception; _started.Set(); }
        }) { IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA); _thread.Start();
        Assert.True(_started.Wait(TimeSpan.FromSeconds(30)), "WPF shell test thread did not start.");
        if (_failure is not null) ExceptionDispatchInfo.Capture(_failure).Throw();
    }

    public void Run(Action action)
    {
        var task = _dispatcher!.InvokeAsync(action).Task;
        Assert.True(Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(45))).GetAwaiter().GetResult() == task, "Spread interaction test exceeded 45 seconds.");
        task.GetAwaiter().GetResult();
    }

    public async Task RunAsync(Func<Task> action)
    {
        var task = _dispatcher!.InvokeAsync(action).Task.Unwrap();
        await task.WaitAsync(TimeSpan.FromSeconds(45));
    }

    public void Dispose()
    {
        _dispatcher?.BeginInvoke(new Action(() => _application?.Shutdown()));
        Assert.True(_thread.Join(TimeSpan.FromSeconds(30)), "WPF shell test thread did not stop.");
        _started.Dispose();
    }

    private static ResourceDictionary ReadApplicationResources()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "src", "Moye", "App.xaml");
            if (File.Exists(path))
            {
                var app = XDocument.Load(path).Root!;
                XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                var resources = new XElement(wpf + "ResourceDictionary", app.Attributes().Where(attribute => attribute.IsNamespaceDeclaration),
                    app.Element(wpf + "Application.Resources")!.Elements());
                return (ResourceDictionary)XamlReader.Parse(resources.ToString());
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Run the production-shell tests from the repository so src/Moye/App.xaml is available.");
    }
}
