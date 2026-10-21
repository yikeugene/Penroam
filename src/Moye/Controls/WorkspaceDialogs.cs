using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Moye.Models;
using L = Moye.Localization.LocalizationService;

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
    public static bool Show(Window owner, WorkspacePreferences preferences, string? focusSetting = null) => Build(owner, preferences, focusSetting).ShowDialog() == true;

    public static Window Build(Window owner, WorkspacePreferences preferences, string? focusSetting = null)
    {
        var window = WorkspaceDialogUi.Window(owner, L.Text("Controls.Workspace.Title"), height: 670);
        NameScope.SetNameScope(window, new NameScope());
        void Name(FrameworkElement control, string name) { control.Name = name; window.RegisterName(name, control); }
        window.Loaded += (_, _) =>
        {
            if (focusSetting is not null && window.FindName(focusSetting) is FrameworkElement control)
            { control.BringIntoView(); control.Focus(); }
        };
        var panel = new StackPanel { Margin = new Thickness(24) };
        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        panel.Children.Add(WorkspaceDialogUi.Text(L.Text("Controls.Workspace.Writing")));
        CheckBox Check(string name, string label, bool value)
        {
            var box = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, IsChecked = value, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
            Name(box, name); AutomationProperties.SetName(box, label);
            panel.Children.Add(box); return box;
        }
        var touch = Check("TwoFingerNavigation", L.Text("Controls.Workspace.TwoFingers"), preferences.TwoFingerNavigationOnly);
        var zoom = Check("LockZoom", L.Text("Features.Command.zoom-lock.Name"), preferences.LockZoom);
        var compact = Check("CompactToolbar", L.Text("Features.HideTheFavoritePenRowInTheMain"), preferences.CompactToolbar);
        var right = Check("FocusToolsDock", L.Text("Controls.Workspace.FocusRight"), preferences.FocusToolsOnRight);
        panel.Children.Add(WorkspaceDialogUi.Text(L.Text("Controls.Workspace.Backups")));
        var enabled = Check("BackupEnabled", L.Text("Controls.Workspace.BackupEnabled"), preferences.BackupEnabled);
        var directory = new TextBox { Text = preferences.BackupDirectory, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
        Name(directory, "BackupFolder"); AutomationProperties.SetName(directory, L.Text("Controls.Workspace.BackupFolder"));
        panel.Children.Add(directory);
        panel.Children.Add(WorkspaceDialogUi.Button(L.Text("Controls.Workspace.ChooseFolder"), (_, _) =>
        {
            var picker = new OpenFolderDialog { Title = L.Text("Controls.Workspace.BackupFolder") };
            if (picker.ShowDialog(window) == true) directory.Text = picker.FolderName;
        }));
        panel.Children.Add(WorkspaceDialogUi.Text(L.Text("Controls.Workspace.BackupIntervals")));
        var values = new StackPanel { Orientation = Orientation.Horizontal };
        var hours = new TextBox { Text = preferences.BackupIntervalHours.ToString(), Width = 100, MinHeight = 44, Margin = new Thickness(0, 0, 12, 0) };
        var retention = new TextBox { Text = preferences.BackupRetention.ToString(), Width = 100, MinHeight = 44 };
        Name(hours, "BackupInterval"); AutomationProperties.SetName(hours, L.Text("Features.BackupIntervalInHours"));
        Name(retention, "BackupRetention"); AutomationProperties.SetName(retention, L.Text("Features.NumberOfCompletedBackupsToKeep"));
        values.Children.Add(hours); values.Children.Add(retention); panel.Children.Add(values);
        panel.Children.Add(WorkspaceDialogUi.Text(L.Text("Controls.Workspace.BackupHint")));
        var error = WorkspaceDialogUi.Text(""); error.Foreground = Brushes.DarkRed; panel.Children.Add(error);
        panel.Children.Add(WorkspaceDialogUi.Button(L.Text("Controls.Workspace.Save"), (_, _) =>
        {
            if (!int.TryParse(hours.Text, out var interval) || interval is < 1 or > 168 || !int.TryParse(retention.Text, out var keep) || keep is < 1 or > 100)
            { error.Text = L.Text("Controls.Workspace.InvalidInterval"); return; }
            if (enabled.IsChecked == true && string.IsNullOrWhiteSpace(directory.Text)) { error.Text = L.Text("Controls.Workspace.FolderRequired"); return; }
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
        var cancel = WorkspaceDialogUi.Button(L.Text("Controls.Common.Cancel"), (_, _) => cancellation.Cancel()); panel.Children.Add(cancel);
        window.Content = panel;
        var done = false;
        window.Closing += (_, e) => { if (!done) { e.Cancel = true; cancellation.Cancel(); cancel.IsEnabled = false; label.Text = L.Text("Controls.Workspace.Canceling"); } };
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

public sealed record WorkspaceCommand(string Name, string Detail, Action? Execute)
{
    public string Id { get; init; } = "";
    public string? ParentId { get; init; }
    public string Category { get; init; } = "Tools";
    public string Shortcut { get; init; } = "";
    public string Keywords { get; init; } = "";
    public string Location { get; init; } = "";
    public Func<string?>? GetUnavailableReason { get; init; }
    public Func<bool>? GetChecked { get; init; }

    public bool IsInstruction => Execute is null;
    public string? UnavailableReason => GetUnavailableReason?.Invoke();
    public bool CanExecute => !IsInstruction && UnavailableReason is null;
    public bool IsChecked => GetChecked?.Invoke() == true;

    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var searchable = string.Join(" ", Name, Detail, Category, Shortcut, Keywords, Location);
        return query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .All(word => searchable.Contains(word, StringComparison.CurrentCultureIgnoreCase));
    }

    public bool TryExecute()
    {
        if (!CanExecute) return false;
        Execute!();
        return true;
    }
}

public static class WorkspaceCommandDialog
{
    internal static void Show(Window owner, IReadOnlyList<WorkspaceCommand> commands) => Build(owner, commands).ShowDialog();

    public static Window Build(Window owner, IReadOnlyList<WorkspaceCommand> commands)
    {
        var previousFocus = Keyboard.FocusedElement as UIElement;
        if (previousFocus is null || Window.GetWindow(previousFocus) != owner)
            previousFocus = FocusManager.GetFocusedElement(owner) as UIElement;
        var window = WorkspaceDialogUi.Window(owner, L.Text("Features.AllToolsHelp"), 690, 640);
        window.MinHeight = 400;
        NameScope.SetNameScope(window, new NameScope());
        var muted = owner.TryFindResource("MutedInk") as Brush ?? new SolidColorBrush(Color.FromRgb(101, 115, 109));
        var layout = new Grid { Margin = new Thickness(16) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        window.Content = layout;

        void Name(FrameworkElement element, string name, string accessibleName)
        {
            element.Name = name;
            window.RegisterName(name, element);
            AutomationProperties.SetName(element, accessibleName);
        }
        void Add(FrameworkElement element, int row)
        {
            Grid.SetRow(element, row); layout.Children.Add(element);
        }

        var search = new TextBox { MinHeight = 44, MinWidth = 44, VerticalContentAlignment = VerticalAlignment.Center };
        Name(search, "CommandSearch", L.Text("Features.SearchToolsHelpAndShortcuts"));
        var searchLabel = new Label { Content = L.Text("Features.SearchToolsHelp"), Target = search, Padding = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetLabeledBy(search, searchLabel);
        Add(searchLabel, 0);
        var searchRow = new Grid();
        searchRow.ColumnDefinitions.Add(new ColumnDefinition());
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.Children.Add(search);
        var clear = WorkspaceDialogUi.Button(L.Text("Controls.Extend.Reset"), (_, _) => { });
        clear.MinWidth = 44; clear.Margin = new Thickness(8, 0, 0, 0);
        clear.ToolTip = L.Text("Features.ClearTheSearchAndShowAllCategories");
        clear.Style = owner.TryFindResource("SecondaryButton") as Style;
        Name(clear, "ClearCommandSearchButton", L.Text("Features.ClearSearchAndCategoryFilter"));
        Grid.SetColumn(clear, 1); searchRow.Children.Add(clear);
        Add(searchRow, 1);

        var categoryRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        categoryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        categoryRow.ColumnDefinitions.Add(new ColumnDefinition());
        var category = new ComboBox
        {
            MinHeight = 44, MinWidth = 44, VerticalContentAlignment = VerticalAlignment.Center,
            ItemsSource = new[] { L.Text("Features.AllCategories") }.Concat(commands.Select(command => command.Category)
                .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)).ToArray(),
            SelectedIndex = 0
        };
        Name(category, "CommandCategory", L.Text("Features.FilterToolsByCategory"));
        var categoryLabel = new Label { Content = L.Text("Controls.Common.Category"), Target = category, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0, 0, 10, 0) };
        AutomationProperties.SetLabeledBy(category, categoryLabel);
        categoryRow.Children.Add(categoryLabel); Grid.SetColumn(category, 1); categoryRow.Children.Add(category);
        var categoryStyle = new Style(typeof(ComboBoxItem));
        categoryStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 44d));
        categoryStyle.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 44d));
        categoryStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        category.ItemContainerStyle = categoryStyle;
        Add(categoryRow, 2);

        var count = new TextBlock { Foreground = muted, Margin = new Thickness(0, 6, 0, 6), TextWrapping = TextWrapping.Wrap };
        Name(count, "CommandResultCount", L.Text("Features.MatchingToolsAndHelpTopics"));
        count.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(TextBlock.Text)) { Source = count });
        AutomationProperties.SetLiveSetting(count, AutomationLiveSetting.Polite);
        Add(count, 3);
        var list = new ListBox { MinHeight = 44, MinWidth = 44, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Name(list, "CommandList", L.Text("Features.ToolsAndHelpTopics"));
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 44d));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 44d));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(AutomationProperties.NameProperty, new Binding(nameof(CommandRow.AccessibleName))));
        list.ItemContainerStyle = style;
        list.ItemTemplate = RowTemplate();
        var results = new Grid(); results.Children.Add(list);
        var empty = WorkspaceDialogUi.Text(L.Text("Features.NoToolsMatchUseResetToClearThe"));
        empty.Margin = new Thickness(12); empty.VerticalAlignment = VerticalAlignment.Center; empty.IsHitTestVisible = false;
        Name(empty, "CommandEmptyState", L.Text("Features.NoToolsMatchResetTheFiltersToSee"));
        results.Children.Add(empty); Add(results, 4);

        var details = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Name(details, "CommandDetails", L.Text("Features.SelectedToolDetailsAndRequirements"));
        details.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(TextBlock.Text)) { Source = details });
        var detailsScroll = new ScrollViewer
        {
            Content = details, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var detailsBorder = new Border
        {
            Background = owner.TryFindResource("SubtleSurface") as Brush ?? new SolidColorBrush(Color.FromRgb(240, 243, 238)),
            BorderBrush = owner.TryFindResource("Stroke") as Brush ?? new SolidColorBrush(Color.FromRgb(221, 227, 220)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10),
            Margin = new Thickness(0, 8, 0, 0), Child = detailsScroll
        };
        Add(detailsBorder, 5);
        var actions = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var run = WorkspaceDialogUi.Button(L.Text("Features.RunSelectedCommand"), (_, _) => { });
        run.MinWidth = 44; run.Margin = new Thickness(0, 0, 8, 0);
        run.Style = owner.TryFindResource("PrimaryButton") as Style;
        Name(run, "RunCommandButton", L.Text("Features.RunSelectedCommand"));
        actions.Children.Add(run);
        var close = WorkspaceDialogUi.Button(L.Text("Controls.Common.Close"), (_, _) => window.Close());
        close.MinWidth = 44; close.Margin = new Thickness(0);
        close.Style = owner.TryFindResource("SecondaryButton") as Style;
        Name(close, "CloseCommandButton", L.Text("Features.CloseAllToolsAndHelp"));
        Grid.SetColumn(close, 1); actions.Children.Add(close); Add(actions, 6);

        var closed = false;
        var dispatchQueued = false;
        var composing = false;
        void UpdateSelection()
        {
            if (list.SelectedItem is not CommandRow row)
            {
                details.Text = L.Text("Features.SelectAToolToSeeHowToUse");
                run.IsEnabled = false; run.Visibility = Visibility.Visible;
                return;
            }
            row.Refresh();
            details.Text = string.Join("\n\n", new[] { row.Name, row.Breadcrumb, row.Command.Detail, row.Status, row.Availability }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            detailsScroll.ScrollToTop();
            run.IsEnabled = row.CanExecute;
            run.Visibility = row.Command.IsInstruction ? Visibility.Collapsed : Visibility.Visible;
        }
        void Filter()
        {
            var selected = (list.SelectedItem as CommandRow)?.Command;
            var rows = commands.Where(command => command.Matches(search.Text)
                    && (category.SelectedIndex == 0 || string.Equals(command.Category, category.SelectedItem as string, StringComparison.CurrentCultureIgnoreCase)))
                .OrderBy(command => command.Category, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(command => command.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(command => new CommandRow(command)).ToArray();
            list.ItemsSource = rows;
            list.SelectedItem = rows.FirstOrDefault(row => ReferenceEquals(row.Command, selected)) ?? rows.FirstOrDefault();
            count.Text = L.Text(rows.Length == 1 ? "Features.SingleToolCount" : "Features.ToolCount", rows.Length);
            empty.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            clear.IsEnabled = search.Text.Length != 0 || category.SelectedIndex != 0;
            UpdateSelection();
        }
        void Run()
        {
            if (closed || dispatchQueued || composing || list.SelectedItem is not CommandRow row) return;
            if (!row.Command.CanExecute) { UpdateSelection(); return; }
            dispatchQueued = true;
            window.Close();
            if (!closed) { dispatchQueued = false; return; }
            // Closing a modal window unwinds before the next command can open its own dialog.
            owner.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => row.Command.TryExecute()));
        }
        void RunKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || composing || Keyboard.Modifiers != ModifierKeys.None) return;
            Run(); e.Handled = true;
        }
        search.TextChanged += (_, _) => Filter();
        category.SelectionChanged += (_, _) => Filter();
        clear.Click += (_, _) => { search.Clear(); category.SelectedIndex = 0; search.Focus(); };
        list.SelectionChanged += (_, _) => UpdateSelection();
        list.MouseDoubleClick += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source
                || ItemsControl.ContainerFromElement(list, source) is not ListBoxItem item) return;
            list.SelectedItem = item.DataContext;
            Run(); e.Handled = true;
        };
        run.Click += (_, _) => Run();
        search.PreviewKeyDown += RunKeyDown; list.PreviewKeyDown += RunKeyDown;
        TextCompositionManager.AddPreviewTextInputStartHandler(search, (_, _) => composing = true);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(search, (_, _) => composing = true);
        search.PreviewTextInput += (_, _) => composing = false;
        search.LostKeyboardFocus += (_, _) => composing = false;
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || composing || category.IsDropDownOpen) return;
            window.Close(); e.Handled = true;
        };
        window.Activated += (_, _) =>
        {
            foreach (CommandRow row in list.Items) row.Refresh();
            UpdateSelection();
        };
        window.Loaded += (_, _) => search.Focus();
        window.Closed += (_, _) =>
        {
            closed = true;
            if (dispatchQueued || previousFocus is null) return;
            owner.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                // A later modal command owns focus; never reactivate the owner behind it.
                if (owner.IsActive && owner.IsEnabled && owner.IsVisible
                    && previousFocus is { IsVisible: true, IsEnabled: true, Focusable: true }
                    && Window.GetWindow(previousFocus) == owner)
                    previousFocus.Focus();
            }));
        };
        Filter();
        return window;
    }

    private static DataTemplate RowTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        foreach (var property in new[] { nameof(CommandRow.Name), nameof(CommandRow.Breadcrumb), nameof(CommandRow.Status), nameof(CommandRow.Availability) })
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding(property));
            text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            if (property == nameof(CommandRow.Name)) text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            else text.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 0));
            if (property == nameof(CommandRow.Breadcrumb)) text.SetValue(TextBlock.FontSizeProperty, 12d);
            var textStyle = new Style(typeof(TextBlock));
            var empty = new DataTrigger { Binding = new Binding(property), Value = "" };
            empty.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
            textStyle.Triggers.Add(empty); text.SetValue(FrameworkElement.StyleProperty, textStyle);
            panel.AppendChild(text);
        }
        return new DataTemplate { VisualTree = panel };
    }

    private sealed class CommandRow : INotifyPropertyChanged
    {
        public CommandRow(WorkspaceCommand command) { Command = command; Refresh(); }
        public WorkspaceCommand Command { get; }
        public string Name => Command.Name;
        public string Breadcrumb => string.IsNullOrWhiteSpace(Command.Location) ? Command.Category :
            Command.Location == Command.Category || Command.Location.StartsWith(Command.Category + " → ", StringComparison.Ordinal) ||
            Command.Location.StartsWith(Command.Category + " > ", StringComparison.Ordinal)
                ? Command.Location : $"{Command.Category} > {Command.Location}";
        public string Status { get; private set; } = "";
        public string Availability { get; private set; } = "";
        public bool CanExecute { get; private set; }
        public string AccessibleName => string.Join(". ", new[] { Name, Breadcrumb, Status, Availability }.Where(value => !string.IsNullOrWhiteSpace(value)));
        public event PropertyChangedEventHandler? PropertyChanged;

        public void Refresh()
        {
            var reason = Command.UnavailableReason;
            CanExecute = !Command.IsInstruction && reason is null;
            Availability = reason is null ? "" : L.Text("Features.Unavailable", reason);
            Status = string.Join("   ", new[]
            {
                Command.IsInstruction ? L.Text("Features.HowToUse") : "",
                string.IsNullOrWhiteSpace(Command.Shortcut) ? "" : L.Text("Features.Shortcut", Command.Shortcut),
                Command.GetChecked is null ? "" : Command.IsChecked ? "On" : "Off"
            }.Where(value => value.Length != 0));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
    }
}
