using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Moye.Controls;
using Moye.Models;
using Moye.Services;
using Moye.ViewModels;
using L = Moye.Localization.LocalizationService;

namespace Moye.UiPreview;

internal static partial class Program
{
    private const BindingFlags BilingualPrivate = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly (int Width, int Height)[] BilingualSizes = [(1024, 700), (1400, 960)];

    private static void RunBilingualPreview(string output)
    {
        var reports = new List<BilingualSceneReport>();
        var switches = new List<BilingualLanguageSwitch>();
        var settingsDirectory = Path.Combine(output, "bilingual-preview-settings", Guid.NewGuid().ToString("N"));
        var previousContext = SynchronizationContext.Current;
        MainWindow? window = null;
        using var repository = new FixtureRepository();
        var fixtureBefore = JsonSerializer.Serialize(repository.Document, DocumentJson.Options);
        // File-backed preference completions must not wait for a Dispatcher pump.
        // No Loaded event, native window, updater, or safety/backup timer is started.
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            L.SetLanguage("en");
            var store = new WorkspacePreferencesStore(Path.Combine(settingsDirectory, "workspace-preferences.json"));
            var workspace = store.LoadAsync().GetAwaiter().GetResult();
            window = new MainWindow(repository,
                new WritingPreferencesStore(Path.Combine(settingsDirectory, "writing-preferences.json")),
                new ErrorReporter(Path.Combine(settingsDirectory, "error.log")), workspaceStore: store, workspace: workspace);
            window.ViewModel.InitializeAsync().GetAwaiter().GetResult();
            if (!window.ViewModel.IsLibraryVisible || window.ViewModel.Document is not null)
                throw new InvalidOperationException("The bilingual fixture must start at home without opening a notebook.");
            // The preloaded store has completed its real read. Only lift the
            // readiness gate normally set by Loaded; selection still invokes the
            // production HomeLanguageChanged handler and SaveWorkspaceAsync.
            typeof(MainWindow).GetField("_workspaceLoaded", BilingualPrivate)!.SetValue(window, true);
            var content = DetachBilingualContent(window);
            var selector = (ComboBox)window.FindName("HomeLanguageSelector");
            if (selector.SelectedValuePath != "Tag" ||
                !selector.Items.OfType<ComboBoxItem>().Select(item => item.Tag as string).SequenceEqual(new[] { "en", "zh-TW" }))
                throw new InvalidOperationException("The home language selector must use stable en/zh-TW tags.");
            var recovery = (Button)window.FindName("RecoveryAlertButton");
            recovery.Visibility = Visibility.Visible;
            L.Bind(recovery, ContentControl.ContentProperty, "Shell.RecoverDraftCount", 2);

            foreach (var state in new[] { "library", "library-empty", "library-search", "library-empty-search" })
            {
                repository.IncludeDocument = !state.StartsWith("library-empty", StringComparison.Ordinal);
                window.ViewModel.Search = state.EndsWith("-search", StringComparison.Ordinal) ? "no matching notebook — 原文" : "";
                window.ViewModel.RefreshLibraryAsync().GetAwaiter().GetResult();
                foreach (var language in new[] { "zh-TW", "en" })
                {
                    SwitchBilingualLanguage(window, store, language, state, switches);
                    foreach (var (width, height) in BilingualSizes)
                    {
                        Arrange(content, width, height);
                        VerifyLibraryState(window, content, state);
                        RequireBilingualText(AutomationProperties.GetName((Button)window.FindName("HomeImportFolderButton")), "Shell.ImportFolder");
                        RequireBilingualText(AutomationProperties.GetName((TextBox)window.FindName("HomeSearch")), "Shell.SearchNotebooks");
                        RequireBilingualText(recovery.Content as string, "Shell.RecoverDraftCount", 2);
                        if (state == "library") VerifyNotebookCards(content, repository.LibraryCount);
                        string[] required = state switch
                        {
                            "library" => ["HomeNewNotebookButton", "HomeImportFolderButton", "NotebookOptionsButton", "RecoveryAlertButton"],
                            "library-search" or "library-empty-search" => ["HomeNewNotebookButton", "HomeImportFolderButton", "ClearHomeSearchButton", "EmptyLibraryClearSearchButton", "RecoveryAlertButton"],
                            _ => ["HomeNewNotebookButton", "HomeImportFolderButton", "EmptyLibraryCreateButton", "RecoveryAlertButton"]
                        };
                        CaptureBilingualScene(output, window, content, language, state + "-recovery", width, height,
                            reports, required, selector);
                    }
                }
            }

            repository.IncludeDocument = true;
            window.ViewModel.Search = "";
            window.ViewModel.RefreshLibraryAsync().GetAwaiter().GetResult();
            window.ViewModel.OpenAsync(repository.Document.Id).GetAwaiter().GetResult();
            if (!window.ViewModel.IsEditorVisible) throw new InvalidOperationException("The bilingual fixture did not open its notebook.");
            typeof(MainWindow).GetMethod("SinglePageClick", BilingualPrivate)!.Invoke(window, [window, new RoutedEventArgs()]);
            foreach (var page in window.ViewModel.Pages)
            {
                var editor = CreateEditor(page, repository);
                editor.LayoutTransform = Transform.Identity;
                page.Thumbnail = RenderElement(editor, page.Page.Width, page.Page.Height, .16);
            }
            foreach (var language in new[] { "zh-TW", "en" })
            {
                SwitchBilingualLanguage(window, store, language, "editor", switches);
                foreach (var (width, height) in BilingualSizes)
                {
                    Arrange(content, width, height);
                    var viewport = (FrameworkElement)window.FindName("Viewport");
                    var pageList = (ListBox)window.FindName("PageList");
                    var selected = window.ViewModel.SelectedPage!;
                    var spread = window.PageSpreads.Single(row => row.First == selected || row.Second == selected);
                    window.ViewModel.Zoom = PageSpreadGeometry.FitPageZoom(spread, viewport.ActualWidth - 90, viewport.ActualHeight - 106);
                    Arrange(content, width, height);
                    // Like the existing editor scenes, populate real page hosts
                    // without dispatching their native Loaded lifecycle.
                    foreach (var host in Descendants<Border>(pageList).Where(border => border.DataContext is PageViewModel &&
                                 double.IsFinite(border.Width) && double.IsFinite(border.Height) && !InsidePageEditor(border)).ToArray())
                        if (host.Child is null) host.Child = CreateEditor((PageViewModel)host.DataContext, repository);
                    foreach (var editor in Descendants<PageEditor>(content))
                        editor.LayoutTransform = new ScaleTransform(window.ViewModel.Zoom, window.ViewModel.Zoom);
                    if (!Descendants<PageEditor>(content).Any()) throw new InvalidOperationException("The bilingual editor has no realized page.");
                    RequireBilingualText(AutomationProperties.GetName((Button)window.FindName("PenButton")), "Shell.Pen");
                    RequireBilingualText(AutomationProperties.GetName((Button)window.FindName("TypeButton")), "Shell.Type");
                    RequireBilingualText(((Button)window.FindName("FitWidthButton")).Content as string, "Shell.FitWidth");
                    CaptureBilingualScene(output, window, content, language, "editor", width, height, reports,
                        ["PenButton", "HighlighterButton", "EraserButton", "LassoButton", "TypeButton", "SelectButton", "PenSettingsButton",
                         "UndoButton", "RedoButton", "FitPageButton", "FitWidthButton", "FocusButton", "ZoomInButton", "ZoomOutButton", "AddSectionButton", "SectionOptionsButton"]);
                }
            }

            CaptureBilingualNotebooks(output, window, store, reports, switches);
            CaptureBilingualUpdates(output, window, store, reports, switches);
            if (JsonSerializer.Serialize(repository.Document, DocumentJson.Options) != fixtureBefore)
                throw new InvalidOperationException("Switching the interface language changed the synthetic notebook data.");
            if (reports.Any(report => report.Layout.Under44Dip.Count > 0 || report.Layout.Overlaps.Count > 0 || report.Layout.Clipped.Count > 0))
                throw new InvalidOperationException("Bilingual layout validation failed. See artifacts/ui-preview-bilingual-report.json.");
        }
        finally
        {
            // Existing scene runners deliberately retain their English assertions.
            L.SetLanguage("en");
            if (window is not null)
            {
                var powerHandler = (PowerModeChangedEventHandler)typeof(MainWindow).GetMethod("PowerModeChanged", BilingualPrivate)!
                    .CreateDelegate(typeof(PowerModeChangedEventHandler), window);
                SystemEvents.PowerModeChanged -= powerHandler;
                foreach (var field in typeof(MainWindow).GetFields(BilingualPrivate))
                    if (field.GetValue(window) is DispatcherTimer timer) timer.Stop();
                typeof(MainWindow).GetField("_closing", BilingualPrivate)!.SetValue(window, true);
                window.ViewModel.Dispose();
                window.Close();
            }
            SynchronizationContext.SetSynchronizationContext(previousContext);
            File.WriteAllText(Path.Combine(output, "ui-preview-bilingual-report.json"), JsonSerializer.Serialize(new
            {
                Scope = "Detached layout and real selection-handler persistence; no native focus, popups, clipboard, IME, pen, touch, or desktop DPI verification.",
                SettingsDirectory = Path.GetRelativePath(output, settingsDirectory),
                LanguageSwitches = switches,
                Scenes = reports
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static void SwitchBilingualLanguage(MainWindow window, WorkspacePreferencesStore store, string language,
        string scene, List<BilingualLanguageSwitch> switches)
    {
        var selector = (ComboBox)window.FindName("HomeLanguageSelector");
        var previous = L.Current.LanguageCode;
        var button = (Button)window.FindName("HomeNewNotebookButton");
        var previousLabel = AutomationProperties.GetName(button);
        var search = window.ViewModel.Search;
        var document = JsonSerializer.Serialize(window.ViewModel.Document, DocumentJson.Options);
        if (previous == language) throw new InvalidOperationException("A bilingual switch must change the existing window's language.");
        selector.SelectedItem = selector.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, language));
        BilingualField<Task>(window, "_languageSaveTask").GetAwaiter().GetResult();
        var saved = new WorkspacePreferencesStore(store.FilePath).LoadAsync().GetAwaiter().GetResult();
        if (L.Current.LanguageCode != language || !Equals(selector.SelectedValue, language) || saved.UiLanguage != language ||
            BilingualField<WorkspacePreferences>(window, "_workspace").UiLanguage != language || !File.Exists(store.FilePath))
            throw new InvalidOperationException($"The actual home language handler did not select and persist {language}.");
        RequireBilingualText(AutomationProperties.GetName(button), "Shell.NewNotebook");
        if (AutomationProperties.GetName(button) == previousLabel)
            throw new InvalidOperationException("An existing home action did not update when the interface language changed.");
        if (window.ViewModel.Search != search || JsonSerializer.Serialize(window.ViewModel.Document, DocumentJson.Options) != document)
            throw new InvalidOperationException("Changing the interface language modified notebook content or the search query.");
        switches.Add(new(scene, previous, language, saved.UiLanguage));
    }

    private static void CaptureBilingualNotebooks(string output, MainWindow window, WorkspacePreferencesStore store,
        List<BilingualSceneReport> reports, List<BilingualLanguageSwitch> switches)
    {
        const string title = "Synthetic notebook — 原稿 unchanged";
        const string folder = "Preview / 課程";
        foreach (var language in new[] { "zh-TW", "en" })
        {
            SwitchBilingualLanguage(window, store, language, "notebook-dialog", switches);
            var dialog = new NotebookDialog(null!, title, folder, PaperTemplate.Ruled);
            try
            {
                var content = DetachBilingualContent(dialog);
                Arrange(content, 544, 652);
                var titleInput = BilingualNamed<TextBox>(content, "NotebookTitleInput");
                var folderInput = BilingualNamed<TextBox>(content, "NotebookFolderInput");
                var create = BilingualNamed<Button>(content, "NotebookCreateButton");
                var cancel = BilingualNamed<Button>(content, "NotebookCancelButton");
                RequireBilingualText(dialog.Title, "Controls.Notebook.Title");
                RequireBilingualText(create.Content as string, "Controls.Notebook.Create");
                RequireBilingualText(cancel.Content as string, "Controls.Common.Cancel");
                RequireBilingualText(AutomationProperties.GetName(titleInput), "Controls.Notebook.Name");
                RequireBilingualText(AutomationProperties.GetName(folderInput), "Controls.Common.Category");
                if (!create.IsDefault || !cancel.IsCancel || titleInput.Text != title || folderInput.Text != folder ||
                    dialog.NotebookTitle != title || dialog.Folder != folder || dialog.SelectedTemplate != PaperTemplate.Ruled)
                    throw new InvalidOperationException("The localized notebook dialog changed data or lost its default/cancel actions.");
                foreach (var (width, height) in BilingualSizes)
                {
                    var surface = new Grid { Background = (Brush)Application.Current.Resources["Canvas"] };
                    content.Width = 544;
                    content.MaxHeight = height - 48;
                    content.HorizontalAlignment = HorizontalAlignment.Center;
                    content.VerticalAlignment = VerticalAlignment.Center;
                    surface.Children.Add(content);
                    var scroll = (ScrollViewer)content;
                    scroll.ScrollToTop();
                    Arrange(surface, width, height);
                    var choices = Descendants<RadioButton>(content).ToArray();
                    if (choices.Length != 7 || choices.Count(choice => choice.IsChecked == true) != 1)
                        throw new InvalidOperationException("The notebook dialog must retain all seven paper choices and one selection.");
                    var revealed = choices.Where(choice => InsideScrollViewports(choice, surface)).ToHashSet();
                    CaptureBilingualScene(output, dialog, surface, language, "notebook-dialog", width, height, reports, [], titleInput, folderInput);
                    scroll.ScrollToBottom();
                    Arrange(surface, width, height);
                    revealed.UnionWith(choices.Where(choice => InsideScrollViewports(choice, surface)));
                    CaptureBilingualScene(output, dialog, surface, language, "notebook-dialog-bottom", width, height, reports,
                        ["NotebookCreateButton", "NotebookCancelButton"]);
                    if (revealed.Count != choices.Length)
                        throw new InvalidOperationException("A paper choice is unreachable in both notebook-dialog scroll scenes.");
                    surface.Children.Remove(content);
                }
            }
            finally { dialog.Close(); }
        }
    }

    private static void CaptureBilingualUpdates(string output, MainWindow window, WorkspacePreferencesStore store,
        List<BilingualSceneReport> reports, List<BilingualLanguageSwitch> switches)
    {
        const string version = "9.9.9";
        const string detail = "Synthetic offline detail — 原文 stays unchanged.";
        var notes = "Synthetic release notes — 原文 unchanged.\n\n" +
            string.Join("\n", Enumerable.Repeat("A synthetic note about writing, pages, and export. This is release data, not interface text.", 16));
        foreach (var state in new[] { "available", "downloading", "ready", "error" })
        {
            // Construct and show once in English, then switch the same live-bound
            // card in both directions; do not recreate it to hide stale bindings.
            var card = new UpdateNotification();
            var surface = new Grid { Background = (Brush)Application.Current.Resources["Canvas"] };
            surface.Children.Add(card);
            switch (state)
            {
                case "available": card.ShowAvailable(version, notes); break;
                case "downloading": card.ShowDownloading(.37); break;
                case "ready": card.ShowReady(version); break;
                case "error": card.ShowError(L.M("Core.UpdateStartFailed", detail)); break;
            }
            foreach (var language in new[] { "zh-TW", "en" })
            {
                SwitchBilingualLanguage(window, store, language, "update-" + state, switches);
                foreach (var (width, height) in BilingualSizes)
                {
                    Arrange(surface, width, height);
                    var heading = BilingualNamed<TextBlock>(card, "UpdateHeading");
                    var message = BilingualNamed<TextBlock>(card, "UpdateMessage");
                    var primary = BilingualNamed<Button>(card, "UpdatePrimaryButton");
                    var secondary = BilingualNamed<Button>(card, "UpdateSecondaryButton");
                    RequireBilingualText(heading.Text, state switch
                    {
                        "available" => "Controls.Update.AvailableHeading", "downloading" => "Controls.Update.DownloadingHeading",
                        "ready" => "Controls.Update.ReadyHeading", _ => "Controls.Update.ErrorHeading"
                    }, state is "available" or "ready" ? [version] : []);
                    if (state == "error") RequireBilingualText(message.Text, "Core.UpdateStartFailed", detail);
                    else RequireBilingualText(message.Text, state switch
                    {
                        "available" => "Controls.Update.AvailableMessage", "downloading" => "Controls.Update.DownloadingMessage",
                        _ => "Controls.Update.ReadyMessage"
                    });
                    RequireBilingualText(secondary.Content as string, state == "downloading" ? "Controls.Update.CancelDownload" : "Controls.Update.Later");
                    if (state != "downloading") RequireBilingualText(primary.Content as string, state switch
                    {
                        "available" => "Controls.Update.UpdateNow", "ready" => "Controls.Update.Install", _ => "Controls.Update.Retry"
                    });
                    if (state == "available")
                    {
                        var scroll = Descendants<ScrollViewer>(card).Single();
                        if (scroll.ScrollableHeight <= 0 || ((TextBlock)scroll.Content).Text != notes)
                            throw new InvalidOperationException("The localized update card changed its release data or lost release-note scrolling.");
                    }
                    var bounds = card.TransformToAncestor(surface).TransformBounds(new Rect(card.RenderSize));
                    if (bounds.Left < 0 || bounds.Top < 0 || bounds.Right > width || bounds.Bottom > height || bounds.Height > 470)
                        throw new InvalidOperationException("The localized update notification does not fit the workspace.");
                    foreach (var button in new[] { primary, secondary }.Where(button => button.Visibility == Visibility.Visible))
                    {
                        var actionBounds = button.TransformToAncestor(surface).TransformBounds(new Rect(button.RenderSize));
                        if (!bounds.Contains(actionBounds) || !button.Focusable || !button.IsTabStop || string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)))
                            throw new InvalidOperationException("The update card has an inaccessible or out-of-card action.");
                    }
                    CaptureBilingualScene(output, window, surface, language, "update-" + state, width, height, reports,
                        state == "downloading" ? ["UpdateSecondaryButton"] : ["UpdatePrimaryButton", "UpdateSecondaryButton"]);
                }
            }
        }
    }

