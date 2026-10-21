using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Moye.Services;
using Moye.ViewModels;
using L = Moye.Localization.LocalizationService;

namespace Moye.Controls;

public sealed class StudyInboxDialog : Window
{
    private readonly MainViewModel _model;
    private readonly bool _resumeOnly;
    private readonly ListBox _items = new() { BorderThickness = new Thickness(0), Margin = new Thickness(0, 12, 0, 8) };
    private readonly TextBox _query = new() { MinHeight = 44, Margin = new Thickness(0, 8, 0, 6) };
    private readonly CheckBox _resolved = new() { Content = L.Text("Controls.StudyInbox.IncludeResolved"), MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    public StudyInboxItem? Result => _items.SelectedItem as StudyInboxItem;

    public StudyInboxDialog(Window owner, MainViewModel model, bool resumeOnly = false)
    {
        _model = model; _resumeOnly = resumeOnly;
        var root = StudyDialogUi.Initialize(this, owner, resumeOnly ? L.Text("Controls.StudyInbox.ResumeTitle") : L.Text("Controls.StudyInbox.QuestionTitle"), 760, 640);
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top);
        heading.Children.Add(new TextBlock { Text = resumeOnly ? L.Text("Controls.StudyInbox.ResumeIntro") : L.Text("Controls.StudyInbox.QuestionIntro"), TextWrapping = TextWrapping.Wrap });
        AutomationProperties.SetName(_query, resumeOnly ? L.Text("Controls.StudyInbox.FilterResume") : L.Text("Controls.StudyInbox.FilterQuestions")); heading.Children.Add(_query);
        if (!resumeOnly) heading.Children.Add(_resolved);
        heading.Children.Add(_status); root.Children.Add(heading);
        var actions = StudyDialogUi.Actions(root);
        var close = OrganizationDialogUi.Button(this, L.Text("Controls.Common.Close"), () => DialogResult = false); close.IsCancel = true;
        var edit = OrganizationDialogUi.Button(this, L.Text("Controls.StudyInbox.Edit"), EditSelected);
        var remove = OrganizationDialogUi.Button(this, L.Text("Controls.StudyInbox.Remove"), RemoveSelected);
        var open = OrganizationDialogUi.Button(this, L.Text("Controls.StudyInbox.Open"), () => { if (Result is not null) DialogResult = true; });
        actions.Children.Add(close); actions.Children.Add(remove); actions.Children.Add(edit); actions.Children.Add(open);
        void Selected() { edit.IsEnabled = remove.IsEnabled = open.IsEnabled = Result is not null; }
        _items.SelectionChanged += (_, _) => Selected(); Selected();
        _items.MouseDoubleClick += (_, e) =>
        {
            if (Result is not null && ItemsControl.ContainerFromElement(_items, e.OriginalSource as DependencyObject) is ListBoxItem) DialogResult = true;
        };
        _items.KeyDown += (_, e) => { if (e.Key == Key.Enter && Result is not null) { DialogResult = true; e.Handled = true; } };
        var panel = new FrameworkElementFactory(typeof(StackPanel)); panel.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 10, 8, 10));
        void Label(string property, bool bold)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new Binding(property));
            text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); text.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 0));
            if (bold) text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); panel.AppendChild(text);
        }
        Label(nameof(StudyInboxItem.Location), false); Label(nameof(StudyInboxItem.Heading), true); Label(nameof(StudyInboxItem.Detail), false);
        _items.ItemTemplate = new DataTemplate { VisualTree = panel };
        ScrollViewer.SetHorizontalScrollBarVisibility(_items, ScrollBarVisibility.Disabled); ScrollViewer.SetVerticalScrollBarVisibility(_items, ScrollBarVisibility.Auto);
        AutomationProperties.SetName(_items, resumeOnly ? L.Text("Controls.StudyInbox.ResumeTitle") : L.Text("Controls.StudyInbox.Items")); root.Children.Add(_items);
        _query.TextChanged += (_, _) => Refresh(); _resolved.Checked += (_, _) => Refresh(); _resolved.Unchecked += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); _query.Focus(); };
    }

    private void Refresh(string? identity = null)
    {
        identity ??= Result?.Identity;
        var items = _model.Document is { } document ? StudyWorkflowService.Items(document, _resumeOnly, _resolved.IsChecked == true, _query.Text) : [];
        _items.ItemsSource = items;
        _items.SelectedItem = items.FirstOrDefault(item => item.Identity == identity);
        _status.Text = items.Count > 0 ? L.Text("Controls.StudyInbox.Count", items.Count, L.Text(_resumeOnly ? "Controls.StudyInbox.ResumeCountLabel" : "Controls.StudyInbox.QuestionCountLabel")) :
            _query.Text.Trim().Length > 0 ? L.Text("Controls.StudyInbox.NoMatches") :
            _resumeOnly ? L.Text("Controls.StudyInbox.NoResume") : L.Text("Controls.StudyInbox.NoQuestions");
    }

    private void EditSelected()
    {
        if (Result is not { } item) return;
        if (item.Question is { } question)
        {
            var dialog = new StudyQuestionDialog(this, question);
            if (dialog.ShowDialog() == true) _model.UpdateStudyQuestion(item.PageId, dialog.Question);
        }
        else if (item.Resume is { } hint)
        {
            var dialog = new StudyResumeDialog(this, hint);
            if (dialog.ShowDialog() == true) _model.SetResumeHint(item.PageId, dialog.Remove ? null : dialog.Hint);
        }
        Refresh(item.Identity);
    }

    private void RemoveSelected()
    {
        if (Result is not { } item) return;
        var message = _resumeOnly ?
            L.Text("Controls.StudyInbox.RemoveResume") :
            L.Text("Controls.StudyInbox.RemoveQuestion");
        if (MessageBox.Show(this, message, L.Text("Controls.StudyInbox.RemoveTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (item.Question is { } question) _model.RemoveStudyQuestion(item.PageId, question.Id);
        else _model.SetResumeHint(item.PageId, null);
        Refresh();
    }
}
