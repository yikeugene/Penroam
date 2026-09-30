using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Moye.Models;

namespace Moye.Controls;

internal static class WorkspaceDialogUi
{
    internal static Window Window(Window owner, string title, double width = 600, double height = 580) => new()
    {
        Owner = owner.IsLoaded ? owner : null, Title = title, Width = width, Height = height, MinWidth = 420, MinHeight = 340,
        MaxHeight = SystemParameters.WorkArea.Height - 32, MaxWidth = SystemParameters.WorkArea.Width - 32,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        Background = Brushes.White, FontFamily = owner.FontFamily, FontSize = 14
    };
    internal static Button Button(string text, RoutedEventHandler action)
    {
        var button = new Button { Content = text, MinHeight = 44, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(3) };
        button.Click += action; return button;
    }
    internal static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
}

public sealed class WorkspaceOptionsDialog
{
    public static bool Show(Window owner, WorkspacePreferences preferences) => Build(owner, preferences).ShowDialog() == true;

    public static Window Build(Window owner, WorkspacePreferences preferences)
    {
        var window = WorkspaceDialogUi.Window(owner, "Workspace settings", height: 670);
        var panel = new StackPanel { Margin = new Thickness(24) };
        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(WorkspaceDialogUi.Text("Writing and workspace"));
        CheckBox Check(string label, bool value)
        {
            var box = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, IsChecked = value, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(box, label);
            panel.Children.Add(box); return box;
        }
        var touch = Check("Use two fingers to move pages (ignore one-finger drags)", preferences.TwoFingerNavigationOnly);
        var zoom = Check("Lock zoom while writing", preferences.LockZoom);
        var compact = Check("Hide the favorite pen row in the main toolbar", preferences.CompactToolbar);
        var right = Check("Dock focus tools on the right", preferences.FocusToolsOnRight);
        panel.Children.Add(WorkspaceDialogUi.Text("Automatic local backups"));
        var enabled = Check("Back up while Penroam is open", preferences.BackupEnabled);
        var directory = new TextBox { Text = preferences.BackupDirectory, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
        panel.Children.Add(directory);
        panel.Children.Add(WorkspaceDialogUi.Button("Choose backup folder…", (_, _) =>
        {
            var picker = new OpenFolderDialog { Title = "Automatic backup folder" };
            if (picker.ShowDialog(window) == true) directory.Text = picker.FolderName;
        }));
        panel.Children.Add(WorkspaceDialogUi.Text("Interval in hours (1–168) · keep completed backups (1–100)"));
        var values = new StackPanel { Orientation = Orientation.Horizontal };
        var hours = new TextBox { Text = preferences.BackupIntervalHours.ToString(), Width = 100, MinHeight = 44, Margin = new Thickness(0, 0, 12, 0) };
        var retention = new TextBox { Text = preferences.BackupRetention.ToString(), Width = 100, MinHeight = 44 };
        values.Children.Add(hours); values.Children.Add(retention); panel.Children.Add(values);
        panel.Children.Add(WorkspaceDialogUi.Text("Backups contain editable notebooks. Recently deleted items are kept separately for 30 days. Both remain on this device unless you choose a synchronized folder."));
        var error = WorkspaceDialogUi.Text(""); error.Foreground = Brushes.DarkRed; panel.Children.Add(error);
        panel.Children.Add(WorkspaceDialogUi.Button("Save settings", (_, _) =>
        {
            if (!int.TryParse(hours.Text, out var interval) || interval is < 1 or > 168 || !int.TryParse(retention.Text, out var keep) || keep is < 1 or > 100)
            { error.Text = "Enter an interval from 1 to 168 hours and keep 1 to 100 backups."; return; }
            if (enabled.IsChecked == true && string.IsNullOrWhiteSpace(directory.Text)) { error.Text = "Choose a backup folder first."; return; }
            preferences.TwoFingerNavigationOnly = touch.IsChecked == true;
            preferences.LockZoom = zoom.IsChecked == true; preferences.CompactToolbar = compact.IsChecked == true;
            preferences.FocusToolsOnRight = right.IsChecked == true; preferences.BackupEnabled = enabled.IsChecked == true;
            preferences.BackupDirectory = directory.Text.Trim(); preferences.BackupIntervalHours = interval; preferences.BackupRetention = keep;
            window.DialogResult = true;
        }));
        return window;
    }
}

internal sealed class SafetyProgressDialog
{
    internal static async Task RunAsync(Window owner, string title, Func<IProgress<string>, CancellationToken, Task> action)
    {
        var window = WorkspaceDialogUi.Window(owner, title, 520, 250); window.MinHeight = 220;
        using var cancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var panel = new StackPanel { Margin = new Thickness(24) };
        var label = WorkspaceDialogUi.Text(title); panel.Children.Add(label);
        panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 6, Margin = new Thickness(0, 12, 0, 12) });
        var cancel = WorkspaceDialogUi.Button("Cancel", (_, _) => cancellation.Cancel()); panel.Children.Add(cancel);
        window.Content = panel;
        var done = false;
        window.Closing += (_, e) => { if (!done) { e.Cancel = true; cancellation.Cancel(); cancel.IsEnabled = false; label.Text = "Canceling after the current safe step…"; } };
        window.Loaded += async (_, _) =>
        {
            try { await action(new Progress<string>(message => label.Text = message), cancellation.Token); completion.SetResult(); }
            catch (OperationCanceledException) { completion.SetCanceled(); }
            catch (Exception ex) { completion.SetException(ex); }
            finally { done = true; window.Close(); }
        };
        window.ShowDialog(); await completion.Task;
    }
}