    private static void CaptureBilingualScene(string output, Window window, FrameworkElement content, string language,
        string scene, int width, int height, List<BilingualSceneReport> reports, string[] required, params Control[] additionalControls)
    {
        var image = $"ui-preview-bilingual-{language}-{scene}-{width}.png";
        SaveImage(output, image, RenderElement(content, width, height));
        VerifyDetached(content, window);
        if (!string.Equals(content.Language.IetfLanguageTag, "en-US", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("An interface translation must not change the document's en-US layout language.");
        foreach (var id in required)
        {
            var matches = Descendants<Button>(content).Where(button => button.Name == id || AutomationProperties.GetAutomationId(button) == id);
            if (!matches.Any(button => HasVisibleAncestors(button) && button.ActualWidth > 0 && button.ActualHeight > 0 && InsideScrollViewports(button, content)))
                throw new InvalidOperationException($"The bilingual {scene} scene is missing the visible control {id}.");
        }
        // Retain the existing unmodified 44-DIP, overlap and clipping checks.
        var layout = MeasureButtons(content, width, height, image, scene);
        var controls = new List<BilingualControlBounds>();
        foreach (var control in additionalControls)
        {
            var id = string.IsNullOrEmpty(control.Name) ? AutomationProperties.GetAutomationId(control) : control.Name;
            if (string.IsNullOrWhiteSpace(id) || !HasVisibleAncestors(control) || control.ActualWidth <= 0 || control.ActualHeight <= 0 ||
                !InsideScrollViewports(control, content) || !control.Focusable || !control.IsTabStop || string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)))
                throw new InvalidOperationException($"The bilingual {scene} control {id} is missing, hidden, or lacks declared keyboard accessibility.");
            var bounds = control.TransformToAncestor(content).TransformBounds(new Rect(control.RenderSize));
            foreach (var previous in controls)
            {
                var intersection = Rect.Intersect(bounds, new Rect(previous.X, previous.Y, previous.Width, previous.Height));
                if (!intersection.IsEmpty && intersection.Width > .5 && intersection.Height > .5)
                    layout.Overlaps.Add($"{id} / {previous.Id}: {Round(intersection.Width)} × {Round(intersection.Height)} DIP");
            }
            controls.Add(new(id, AutomationProperties.GetName(control), Round(bounds.X), Round(bounds.Y), Round(bounds.Width), Round(bounds.Height)));
            if (bounds.Width < 43.99 || bounds.Height < 43.99) layout.Under44Dip.Add(id);
            if (bounds.Left < -.5 || bounds.Top < -.5 || bounds.Right > width + .5 || bounds.Bottom > height + .5) layout.Clipped.Add(id);
            foreach (var button in layout.Buttons)
            {
                if (button.Name == ControlName(control)) continue; // Shared checks now include ComboBox controls.
                var intersection = Rect.Intersect(bounds, button.Rect);
                if (!intersection.IsEmpty && intersection.Width > .5 && intersection.Height > .5)
                    layout.Overlaps.Add($"{id} / {button.Name}: {Round(intersection.Width)} × {Round(intersection.Height)} DIP");
            }
        }
        reports.Add(new(language, layout, controls));
    }

