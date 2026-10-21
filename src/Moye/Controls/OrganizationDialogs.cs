using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Moye.Models;
using Moye.ViewModels;
using L = Moye.Localization.LocalizationService;

namespace Moye.Controls;

internal static class OrganizationDialogUi
{
    public static StackPanel Initialize(Window window, Window owner, string title, double width = 520)
    {
        window.Owner = owner?.IsLoaded == true ? owner : null; window.Title = title; window.Width = width; window.SizeToContent = SizeToContent.Height;
        window.MaxHeight = SystemParameters.WorkArea.Height; window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.ShowInTaskbar = false; window.Background = Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18), TextWrapping = TextWrapping.Wrap });
        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return panel;
    }
    public static Button Button(Window window, string text, Action action)
    {
        var button = new Button { Content = text, MinHeight = 44, MinWidth = 80, Margin = new Thickness(3), Padding = new Thickness(14, 8, 14, 8) };
        AutomationProperties.SetName(button, text); button.Click += (_, _) => action(); return button;
    }
    public static void Finish(Window window, Panel panel)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = Button(window, L.Text("Controls.Common.Cancel"), () => window.DialogResult = false); cancel.IsCancel = true;
        var apply = Button(window, L.Text("Controls.Common.Apply"), () => window.DialogResult = true); apply.IsDefault = true;
        row.Children.Add(cancel); row.Children.Add(apply); panel.Children.Add(row);
    }
    public static TextBox Field(Panel panel, string label, string text)
    {
        var input = new TextBox { Text = text, MaxLength = 160, Margin = new Thickness(0, 5, 0, 14), MinHeight = 44 };
        AutomationProperties.SetName(input, label); panel.Children.Add(new Label { Content = label, Target = input }); panel.Children.Add(input); return input;
    }
}

