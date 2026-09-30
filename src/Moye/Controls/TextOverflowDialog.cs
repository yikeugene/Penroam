using System.Windows;
using System.Windows.Controls;
using Moye.Models;

namespace Moye.Controls;

public sealed class TextOverflowDialog : Window
{
    public TextFlow.TextOverflow? JumpTo { get; private set; }
    public bool ContinueExport { get; private set; }
    public TextOverflowDialog(Window owner, NotebookDocument snapshot, IReadOnlyList<TextFlow.TextOverflow> issues)
    {
        Owner = owner; Title = "Check text before exporting"; Width = 640; Height = 460;
        MinWidth = 480; MinHeight = 360; MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 24);
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        var panel = new DockPanel { Margin = new Thickness(24) }; Content = panel;
        var intro = new TextBlock { Text = $"{issues.Count} text {(issues.Count == 1 ? "box extends" : "boxes extend")} beyond its visible area. The PDF only includes the visible text. Go to a box and use Continue on next page, or export its current appearance.", TextWrapping = TextWrapping.Wrap, FontSize = 15, Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(intro, Dock.Top); panel.Children.Add(intro);
        var list = new ListBox { ItemsSource = issues.Select(issue => new IssueChoice(issue, Label(snapshot, issue))).ToList(), DisplayMemberPath = nameof(IssueChoice.Label), SelectedIndex = 0 };
        var itemStyle = new Style(typeof(ListBoxItem)); itemStyle.Setters.Add(new Setter(MinHeightProperty, 44d)); list.ItemContainerStyle = itemStyle;
        var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) }; DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        var go = new Button { Content = "Go to selected text", MinHeight = 44, IsDefault = true, Style = TryFindResource("PrimaryButton") as Style };
        go.Click += (_, _) => { JumpTo = (list.SelectedItem as IssueChoice)?.Issue; DialogResult = true; }; actions.Children.Add(go);
        var export = new Button { Content = "Export visible text", MinHeight = 44, Margin = new Thickness(8, 0, 0, 0), Style = TryFindResource("SecondaryButton") as Style };
        export.Click += (_, _) => { ContinueExport = true; DialogResult = true; }; actions.Children.Add(export);
        actions.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinHeight = 44, Margin = new Thickness(8, 0, 0, 0), Style = TryFindResource("SecondaryButton") as Style });
        panel.Children.Add(list);
    }
    private static string Label(NotebookDocument document, TextFlow.TextOverflow issue)
    {
        var text = document.Pages.First(page => page.Id == issue.PageId).Texts.First(item => item.Id == issue.TextId).Text.ReplaceLineEndings(" ");
        return $"Export page {issue.PageNumber} · {(text.Length > 65 ? text[..65] + "…" : text)}";
    }
    private sealed record IssueChoice(TextFlow.TextOverflow Issue, string Label);
}
