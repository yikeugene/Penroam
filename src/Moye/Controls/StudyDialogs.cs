using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Moye.Models;
using Moye.Services;

namespace Moye.Controls;

public sealed class StudyQuestionDialog : Window
{
    private readonly StudyQuestion _original;
    private readonly TextBox _prompt, _explanation, _retry;
    private readonly ComboBox _status;
    public StudyQuestion Question => _original with
    {
        Prompt = _prompt.Text.Trim(), Explanation = _explanation.Text.Trim(), RetryNotes = _retry.Text.Trim(),
        Status = ((ComboBoxItem)_status.SelectedItem).Tag is StudyQuestionStatus status ? status : StudyQuestionStatus.NeedHelp
    };

    public StudyQuestionDialog(Window owner, StudyQuestion question, bool creating = false)
    {
        _original = StudyWorkflowService.Copy(question);
        var root = StudyDialogUi.Initialize(this, owner, creating ? "Add a question" : "Question and next attempt", 640, 660);
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = question.Anchor is null ? "Linked to this page. Reopen its source from the question inbox." : "Linked to the marked region of this page. Reopen its source from the question inbox.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        _prompt = StudyDialogUi.Field(content, "What do you want to understand?", question.Prompt, StudyWorkflowService.MaximumPromptLength, 76);
        content.Children.Add(new Label { Content = "Progress" });
        _status = new ComboBox { MinHeight = 44, Margin = new Thickness(0, 0, 0, 10) };
        AutomationProperties.SetName(_status, "Question progress");
        foreach (var value in Enum.GetValues<StudyQuestionStatus>())
        {
            var item = new ComboBoxItem { Content = StudyWorkflowService.StatusLabel(value), Tag = value, MinHeight = 36 };
            _status.Items.Add(item); if (value == question.Status) _status.SelectedItem = item;
        }
        content.Children.Add(_status);
        _explanation = StudyDialogUi.Field(content, "What helped / explanation", question.Explanation, StudyWorkflowService.MaximumNotesLength, 96);
        _retry = StudyDialogUi.Field(content, "Next attempt / what to check", question.RetryNotes, StudyWorkflowService.MaximumNotesLength, 96);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive); content.Children.Add(error);
        var actions = StudyDialogUi.Actions(root);
        var cancel = OrganizationDialogUi.Button(this, "Cancel", () => DialogResult = false); cancel.IsCancel = true;
        var save = OrganizationDialogUi.Button(this, creating ? "Add question" : "Save", () =>
        {
            if (string.IsNullOrWhiteSpace(_prompt.Text)) { error.Text = "Write a question before saving."; _prompt.Focus(); return; }
            DialogResult = true;
        });
        save.IsDefault = true; actions.Children.Add(cancel); actions.Children.Add(save);
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Loaded += (_, _) => _prompt.Focus();
    }
}

public sealed class StudyResumeDialog : Window
{
    private readonly ResumeHint _original;
    private readonly TextBox _next;
    public bool Remove { get; private set; }
    public ResumeHint Hint => _original with { NextStep = _next.Text.Trim() };
    public StudyResumeDialog(Window owner, ResumeHint? hint, StudyAnchor? anchor = null)
    {
        _original = hint is null ? new ResumeHint() : StudyWorkflowService.Copy(hint);
        if (anchor is not null) _original.Anchor = anchor with {};
        var root = StudyDialogUi.Initialize(this, owner, "Next time, continue here", 600, 440);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Leave the next small step while it is still clear. This note stays with the page and appears in Resume notes.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        _next = StudyDialogUi.Field(panel, "What should you do when you return?", hint?.NextStep ?? "", StudyWorkflowService.MaximumResumeLength, 150);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
        var actions = StudyDialogUi.Actions(root);
        if (hint is not null) actions.Children.Add(OrganizationDialogUi.Button(this, "Clear note", () => { Remove = true; DialogResult = true; }));
        var cancel = OrganizationDialogUi.Button(this, "Cancel", () => DialogResult = false); cancel.IsCancel = true; actions.Children.Add(cancel);
        var save = OrganizationDialogUi.Button(this, "Save", () =>
        {
            if (string.IsNullOrWhiteSpace(_next.Text)) { error.Text = "Write a next step before saving."; _next.Focus(); return; }
            DialogResult = true;
        });
        save.IsDefault = true; actions.Children.Add(save);
        root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Loaded += (_, _) => { _next.Focus(); _next.CaretIndex = _next.Text.Length; };
    }
}

internal static class StudyDialogUi
{
    public static DockPanel Initialize(Window window, Window owner, string title, double width, double height)
    {
        window.Owner = owner?.IsLoaded == true ? owner : null; window.Title = title; window.Width = width; window.Height = height;
        window.MinWidth = 480; window.MinHeight = 350; window.MaxHeight = SystemParameters.WorkArea.Height;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner; window.ShowInTaskbar = false; window.Background = Brushes.White;
        var root = new DockPanel { Margin = new Thickness(24) };
        var heading = new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading); window.Content = root; return root;
    }
    public static StackPanel Actions(DockPanel root)
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions); return actions;
    }
    public static TextBox Field(Panel panel, string label, string value, int maxLength, double height)
    {
        var input = new TextBox { Text = value, MaxLength = maxLength, Height = height, MinHeight = 44, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 4, 0, 10) };
        AutomationProperties.SetName(input, label); panel.Children.Add(new Label { Content = label, Target = input }); panel.Children.Add(input); return input;
    }
}
