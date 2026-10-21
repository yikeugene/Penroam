using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Moye.Models;
using L = Moye.Localization.LocalizationService;

namespace Moye.Controls;

/// <summary>Creates a notebook with an explicitly chosen first-page background.</summary>
public sealed class NotebookDialog : Window
{
    private readonly TextBox _title;
    private readonly TextBox _folder;
    private readonly PaperTemplatePicker _paper;

    public string NotebookTitle => _title.Text.Trim();
    public string Folder => _folder.Text.Trim();
    public PaperTemplate SelectedTemplate => _paper.SelectedTemplate;

    public NotebookDialog(Window owner, string title = "Untitled Notebook", string folder = "My Notes",
        PaperTemplate template = PaperTemplate.Ruled)
    {
        Owner = owner;
        Title = L.Text("Controls.Notebook.Title");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.White;
        Language = XmlLanguage.GetLanguage("en-US");

        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock
        {
            Text = L.Text("Controls.Notebook.Heading"), FontSize = 24, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        panel.Children.Add(new TextBlock
        {
            Text = L.Text("Controls.Notebook.Intro"),
            Foreground = (Brush)FindResource("MutedInk"), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 24)
        });

        _title = AddField(panel, L.Text("Controls.Notebook.Name"), title);
        _title.Name = "NotebookTitleInput";
        _folder = AddField(panel, L.Text("Controls.Common.Category"), folder);
        _folder.Name = "NotebookFolderInput";
        panel.Children.Add(new TextBlock { Text = L.Text("Controls.Notebook.Paper"), FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = L.Text("Controls.Notebook.PaperHint"),
            FontSize = 12, Foreground = (Brush)FindResource("MutedInk"),
            Margin = new Thickness(0, 5, 0, 10), TextWrapping = TextWrapping.Wrap
        });
        _paper = new PaperTemplatePicker { SelectedTemplate = template, Margin = new Thickness(-3, 0, -3, 8) };
        panel.Children.Add(_paper);

        var error = new TextBlock
        {
            Foreground = (Brush)FindResource("Danger"),
            TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 12)
        };
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        _title.TextChanged += (_, _) => error.Visibility = Visibility.Collapsed;
        _folder.TextChanged += (_, _) => error.Visibility = Visibility.Collapsed;
        panel.Children.Add(error);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var cancel = new Button { Name = "NotebookCancelButton", Content = L.Text("Controls.Common.Cancel"), IsCancel = true, MinWidth = 88, Style = (Style)FindResource("SecondaryButton") };
        var create = new Button
        {
            Name = "NotebookCreateButton", Content = L.Text("Controls.Notebook.Create"), IsDefault = true, MinWidth = 148,
            Style = (Style)FindResource("PrimaryButton")
        };
        create.Click += (_, _) =>
        {
            var empty = string.IsNullOrWhiteSpace(_title.Text) ? _title : string.IsNullOrWhiteSpace(_folder.Text) ? _folder : null;
            if (empty is not null)
            {
                error.Text = empty == _title ? L.Text("Controls.Notebook.NameRequired") : L.Text("Controls.Notebook.CategoryRequired");
                error.Visibility = Visibility.Visible;
                empty.Focus();
                return;
            }
            DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(create);
        panel.Children.Add(buttons);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Loaded += (_, _) => { _title.Focus(); _title.SelectAll(); };
    }

    private static TextBox AddField(Panel panel, string label, string value)
    {
        var input = new TextBox { Text = value, MaxLength = 160, Margin = new Thickness(0, 6, 0, 18) };
        AutomationProperties.SetName(input, label);
        panel.Children.Add(new Label { Content = label, Target = input, Padding = new Thickness(0), FontWeight = FontWeights.SemiBold });
        panel.Children.Add(input);
        return input;
    }
}
