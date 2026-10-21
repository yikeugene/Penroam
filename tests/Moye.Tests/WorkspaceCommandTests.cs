using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Moye.Controls;
using Moye.Models;

namespace Moye.Tests;

public sealed class WorkspaceCommandTests
{
    [Fact]
    public void ThreeArgumentConstructorRetainsExecutableDefaults()
    {
        var calls = 0;
        var command = new WorkspaceCommand("Save", "Save the current notebook.", () => calls++);
        Assert.Equal("", command.Id);
        Assert.Null(command.ParentId);
        Assert.Equal("Tools", command.Category);
        Assert.Equal("", command.Shortcut);
        Assert.Equal("", command.Keywords);
        Assert.Equal("", command.Location);
        Assert.False(command.IsInstruction);
        Assert.Null(command.UnavailableReason);
        Assert.False(command.IsChecked);
        Assert.True(command.CanExecute);
        Assert.True(command.TryExecute());
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("HIGHLIGHTER")]
    [InlineData("TRANSLUCENT")]
    [InlineData("WRITING")]
    [InlineData("CTRL+SHIFT+H")]
    [InlineData("MARKER")]
    [InlineData("TOOLBAR")]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public void MatchesSearchesEveryDiscoverabilityFieldWithoutExecuting(string query)
    {
        var command = SearchableCommand(() => throw new InvalidOperationException("Search executed a command."));
        Assert.True(command.Matches(query));
    }

    [Fact]
    public void MatchesRequiresEveryWordAcrossFieldsAndDoesNotReadLiveState()
    {
        var command = SearchableCommand(() => throw new InvalidOperationException()) with
        {
            GetChecked = () => throw new InvalidOperationException("Search read check state."),
            GetUnavailableReason = () => throw new InvalidOperationException("Search read availability.")
        };
        Assert.True(command.Matches(" marker\tWRITING\ntranslucent  toolbar "));
        Assert.False(command.Matches("marker missing"));
        Assert.False(command.Matches("unrelated"));
    }

    [Fact]
    public void TryExecuteRechecksAvailabilityAndCheckedStateIsLive()
    {
        var available = true;
        var isChecked = false;
        var calls = 0;
        var command = new WorkspaceCommand("Toggle tool", "Use the tool.", () => calls++)
        {
            GetUnavailableReason = () => available ? null : "Open a notebook first.",
            GetChecked = () => isChecked
        };
        Assert.True(command.CanExecute);
        Assert.False(command.IsChecked);
        available = false;
        isChecked = true;
        Assert.True(command.IsChecked);
        Assert.Equal("Open a notebook first.", command.UnavailableReason);
        Assert.False(command.TryExecute());
        Assert.Equal(0, calls);
        available = true;
        Assert.True(command.TryExecute());
        Assert.Equal(1, calls);
    }

    [Fact]
    public void InstructionsAreNeverExecutable()
    {
        var command = new WorkspaceCommand("Move pages", "Use two fingers on the page.", null);
        Assert.True(command.IsInstruction);
        Assert.False(command.CanExecute);
        Assert.False(command.TryExecute());
        Assert.True(command.Matches("two fingers"));
    }

