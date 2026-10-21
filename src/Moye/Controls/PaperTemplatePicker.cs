using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Moye.Models;
using L = Moye.Localization.LocalizationService;

namespace Moye.Controls;

/// <summary>A visual, keyboard-accessible choice of the supported paper backgrounds.</summary>
public sealed class PaperTemplatePicker : UserControl
{
    public static readonly DependencyProperty SelectedTemplateProperty = DependencyProperty.Register(
        nameof(SelectedTemplate), typeof(PaperTemplate), typeof(PaperTemplatePicker),
        new FrameworkPropertyMetadata(PaperTemplate.Ruled,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedTemplateChanged));

    private readonly Dictionary<PaperTemplate, RadioButton> _choices = [];
    private readonly Dictionary<PaperTemplate, TextBlock> _selectionMarks = [];

    public PaperTemplate SelectedTemplate
    {
        get => (PaperTemplate)GetValue(SelectedTemplateProperty);
        set => SetValue(SelectedTemplateProperty, value);
    }

    public event EventHandler? SelectionChanged;

    public PaperTemplatePicker()
    {
        MinWidth = 240;
        var group = new UniformGrid { Columns = 4 };
        var groupName = $"PaperTemplates_{Guid.NewGuid():N}";
        AddChoice(group, groupName, PaperTemplate.Plain, "Controls.Paper.Plain.Caption", "Controls.Paper.Plain.Description");
        AddChoice(group, groupName, PaperTemplate.Ruled, "Controls.Paper.Ruled.Caption", "Controls.Paper.Ruled.Description");
        AddChoice(group, groupName, PaperTemplate.Grid, "Controls.Paper.Grid.Caption", "Controls.Paper.Grid.Description");
        AddChoice(group, groupName, PaperTemplate.DotGrid, "Controls.Paper.DotGrid.Caption", "Controls.Paper.DotGrid.Description");
        AddChoice(group, groupName, PaperTemplate.Cornell, "Controls.Paper.Cornell.Caption", "Controls.Paper.Cornell.Description");
        AddChoice(group, groupName, PaperTemplate.Graph, "Controls.Paper.Graph.Caption", "Controls.Paper.Graph.Description");
        AddChoice(group, groupName, PaperTemplate.Meeting, "Controls.Paper.Meeting.Caption", "Controls.Paper.Meeting.Description");
        Content = group;
        UpdateSelection();
    }

    private void AddChoice(Panel group, string groupName, PaperTemplate template, string nameKey, string descriptionKey)
    {
        var content = new StackPanel();
        var paper = new Border
        {
            Width = 40, Height = 56,
            BorderBrush = Brush("#DDE3DC"), BorderThickness = new Thickness(1),
            Background = Brushes.White, Margin = new Thickness(0, 0, 0, 6),
            Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = new PaperVisual { Width = 793.700787, Height = 1122.519685, Template = template }
            }
        };
        content.Children.Add(paper);
        var name = new TextBlock
        {
            FontSize = 13, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        L.Bind(name, TextBlock.TextProperty, nameKey);
        content.Children.Add(name);
        var description = new TextBlock
        {
            FontSize = 11, Foreground = Brush("#65736D"),
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0), MinHeight = 30
        };
        L.Bind(description, TextBlock.TextProperty, descriptionKey);
        content.Children.Add(description);

        var choiceContent = new Grid();
        choiceContent.Children.Add(content);
        var selectedMark = new TextBlock
        {
            Text = "✓", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brush("#236451"),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Hidden, IsHitTestVisible = false
        };
        choiceContent.Children.Add(selectedMark);
        _selectionMarks[template] = selectedMark;
        var choice = new RadioButton
        {
            GroupName = groupName, Content = choiceContent, MinHeight = 138,
            Margin = new Thickness(3), Padding = new Thickness(6, 7, 6, 7),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            Style = ChoiceStyle()
        };
        BindingOperations.SetBinding(choice, ToolTipProperty, L.FormatBinding("Controls.Paper.ToolTip", L.Binding(nameKey), L.Binding(descriptionKey)));
        BindingOperations.SetBinding(choice, AutomationProperties.NameProperty, L.FormatBinding("Controls.Paper.Accessible", L.Binding(nameKey)));
        L.Bind(choice, AutomationProperties.HelpTextProperty, descriptionKey);
        choice.Checked += (_, _) => SetCurrentValue(SelectedTemplateProperty, template);
        _choices[template] = choice;
        group.Children.Add(choice);
    }

    public static string DisplayName(PaperTemplate template) => template switch
    {
        PaperTemplate.Plain => L.Text("Controls.Paper.Plain.Name"),
        PaperTemplate.Ruled => L.Text("Controls.Paper.Ruled.Name"),
        PaperTemplate.Grid => L.Text("Controls.Paper.Grid.Name"),
        PaperTemplate.DotGrid => L.Text("Controls.Paper.DotGrid.Name"),
        PaperTemplate.Cornell => L.Text("Controls.Paper.Cornell.Name"),
        PaperTemplate.Graph => L.Text("Controls.Paper.Graph.Name"),
        PaperTemplate.Meeting => L.Text("Controls.Paper.Meeting.Name"),
        _ => template.ToString()
    };

    private static Style ChoiceStyle()
    {
        var style = new Style(typeof(RadioButton));
        style.Setters.Add(new Setter(ForegroundProperty, Brush("#24332F")));
        style.Setters.Add(new Setter(BackgroundProperty, Brush("#FAFBF8")));
        style.Setters.Add(new Setter(BorderBrushProperty, Brush("#DDE3DC")));
        style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(2)));

        var chrome = new FrameworkElementFactory(typeof(Border));
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        chrome.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        chrome.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
        chrome.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
        chrome.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentProperty));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        chrome.AppendChild(presenter);
        style.Setters.Add(new Setter(TemplateProperty, new ControlTemplate(typeof(RadioButton)) { VisualTree = chrome }));

        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(BackgroundProperty, Brush("#F0F3EE")));
        style.Triggers.Add(hover);
        var selected = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        selected.Setters.Add(new Setter(BorderBrushProperty, Brush("#236451")));
        selected.Setters.Add(new Setter(BackgroundProperty, Brush("#E7F0EA")));
        selected.Setters.Add(new Setter(ForegroundProperty, Brush("#236451")));
        style.Triggers.Add(selected);
        var focused = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(BorderBrushProperty, Brush("#153F34")));
        style.Triggers.Add(focused);
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(OpacityProperty, .45));
        style.Triggers.Add(disabled);
        return style;
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private static void OnSelectedTemplateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var picker = (PaperTemplatePicker)sender;
        picker.UpdateSelection();
        picker.SelectionChanged?.Invoke(picker, EventArgs.Empty);
    }

    private void UpdateSelection()
    {
        foreach (var (template, choice) in _choices)
        {
            choice.IsChecked = template == SelectedTemplate;
            _selectionMarks[template].Visibility = choice.IsChecked == true ? Visibility.Visible : Visibility.Hidden;
        }
    }
}