    private static FrameworkElement DetachBilingualContent(Window window)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        content.DataContext = window.DataContext;
        content.Resources.MergedDictionaries.Add(window.Resources);
        TextElement.SetFontFamily(content, window.FontFamily);
        TextElement.SetFontSize(content, window.FontSize);
        TextElement.SetForeground(content, window.Foreground);
        content.Language = window.Language;
        content.UseLayoutRounding = window.UseLayoutRounding;
        content.SnapsToDevicePixels = window.SnapsToDevicePixels;
        if (content is Control control) control.Background = window.Background;
        VerifyDetached(content, window);
        return content;
    }

    private static T BilingualField<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BilingualPrivate)!.GetValue(instance)!;

    private static T BilingualNamed<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants<T>(root).Single(element => element.Name == name || AutomationProperties.GetAutomationId(element) == name);

    private static void RequireBilingualText(string? actual, string key, params object?[] arguments)
    {
        if (L.GetText(key, "en") == key || L.GetText(key, "zh-TW") == key || L.GetText(key, "en") == L.GetText(key, "zh-TW"))
            throw new InvalidOperationException($"The bilingual probe {key} needs distinct English and Traditional Chinese resources.");
        if (actual != L.Text(key, arguments))
            throw new InvalidOperationException($"The existing {key} control did not display its {L.Current.LanguageCode} resource.");
    }

    private sealed record BilingualLanguageSwitch(string Scene, string From, string To, string PersistedLanguage);
    private sealed record BilingualControlBounds(string Id, string Label, double X, double Y, double Width, double Height);
    private sealed record BilingualSceneReport(string Language, PreviewReport Layout, List<BilingualControlBounds> AdditionalControls);
}