    [Fact]
    public void EmptyUnavailableReasonStillBlocksExecutionAndErrorsAreNotSwallowed()
    {
        var unavailable = new WorkspaceCommand("Tool", "Details", () => throw new InvalidOperationException())
        { GetUnavailableReason = () => "" };
        Assert.False(unavailable.CanExecute);
        Assert.False(unavailable.TryExecute());
        var failure = new InvalidOperationException("Execution failed.");
        var command = new WorkspaceCommand("Tool", "Details", () => throw failure);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => command.TryExecute()));
        var getterFailure = command with { GetUnavailableReason = () => throw failure };
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => getterFailure.TryExecute()));
    }

    [Fact]
    public void DialogKeepsUnavailableAndInstructionEntriesAndResetsBothFilters()
    {
        Sta(() =>
        {
            var calls = 0;
            var owner = new Window();
            var dialog = WorkspaceCommandDialog.Build(owner,
            [
                SearchableCommand(() => calls++),
                new("Export PDF", "Share the current notebook.", () => calls++)
                { Category = "Documents", GetUnavailableReason = () => "Open a notebook first." },
                new("Finger navigation", "Move pages with two fingers.", null) { Category = "Writing", ParentId = "writing.tools" }
            ]);
            try
            {
                Assert.Equal("All tools & help", dialog.Title);
                Assert.Equal(IntPtr.Zero, new WindowInteropHelper(dialog).Handle);
                var search = Find<TextBox>(dialog, "CommandSearch");
                var category = Find<ComboBox>(dialog, "CommandCategory");
                var list = Find<ListBox>(dialog, "CommandList");
                Assert.Equal("All categories", category.SelectedItem);
                Assert.Equal(3, list.Items.Count);
                Assert.Contains("Open a notebook first.", Find<TextBlock>(dialog, "CommandDetails").Text);
                Assert.False(Find<Button>(dialog, "RunCommandButton").IsEnabled);
                category.SelectedItem = "Writing";
                Assert.Equal(2, list.Items.Count);
                search.Text = "marker toolbar";
                Assert.Single(list.Items.Cast<object>());
                Assert.Contains("Highlighter", Find<TextBlock>(dialog, "CommandDetails").Text);
                search.Text = "does not exist";
                Assert.Empty(list.Items);
                Assert.Equal(Visibility.Visible, Find<TextBlock>(dialog, "CommandEmptyState").Visibility);
                Assert.False(Find<Button>(dialog, "RunCommandButton").IsEnabled);
                Click(Find<Button>(dialog, "ClearCommandSearchButton"));
                Assert.Equal("", search.Text);
                Assert.Equal("All categories", category.SelectedItem);
                Assert.Equal(3, list.Items.Count);
                Assert.Equal(0, calls);
                Assert.Equal(IntPtr.Zero, new WindowInteropHelper(dialog).Handle);
            }
            finally { dialog.Close(); owner.Close(); }
        });
    }

    [Fact]
    public void UnavailableRowsStaySelectableButCannotRunByButtonEnterOrDoubleClick()
    {
        Sta(() =>
        {
            var calls = 0;
            var owner = new Window();
            var dialog = WorkspaceCommandDialog.Build(owner,
            [new("Export PDF", "Share a notebook.", () => calls++) { GetUnavailableReason = () => "Open a notebook first." }]);
            using var source = InputSource();
            try
            {
                var content = Detach(dialog);
                Layout(content, 690, 640);
                var list = Find<ListBox>(dialog, "CommandList");
                var item = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
                Assert.True(item.IsEnabled);
                Assert.True(item.IsSelected);
                Assert.Contains("Open a notebook first.", AutomationProperties.GetName(item));
                Click(Find<Button>(dialog, "RunCommandButton"));
                Key(Find<TextBox>(dialog, "CommandSearch"), source, System.Windows.Input.Key.Enter);
                DoubleClick(list, item);
                PumpDispatcher();
                Assert.Equal(0, calls);
                Assert.False(Find<Button>(dialog, "RunCommandButton").IsEnabled);
            }
            finally { dialog.Close(); owner.Close(); }
        });
    }

    [Fact]
    public void InstructionSelectionShowsUsageWithoutARunAction()
    {
        Sta(() =>
        {
            var owner = new Window();
            var dialog = WorkspaceCommandDialog.Build(owner,
            [new("Finger navigation", "Use two fingers to move pages.", null) { Category = "Writing", Location = "On the page" }]);
            using var source = InputSource();
            try
            {
                var details = Find<TextBlock>(dialog, "CommandDetails").Text;
                Assert.Contains("How to use", details);
                Assert.Contains("Use two fingers to move pages.", details);
                Assert.Contains("Writing > On the page", details);
                Assert.Equal(Visibility.Collapsed, Find<Button>(dialog, "RunCommandButton").Visibility);
                Assert.False(Find<Button>(dialog, "RunCommandButton").IsEnabled);
                var closed = false; dialog.Closed += (_, _) => closed = true;
                Click(Find<Button>(dialog, "RunCommandButton"));
                Key(Find<TextBox>(dialog, "CommandSearch"), source, System.Windows.Input.Key.Enter);
                PumpDispatcher();
                Assert.False(closed);
            }
            finally { dialog.Close(); owner.Close(); }
        });
    }

    [Fact]
    public void ExistingCategoryInLocationIsNotRepeated()
    {
        Sta(() =>
        {
            var owner = new Window();
            var dialog = WorkspaceCommandDialog.Build(owner,
                [new("Pen opacity", "Change pen transparency.", null) { Category = "Tools", Location = "Tools → Manage pens → Pen opacity" }]);
            try
            {
                var details = Find<TextBlock>(dialog, "CommandDetails").Text;
                Assert.Contains("Tools → Manage pens → Pen opacity", details);
                Assert.DoesNotContain("Tools > Tools", details);
            }
            finally { dialog.Close(); owner.Close(); }
        });
    }

    [Fact]
    public void FilterRefreshesAvailabilityAndCheckStateWithoutRunningAnything()
    {
        Sta(() =>
        {
            var available = false;
            var isChecked = false;
            var calls = 0;
            var owner = new Window();
            var dialog = WorkspaceCommandDialog.Build(owner,
            [new("Two-page view", "Place two pages side by side.", () => calls++)
            {
                Shortcut = "Ctrl+2", GetChecked = () => isChecked,
                GetUnavailableReason = () => available ? null : "Open a notebook first."
            }]);
            try
            {
                var run = Find<Button>(dialog, "RunCommandButton");
                Assert.False(run.IsEnabled);
                Assert.Contains("Off", Find<TextBlock>(dialog, "CommandDetails").Text);
                available = true; isChecked = true;
                Find<TextBox>(dialog, "CommandSearch").Text = "two-page";
                Assert.True(run.IsEnabled);
                var details = Find<TextBlock>(dialog, "CommandDetails").Text;
                Assert.Contains("Ctrl+2", details);
                Assert.Contains("On", details);
                Assert.DoesNotContain("Open a notebook first.", details);
                var closed = false; dialog.Closed += (_, _) => closed = true;
                available = false;
                Click(run);
                Assert.False(closed);
                Assert.False(run.IsEnabled);
                Assert.Equal(0, calls);
            }
            finally { dialog.Close(); owner.Close(); }
        });
    }

    [Theory]
    [InlineData("button")]
    [InlineData("enter")]
    [InlineData("double-click")]
    public void RepeatedActivationQueuesOnlyOnceAndExecutesAfterClose(string activation)
    {
        Sta(() =>
        {
            var calls = 0;
            var closed = false;
            var owner = new Window();
            var dialog = WorkspaceCommandDialog.Build(owner,
            [new("Open tool", "Open another dialog.", () => { Assert.True(closed); calls++; })]);
            using var source = InputSource();
            try
            {
                dialog.Closed += (_, _) => closed = true;
                Layout(Detach(dialog), 690, 640);
                var run = Find<Button>(dialog, "RunCommandButton");
                var search = Find<TextBox>(dialog, "CommandSearch");
                var list = Find<ListBox>(dialog, "CommandList");
                if (activation == "button") Click(run);
                else if (activation == "enter") Key(search, source, System.Windows.Input.Key.Enter);
                else DoubleClick(list, Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0)));
                Click(run);
                Key(search, source, System.Windows.Input.Key.Enter);
                Assert.True(closed);
                Assert.Equal(0, calls);
                PumpDispatcher();
                Assert.Equal(1, calls);
            }
            finally { dialog.Close(); owner.Close(); }
        });
    }

    [Fact]
    public void QueuedCommandRechecksAvailabilityAfterDialogCloses()
    {
        Sta(() =>
        {
            var available = true;
            var calls = 0;
            var owner = new Window();
            var dialog = WorkspaceCommandDialog.Build(owner,
            [new("Tool", "Details", () => calls++) { GetUnavailableReason = () => available ? null : "Selection changed." }]);
            try
            {
                Click(Find<Button>(dialog, "RunCommandButton"));
                available = false;
                PumpDispatcher();
                Assert.Equal(0, calls);
            }
            finally { dialog.Close(); owner.Close(); }
        });
    }

    [Fact]
    public void CancelAndCategoryEnterDoNotDispatchTheSelectedCommand()
    {
        Sta(() =>
        {
            var calls = 0;
            var owner = new Window();
            var dialog = WorkspaceCommandDialog.Build(owner, [new("Tool", "Details", () => calls++)]);
            using var source = InputSource();
            try
            {
                var closed = false; dialog.Closed += (_, _) => closed = true;
                Key(Find<ComboBox>(dialog, "CommandCategory"), source, System.Windows.Input.Key.Enter);
                Assert.False(closed);
                Key(dialog, source, System.Windows.Input.Key.Escape);
                Assert.True(closed);
                Click(Find<Button>(dialog, "RunCommandButton"));
                PumpDispatcher();
                Assert.Equal(0, calls);
            }
            finally { dialog.Close(); owner.Close(); }
        });
    }

    [Fact]
    public void CompositionCandidateEnterDoesNotRunUntilCompositionCompletes()
    {
        Sta(() =>
        {
            var calls = 0;
            var owner = new Window();
            var dialog = WorkspaceCommandDialog.Build(owner, [new("Tool", "Details", () => calls++)]);
            using var source = InputSource();
            try
            {
                var search = Find<TextBox>(dialog, "CommandSearch");
                Compose(search, TextCompositionManager.PreviewTextInputStartEvent);
                Assert.False(Key(search, source, System.Windows.Input.Key.Enter).Handled);
                Compose(search, TextCompositionManager.PreviewTextInputUpdateEvent);
                Assert.False(Key(search, source, System.Windows.Input.Key.Enter).Handled);
                Assert.False(Key(search, source, System.Windows.Input.Key.ImeProcessed).Handled);
                Assert.False(Key(dialog, source, System.Windows.Input.Key.Escape).Handled);
                PumpDispatcher();
                Assert.Equal(0, calls);
                Compose(search, TextCompositionManager.PreviewTextInputEvent);
                Assert.True(Key(search, source, System.Windows.Input.Key.Enter).Handled);
                PumpDispatcher();
                Assert.Equal(1, calls);
            }
            finally { dialog.Close(); owner.Close(); }
        });
    }

    [Theory]
    [InlineData(690, 640)]
    [InlineData(420, 400)]
    [InlineData(404, 361)]
    public void DetachedLayoutKeepsNamedTouchTargetsAndContentWithinWidth(double width, double height)
    {
        Sta(() =>
        {
            var owner = new Window();
            var dialog = WorkspaceCommandDialog.Build(owner,
            [SearchableCommand(() => { }) with { Category = "Drawing" }, new("A longer command explaining how to manage notebook sections", "Select a section to manage its pages.", null) { Category = "Organization", Location = "Notebook > Sections > Page organizer" }]);
            try
            {
                var content = Detach(dialog);
                Layout(content, width, height);
                foreach (var name in new[] { "CommandSearch", "CommandCategory", "ClearCommandSearchButton", "RunCommandButton", "CloseCommandButton", "CommandList" })
                {
                    var control = Find<Control>(dialog, name);
                    Assert.True(control.ActualWidth >= 44, $"{name} is narrower than 44 DIP.");
                    Assert.True(control.ActualHeight >= 44, $"{name} is shorter than 44 DIP.");
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)));
                    var bounds = new Rect(control.TranslatePoint(new Point(), content), control.RenderSize);
                    Assert.True(bounds.Left >= 0 && bounds.Right <= width + .5, $"{name} extends outside the surface width.");
                    Assert.True(bounds.Top >= 0 && bounds.Bottom <= height + .5, $"{name} extends outside the surface height.");
                }
                Assert.All(Descendants<ScrollViewer>(content), scroll => Assert.Equal(0, scroll.ScrollableWidth));
                Assert.True(Find<TextBlock>(dialog, "CommandDetails").ActualWidth <= width);
                Assert.Equal(IntPtr.Zero, new WindowInteropHelper(dialog).Handle);
            }
            finally { dialog.Close(); owner.Close(); }
        });
    }

    [Theory]
    [InlineData("TwoFingerNavigation")]
    [InlineData("LockZoom")]
    [InlineData("CompactToolbar")]
    [InlineData("FocusToolsDock")]
    [InlineData("BackupEnabled")]
    [InlineData("BackupFolder")]
    [InlineData("BackupInterval")]
    [InlineData("BackupRetention")]
    public void OpeningSettingsAtANamedControlDoesNotChangePreferences(string focusSetting)
    {
        Sta(() =>
        {
            var owner = new Window();
            var preferences = new WorkspacePreferences { LockZoom = true, BackupIntervalHours = 48, BackupRetention = 12 };
            var before = JsonSerializer.Serialize(preferences);
            var dialog = WorkspaceOptionsDialog.Build(owner, preferences, focusSetting);
            try
            {
                var control = Find<Control>(dialog, focusSetting);
                Assert.True(control.Focusable);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)));
                dialog.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                Assert.Equal(before, JsonSerializer.Serialize(preferences));
                Assert.Null(dialog.DialogResult);
            }
            finally { dialog.Close(); owner.Close(); }
        });
    }

    private static WorkspaceCommand SearchableCommand(Action execute) => new("Highlighter", "Add translucent ink.", execute)
    { Id = "writing.highlighter", Category = "Writing", Shortcut = "Ctrl+Shift+H", Keywords = "marker emphasis", Location = "Toolbar > Pen" };

    private static T Find<T>(Window window, string name) where T : FrameworkElement => Assert.IsAssignableFrom<T>(window.FindName(name));
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void DoubleClick(ListBox list, ListBoxItem item) => list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
    { RoutedEvent = Control.MouseDoubleClickEvent, Source = item });
    private static KeyEventArgs Key(UIElement element, PresentationSource source, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        element.RaiseEvent(args); return args;
    }
    private static void Compose(TextBox search, RoutedEvent routedEvent) => search.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,
        new TextComposition(InputManager.Current, search, "candidate")) { RoutedEvent = routedEvent });
    private static HwndSource InputSource() => new(new HwndSourceParameters("Penroam command input test")
    { Width = 1, Height = 1, WindowStyle = unchecked((int)0x80000000) });
    private static FrameworkElement Detach(Window dialog)
    {
        var content = (FrameworkElement)dialog.Content;
        dialog.Content = null;
        TextElement.SetFontFamily(content, dialog.FontFamily);
        TextElement.SetFontSize(content, dialog.FontSize);
        TextElement.SetForeground(content, dialog.Foreground);
        return content;
    }
    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
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
    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Workspace command test exceeded 30 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