public sealed class PageOrganizationDetailsDialog : Window
{
    private readonly TextBox _title;
    private readonly CheckBox _bookmark;
    public string PageTitle => _title.Text.Trim();
    public bool IsBookmarked => _bookmark.IsChecked == true;
    public PageOrganizationDetailsDialog(Window owner, NotePage page)
    {
        var panel = OrganizationDialogUi.Initialize(this, owner, L.Text("Controls.Organization.PageDetails"));
        _title = OrganizationDialogUi.Field(panel, L.Text("Controls.Organization.PageTitle"), page.Title);
        _bookmark = new CheckBox { Content = L.Text("Controls.Organization.Bookmark"), IsChecked = page.IsBookmarked, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
        panel.Children.Add(_bookmark); OrganizationDialogUi.Finish(this, panel);
        Loaded += (_, _) => { _title.Focus(); _title.SelectAll(); };
    }
}

public sealed class NotebookOrganizationDialog : Window
{
    private readonly CheckBox _pinned;
    private readonly ComboBox _colors;
    public bool IsPinned => _pinned.IsChecked == true;
    public string CoverColor => (_colors.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
    public NotebookOrganizationDialog(Window owner, NotebookSummary notebook)
    {
        var panel = OrganizationDialogUi.Initialize(this, owner, L.Text("Controls.Organization.NotebookAppearance"));
        panel.Children.Add(new TextBlock { Text = notebook.Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        _pinned = new CheckBox { Content = L.Text("Controls.Organization.Pin"), IsChecked = notebook.IsPinned, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
        panel.Children.Add(_pinned); panel.Children.Add(new Label { Content = L.Text("Controls.Organization.CoverColor") });
        _colors = new ComboBox { MinHeight = 44 };
        var names = new[] { L.Text("Controls.Organization.Cover.Forest"), L.Text("Controls.Organization.Cover.Slate"), L.Text("Controls.Organization.Cover.Terracotta"), L.Text("Controls.Organization.Cover.Plum"), L.Text("Controls.Organization.Cover.Ochre"), L.Text("Controls.Organization.Cover.Teal") };
        var automatic = new ComboBoxItem { Content = L.Text("Controls.Organization.AutomaticColor"), Tag = "" }; _colors.Items.Add(automatic);
        for (var index = 0; index < NotebookAppearance.Colors.Count; index++)
        {
            var color = NotebookAppearance.Colors[index]; var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border { Background = (Brush)new BrushConverter().ConvertFromString(color)!, Width = 28, Height = 24, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 10, 0) });
            row.Children.Add(new TextBlock { Text = names[index], VerticalAlignment = VerticalAlignment.Center });
            var item = new ComboBoxItem { Content = row, Tag = color, MinHeight = 36 }; _colors.Items.Add(item);
            if (string.Equals(notebook.CoverColor, color, StringComparison.OrdinalIgnoreCase)) _colors.SelectedItem = item;
        }
        if (_colors.SelectedItem is null) _colors.SelectedItem = automatic;
        AutomationProperties.SetName(_colors, L.Text("Controls.Organization.CoverColor")); panel.Children.Add(_colors); OrganizationDialogUi.Finish(this, panel);
    }
}

public sealed class LibraryOrganizationDialog : Window
{
    private readonly ComboBox _category;
    private readonly CheckBox _sort;
    public string Category => (_category.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
    public bool SortByName => _sort.IsChecked == true;
    public LibraryOrganizationDialog(Window owner, MainViewModel model)
    {
        var panel = OrganizationDialogUi.Initialize(this, owner, L.Text("Controls.Organization.FilterSort"));
        panel.Children.Add(new Label { Content = L.Text("Controls.Common.Category") }); _category = new ComboBox { MinHeight = 44 };
        foreach (var value in new[] { "" }.Concat(model.Categories))
        {
            var item = new ComboBoxItem { Content = value.Length == 0 ? L.Text("Controls.Organization.AllCategories") : value, Tag = value, MinHeight = 36 };
            _category.Items.Add(item); if (value == model.CategoryFilter) _category.SelectedItem = item;
        }
        AutomationProperties.SetName(_category, L.Text("Controls.Organization.CategoryFilter")); panel.Children.Add(_category);
        _sort = new CheckBox { Content = L.Text("Controls.Organization.SortByName"), IsChecked = model.SortNotebooksByName, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
        panel.Children.Add(_sort); OrganizationDialogUi.Finish(this, panel);
    }
}

public sealed class PageOrganizationTransferDialog : Window
{
    private readonly ComboBox _notebooks = new() { MinHeight = 44, DisplayMemberPath = "Title" };
    private readonly ComboBox _sections = new() { MinHeight = 44, DisplayMemberPath = "Title" };
    private readonly CheckBox _copy = new() { Content = L.Text("Controls.Organization.KeepCopy"), MinHeight = 44, IsChecked = false };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _apply;
    public string NotebookId => ((NotebookSummary)_notebooks.SelectedItem).Id;
    public string SectionId => ((NoteSection)_sections.SelectedItem).Id;
    public bool Copy => _copy.IsChecked == true;
    public PageOrganizationTransferDialog(Window owner, MainViewModel model)
    {
        var panel = OrganizationDialogUi.Initialize(this, owner, L.Text("Controls.Organization.TransferTitle"));
        panel.Children.Add(new TextBlock { Text = L.Text("Controls.Organization.TransferHint"), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new Label { Content = L.Text("Controls.Organization.Notebook") }); panel.Children.Add(_notebooks);
        panel.Children.Add(new Label { Content = L.Text("Controls.Common.Section") }); panel.Children.Add(_sections); panel.Children.Add(_copy); panel.Children.Add(_status);
        AutomationProperties.SetName(_notebooks, L.Text("Controls.Organization.DestinationNotebook")); AutomationProperties.SetName(_sections, L.Text("Controls.Common.DestinationSection"));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = OrganizationDialogUi.Button(this, L.Text("Controls.Common.Cancel"), () => DialogResult = false); cancel.IsCancel = true;
        _apply = OrganizationDialogUi.Button(this, L.Text("Controls.Organization.Transfer"), () => DialogResult = true); _apply.IsEnabled = false;
        actions.Children.Add(cancel); actions.Children.Add(_apply); panel.Children.Add(actions);
        _notebooks.SelectionChanged += async (_, _) =>
        {
            _apply.IsEnabled = false; _sections.ItemsSource = null;
            if (_notebooks.SelectedItem is not NotebookSummary selected) return;
            try
            {
                var document = await model.Repository.LoadAsync(selected.Id);
                if ((_notebooks.SelectedItem as NotebookSummary)?.Id != selected.Id) return;
                _sections.ItemsSource = document?.Sections; _sections.SelectedIndex = 0; _apply.IsEnabled = _sections.SelectedItem is not null;
                _status.Text = document is null ? L.Text("Controls.Organization.NotebookMissing") : "";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        Loaded += async (_, _) =>
        {
            try { _notebooks.ItemsSource = (await model.Repository.ListAsync()).Where(note => note.Id != model.Document?.Id).ToArray(); _notebooks.SelectedIndex = 0; if (_notebooks.Items.Count == 0) _status.Text = L.Text("Controls.Organization.CreateDestination"); }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
    }
}
