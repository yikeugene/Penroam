using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Moye.Controls;
using Moye.Models;
using Moye.Services;
using Moye.ViewModels;
using L = Moye.Localization.LocalizationService;

namespace Moye.Tests;

[Collection("Page spread shell")]
public sealed class LocalizationTests(PageSpreadTestApplication application)
{
    [Fact]
    public void CompiledEnglishAndTraditionalChineseResourcesHaveMatchingKeysAndArguments()
    {
        var resources = new ResourceManager("Moye.Localization.Strings", typeof(L).Assembly);
        try
        {
            Dictionary<string, string> ReadResources(CultureInfo culture) =>
                Assert.IsAssignableFrom<ResourceSet>(resources.GetResourceSet(culture, true, false))
                    .Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => Assert.IsType<string>(entry.Value));
            var english = ReadResources(CultureInfo.InvariantCulture);
            var chinese = ReadResources(CultureInfo.GetCultureInfo("zh-TW"));
            Assert.NotEmpty(english);
            Assert.Equal(english.Keys.Order(), chinese.Keys.Order());
            foreach (var prefix in new[] { "Core.", "Shell.", "Controls.", "Messages." })
                Assert.Contains(english.Keys, key => key.StartsWith(prefix, StringComparison.Ordinal));
            foreach (var (key, text) in english)
            {
                Assert.False(string.IsNullOrWhiteSpace(text), key);
                Assert.False(string.IsNullOrWhiteSpace(chinese[key]), key);
                Assert.True(ArgumentIndexes(text).SequenceEqual(ArgumentIndexes(chinese[key])), $"Placeholder mismatch: {key}");
            }
            Assert.NotEqual(english["Core.CommandNewNotebook"], chinese["Core.CommandNewNotebook"]);
        }
        finally { resources.ReleaseAllResources(); }
    }

    [Theory]
    [InlineData(null, "en")]
    [InlineData("", "en")]
    [InlineData("en", "en")]
    [InlineData("zh-TW", "zh-TW")]
    [InlineData("zh-CN", "en")]
    [InlineData("unknown", "en")]
    public void OnlyApprovedLanguageCodesAreUsedAndMissingKeysHaveSafeFallbacks(string? language, string expected)
    {
        Assert.Equal(expected, L.NormalizeLanguage(language));
        Assert.Equal(L.GetText("Core.CommandNewNotebook", expected), L.GetText("Core.CommandNewNotebook", language!));
        Assert.Equal("Missing.Localization.TestKey", L.GetText("Missing.Localization.TestKey", language!));
    }

    [Fact]
    public void ExistingBindingsAndLateMessagesSwitchLanguageWithoutChangingFormattingCulture() => application.Run(() =>
    {
        var previousLanguage = L.Current.LanguageCode;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
            L.SetLanguage("en");
            var label = new TextBlock();
            label.SetBinding(TextBlock.TextProperty, L.Binding("Core.Eraser"));
            L.Bind(label, FrameworkElement.ToolTipProperty, "Core.WorkspaceSaveFailed", 1234.5);
            L.Bind(label, AutomationProperties.NameProperty, "Core.Eraser");
            var counts = new ObservableCollection<int> { 1 };
            var count = new TextBlock();
            count.SetBinding(TextBlock.TextProperty, L.CountBinding("Shell.PageCount", "Shell.SinglePageCount", new Binding("Count") { Source = counts }));
            var formatted = new TextBlock();
            formatted.SetBinding(TextBlock.TextProperty, L.FormatBinding("Core.WorkspaceSaveFailed", new Binding("Count") { Source = counts }));
            var immutableLabel = new TextBlock();
            immutableLabel.SetBinding(TextBlock.TextProperty, L.FormatBinding("Core.WorkspaceSaveFailed",
                new Binding("Title") { Source = new { Title = "New notebook" }, Mode = BindingMode.OneTime }));
            object?[] arguments = ["New notebook"];
            var lateMessage = L.M("Core.WorkspaceSaveFailed", arguments);
            arguments[0] = "must not replace the captured argument";

            foreach (var language in new[] { "en", "zh-TW", "en" })
            {
                L.SetLanguage(language); DrainDispatcher();
                Assert.Equal(Expected("Core.Eraser", language), label.Text);
                Assert.Equal(label.Text, AutomationProperties.GetName(label));
                Assert.Equal(Expected("Core.WorkspaceSaveFailed", language, 1234.5), label.ToolTip);
                Assert.Contains("1234,5", Assert.IsType<string>(label.ToolTip));
                Assert.Equal(Expected(counts.Count == 1 ? "Shell.SinglePageCount" : "Shell.PageCount", language, counts.Count), count.Text);
                Assert.Equal(Expected("Core.WorkspaceSaveFailed", language, counts.Count), formatted.Text);
                Assert.Equal(Expected("Core.WorkspaceSaveFailed", language, "New notebook"), lateMessage.ToString());
                Assert.Equal(lateMessage.ToString(), immutableLabel.Text);
                Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
                Assert.Equal("ja-JP", CultureInfo.CurrentUICulture.Name);
                counts.Add(2); DrainDispatcher();
                Assert.Equal(Expected("Shell.PageCount", language, counts.Count), count.Text);
                Assert.Equal(Expected("Core.WorkspaceSaveFailed", language, counts.Count), formatted.Text);
            }
        }
        finally
        {
            L.SetLanguage(previousLanguage);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    });

    [Fact]
    public Task HomeSelectionUpdatesExistingShellAndPersistsWithoutEditingTheNotebook() => application.RunAsync(async () =>
    {
        var previousLanguage = L.Current.LanguageCode;
        using var directory = new StorageTestDirectory();
        try
        {
            L.SetLanguage("en");
            using (var fixture = new LocalizedShell(directory.Root))
            {
                await fixture.InitializeAsync();
                var model = fixture.Model;
                model.AddPage(); model.Undo();
                await model.Autosave.FlushAsync();
                await model.ReturnToLibraryAsync();
                model.Search = "New notebook"; model.Zoom = 1.25;
                // FlushAsync posts StateChanged; finish setup notifications before seeding the warning under test.
                DrainDispatcher();
                model.SetStatus("Core.WorkspaceSaveFailed", "synthetic disk full");
                model.SetOperation("Core.Loading");
                fixture.Layout();
                var document = model.Document!;
                var serialized = JsonSerializer.SerializeToUtf8Bytes(document, DocumentJson.Options);
                var modified = document.ModifiedUtc;
                var pages = model.Pages.ToArray(); var sections = model.Sections.ToArray();
                var selected = model.SelectedPage; var selectedSection = model.SelectedSection;
                var notebook = Assert.Single(model.Notebooks);
                var saveCount = fixture.Repository.SaveCount;
                var changes = 0;
                model.DocumentReplaced += (_, _) => changes++;
                model.HistoryChanged += (_, _) => changes++;
                model.Pages.CollectionChanged += (_, _) => changes++;
                model.Notebooks.CollectionChanged += (_, _) => changes++;
                var notifications = new List<string?>();
                model.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
                Assert.True(model.CanRedo); Assert.False(model.CanUndo);
                Assert.Equal("Tag", fixture.Language.SelectedValuePath);
                Assert.Equal(new[] { "en", "zh-TW" }, fixture.Language.Items.Cast<ComboBoxItem>().Select(item => (string)item.Tag));

                foreach (var language in new[] { "zh-TW", "en", "zh-TW" })
                {
                    Assert.Equal(Expected("Core.WorkspaceSaveFailed", L.Current.LanguageCode, "synthetic disk full"), model.Status);
                    notifications.Clear();
                    await fixture.SelectLanguageAsync(language);
                    fixture.AssertShellText(language);
                    Assert.Same(model, fixture.Window.ViewModel); Assert.Same(document, model.Document);
                    Assert.Equal(serialized, JsonSerializer.SerializeToUtf8Bytes(model.Document, DocumentJson.Options));
                    Assert.Equal(modified, document.ModifiedUtc);
                    Assert.Equal(pages, model.Pages); Assert.Equal(sections, model.Sections);
                    Assert.Same(selected, model.SelectedPage); Assert.Same(selectedSection, model.SelectedSection);
                    Assert.Same(notebook, Assert.Single(model.Notebooks));
                    Assert.Equal("New notebook", model.Title); Assert.Equal("New notebook", model.Search);
                    Assert.Equal(1.25, model.Zoom); Assert.True(model.IsLibraryVisible);
                    Assert.True(model.CanRedo); Assert.False(model.CanUndo); Assert.False(model.Autosave.IsDirty);
                    Assert.Equal(0, changes); Assert.Equal(saveCount, fixture.Repository.SaveCount);
                    Assert.Equal(Expected("Core.WorkspaceSaveFailed", language, "synthetic disk full"), model.Status);
                    Assert.Equal(Expected("Core.Loading", language), model.Operation);
                    Assert.Contains(nameof(MainViewModel.Status), notifications);
                    Assert.Contains(nameof(MainViewModel.Operation), notifications);
                    Assert.Contains(nameof(MainViewModel.LibraryCountText), notifications);
                    Assert.Equal(language, (await new WorkspacePreferencesStore(fixture.WorkspacePath).LoadAsync()).UiLanguage);
                }
            }
            L.SetLanguage("en");
            using var reopened = new LocalizedShell(directory.Root);
            await reopened.InitializeAsync();
            Assert.Equal("zh-TW", L.Current.LanguageCode);
            Assert.Equal("zh-TW", reopened.Language.SelectedValue);
            reopened.AssertShellText("zh-TW");
            Assert.Equal("New notebook", reopened.Model.Title);
        }
        finally { L.SetLanguage(previousLanguage); }
    });

    [Theory]
    [InlineData("{\"version\":1,\"uiLanguage\":\"unknown\"}", false)]
    [InlineData("{\"version\":42,\"uiLanguage\":\"zh-TW\",\"keep\":\"future settings\"}", true)]
    [InlineData("not json", true)]
    public Task HomeSelectionRespectsThePreloadedStoresUnreadableFileGuard(string content, bool blocked) => application.RunAsync(async () =>
    {
        var previousLanguage = L.Current.LanguageCode;
        using var directory = new StorageTestDirectory();
        try
        {
            L.SetLanguage("en");
            var path = Path.Combine(directory.Root, "workspace-preferences.json");
            await File.WriteAllTextAsync(path, content);
            var store = new WorkspacePreferencesStore(path);
            var preferences = await store.LoadAsync();
            using var fixture = new LocalizedShell(directory.Root, store, preferences);
            await fixture.InitializeAsync();
            Assert.Same(store, Get<WorkspacePreferencesStore>(fixture.Window, "_workspaceStore"));
            Assert.Equal("en", fixture.Language.SelectedValue);
            Assert.Equal(content, await File.ReadAllTextAsync(path));

            await fixture.SelectLanguageAsync("zh-TW");

            Assert.Equal("zh-TW", L.Current.LanguageCode);
            fixture.AssertShellText("zh-TW");
            if (blocked)
            {
                Assert.NotNull(store.Warning);
                Assert.Equal(content, await File.ReadAllTextAsync(path));
                Assert.Equal(Expected("Core.WorkspaceSaveFailed", "zh-TW", store.Warning), fixture.Model.Status);
                await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(preferences));
            }
            else
            {
                Assert.Null(store.Warning);
                Assert.Equal("zh-TW", (await new WorkspacePreferencesStore(path).LoadAsync()).UiLanguage);
            }
        }
        finally { L.SetLanguage(previousLanguage); }
    });

    private static string Expected(string key, string language, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, L.GetText(key, language), args);

    private static int[] ArgumentIndexes(string template)
    {
        var format = CompositeFormat.Parse(template);
        var recorder = new ArgumentRecorder();
        _ = string.Format(recorder, format, Enumerable.Range(0, format.MinimumArgumentCount).Cast<object>().ToArray());
        return recorder.Indexes.Order().ToArray();
    }

    private sealed class ArgumentRecorder : IFormatProvider, ICustomFormatter
    {
        public HashSet<int> Indexes { get; } = [];
        public object? GetFormat(Type? type) => type == typeof(ICustomFormatter) ? this : null;
        public string Format(string? format, object? arg, IFormatProvider? provider) { Indexes.Add((int)arg!); return ""; }
    }

    // The same hidden presentation-source pattern as PageSpreadInteractionTests;
    // the collection owns the only Application and never calls real App startup.
    private sealed class LocalizedShell : IDisposable
    {
        private readonly FrameworkElement _content;
        private readonly HwndSource _source;
        public MainWindow Window { get; }
        public MainViewModel Model => Window.ViewModel;
        public LanguageRepository Repository { get; } = new();
        public ComboBox Language => (ComboBox)Window.FindName("HomeLanguageSelector");
        public string WorkspacePath { get; }

        public LocalizedShell(string root, WorkspacePreferencesStore? store = null, WorkspacePreferences? preferences = null)
        {
            WorkspacePath = Path.Combine(root, "workspace-preferences.json");
            Window = new(Repository, new WritingPreferencesStore(Path.Combine(root, "writing.json")),
                new ErrorReporter(Path.Combine(root, "errors.log")), workspaceStore: store, workspace: preferences);
            _content = (FrameworkElement)Window.Content;
            Window.Content = null; _content.DataContext = Model;
            _content.Resources.MergedDictionaries.Add(Window.Resources);
            TextElement.SetFontFamily(_content, Window.FontFamily); TextElement.SetFontSize(_content, Window.FontSize);
            TextElement.SetForeground(_content, Window.Foreground);
            _source = new(new HwndSourceParameters("Penroam localization test")
            { Width = 1400, Height = 960, WindowStyle = unchecked((int)0x80000000) });
            _source.RootVisual = _content;
        }

        public async Task InitializeAsync()
        {
            await Model.InitializeAsync();
            await Model.OpenAsync(Repository.Document.Id);
            await Model.ReturnToLibraryAsync();
            await (Task)Invoke(Window, "LoadWorkspaceAsync")!;
            Set(Window, "_ready", true); Invoke(Window, "UpdateTool"); Layout();
        }

        public async Task SelectLanguageAsync(string language)
        {
            Language.SelectedValue = language; // Exercise the real SelectionChanged handler, not a private method.
            await Get<Task>(Window, "_languageSaveTask"); Layout();
            Assert.Equal(language, Language.SelectedValue);
        }

        public void AssertShellText(string language)
        {
            var button = (Button)Window.FindName("HomeNewNotebookButton");
            var label = ((StackPanel)button.Content).Children.OfType<TextBlock>().Last();
            Assert.Equal(Expected("Shell.NewNotebookShort", language), label.Text);
            Assert.Equal(Expected("Shell.NewNotebook", language), AutomationProperties.GetName(button));
            Assert.Equal(Expected("Shell.HomeSearchHint", language), ((TextBox)Window.FindName("HomeSearch")).ToolTip);
            Assert.Equal(Expected("Core.Eraser", language), ((TextBlock)Window.FindName("EraserModeLabel")).Text);
            var count = Descendants<TextBlock>((ItemsControl)Window.FindName("HomeNotebookCards"))
                .Single(block => BindingOperations.GetMultiBinding(block, TextBlock.TextProperty) is not null);
            Assert.Equal(Expected("Shell.PageCount", language, 2), count.Text);
            var libraryCount = Descendants<TextBlock>((FrameworkElement)Window.FindName("LibraryHome"))
                .Single(block => BindingOperations.GetBinding(block, TextBlock.TextProperty)?.Path?.Path == nameof(MainViewModel.LibraryCountText));
            Assert.Equal(Expected(Model.HasSearch ? "Messages.Count.FilteredNotebooks" : "Messages.Count.Notebook", language, 1, 1), libraryCount.Text);
        }

        public void Layout()
        {
            for (var pass = 0; pass < 3; pass++)
            {
                _content.Measure(new Size(1400, 960)); _content.Arrange(new Rect(0, 0, 1400, 960));
                _content.UpdateLayout(); DrainDispatcher();
            }
        }

        public void Dispose()
        {
            Set(Window, "_closing", true);
            foreach (var field in typeof(MainWindow).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                         .Where(field => field.FieldType == typeof(DispatcherTimer))) ((DispatcherTimer?)field.GetValue(Window))?.Stop();
            Invoke(Window, "ClearTouches", false);
            foreach (var host in Get<Dictionary<Border, PageEditor>>(Window, "_editors").Keys.ToArray()) Invoke(Window, "DetachPage", host);
            SystemEvents.PowerModeChanged -= (PowerModeChangedEventHandler)Delegate.CreateDelegate(typeof(PowerModeChangedEventHandler), Window,
                typeof(MainWindow).GetMethod("PowerModeChanged", BindingFlags.Instance | BindingFlags.NonPublic)!);
            _source.Dispose(); Model.Dispose(); Window.Close();
        }
    }

    private sealed class LanguageRepository : INotebookRepository
    {
        public NotebookDocument Document { get; private set; } = new()
        {
            Title = "New notebook", Folder = "My Notes",
            Pages = [new() { Title = "New notebook", Texts = [new() { Text = "New notebook\n繁體中文筆記", X = 24.5, Y = 61.25 }] }, new()]
        };
        public int SaveCount { get; private set; }
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<NotebookSummary>> ListAsync() => Task.FromResult<IReadOnlyList<NotebookSummary>>(
            [new() { Id = Document.Id, Title = Document.Title, Folder = Document.Folder, PageCount = Document.Pages.Count, ModifiedUtc = Document.ModifiedUtc }]);
        public Task<NotebookDocument?> LoadAsync(string id) => Task.FromResult<NotebookDocument?>(Document.Snapshot());
        public Task SaveAsync(NotebookDocument document) { Document = document.Snapshot(); SaveCount++; return Task.CompletedTask; }
        public Task DeleteAsync(string id) => Task.CompletedTask;
        public Task<AssetData> PutAssetAsync(string name, string contentType, byte[] bytes) => throw new InvalidOperationException("No fixture assets");
        public Task<AssetData> GetAssetAsync(string id) => throw new InvalidOperationException("No fixture assets");
        public void Dispose() { }
    }

    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
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