public sealed record WorkspaceCommand(string Name, string Detail, Action Execute);

public static class WorkspaceCommandDialog
{
    internal static void Show(Window owner, IReadOnlyList<WorkspaceCommand> commands) => Build(owner, commands).ShowDialog();

    public static Window Build(Window owner, IReadOnlyList<WorkspaceCommand> commands)
    {
        var window = WorkspaceDialogUi.Window(owner, "Commands & help", 690, 640);
        var layout = new DockPanel { Margin = new Thickness(22) };
        var search = new TextBox { MinHeight = 44, Margin = new Thickness(0, 0, 0, 12), ToolTip = "Search commands, tools and shortcuts" };
        System.Windows.Automation.AutomationProperties.SetName(search, "Find a command");
        DockPanel.SetDock(search, Dock.Top); layout.Children.Add(search);
        var hint = WorkspaceDialogUi.Text("Write with Pen · Move with your fingers · Use Lasso to move notes together. Select a command for its shortcut and explanation. Esc closes this panel.");
        DockPanel.SetDock(hint, Dock.Bottom); layout.Children.Add(hint);
        var run = WorkspaceDialogUi.Button("Run selected command", (_, _) => { }); DockPanel.SetDock(run, Dock.Bottom); layout.Children.Add(run);
        var list = new ListBox { DisplayMemberPath = nameof(WorkspaceCommand.Name), MinHeight = 44 };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        var style = new Style(typeof(ListBoxItem)); style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 44d)); style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center)); list.ItemContainerStyle = style;
        layout.Children.Add(list); window.Content = layout;
        void Filter() { list.ItemsSource = commands.Where(command => (command.Name + " " + command.Detail).Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)).ToArray(); list.SelectedIndex = 0; }
        void Run() { if (list.SelectedItem is WorkspaceCommand command) { window.Close(); owner.Dispatcher.BeginInvoke(command.Execute); } }
        search.TextChanged += (_, _) => Filter();
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is WorkspaceCommand command) hint.Text = command.Detail; };
        list.MouseDoubleClick += (_, _) => Run(); run.Click += (_, _) => Run();
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { window.Close(); e.Handled = true; } else if (e.Key == Key.Enter) { Run(); e.Handled = true; } };
        window.Loaded += (_, _) => search.Focus(); Filter(); return window;
    }
}
