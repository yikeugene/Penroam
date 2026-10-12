using System.Windows;
using System.Windows.Controls;
using Moye.Models;

namespace Moye.Controls;

public sealed class PracticeAttemptsDialog : Window
{
    private readonly ListBox _attempts = new() { Height = 160 };
    private readonly TextBox _reflection;
    private readonly CheckBox _reviewed = new() { Content = "Reviewed — I have checked this solution", MinHeight = 44 };
    public NotePage? SelectedPage => (_attempts.SelectedItem as ListBoxItem)?.Tag as NotePage;
    private readonly Dictionary<string, (string Reflection, bool Reviewed)> _changes = [];
    private string? _editingId;
    public IReadOnlyDictionary<string, (string Reflection, bool Reviewed)> Changes
    {
        get { KeepEditing(); return _changes; }
    }
    private void KeepEditing()
    {
        if (_editingId is not null) _changes[_editingId] = (_reflection.Text, _reviewed.IsChecked == true);
    }
    public string Action { get; private set; } = "";
    public string Reflection => _reflection.Text;
    public bool Reviewed => _reviewed.IsChecked == true;

    public PracticeAttemptsDialog(Window owner, IEnumerable<NotePage> pages, string? selectedId)
    {
        var panel = OrganizationDialogUi.Initialize(this, owner, "Practice attempts", 650);
        panel.Children.Add(new TextBlock { Text = "Keep your working for each attempt. Record what went wrong and what you would change next time.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        foreach (var page in pages.Where(p => p.Practice is not null).OrderBy(p => p.Practice!.GroupId).ThenBy(p => p.Practice!.Number))
        {
            var attempt = page.Practice!;
            _attempts.Items.Add(new ListBoxItem { MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center, Content = new TextBlock { Text = $"{(attempt.Reviewed ? "✓ " : "")}{attempt.Question} · Attempt {attempt.Number}", TextWrapping = TextWrapping.Wrap }, Tag = page });
        }
        panel.Children.Add(_attempts);
        _reflection = OrganizationDialogUi.Field(panel, "What went wrong / next approach", "");
        _reflection.MaxLength = 16000; _reflection.AcceptsReturn = true; _reflection.TextWrapping = TextWrapping.Wrap; _reflection.Height = 120; _reflection.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        panel.Children.Add(_reviewed);
        _attempts.SelectionChanged += (_, _) =>
        {
            KeepEditing();
            if (SelectedPage?.Practice is not { } attempt) return;
            _editingId = SelectedPage.Id;
            if (_changes.TryGetValue(_editingId, out var staged)) { _reflection.Text = staged.Reflection; _reviewed.IsChecked = staged.Reviewed; return; }
            _reflection.Text = attempt.Reflection; _reviewed.IsChecked = attempt.Reviewed;
        };
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (caption, action) in new[] { ("Save & open", "open"), ("Another attempt", "new"), ("Original page", "source") })
            buttons.Children.Add(OrganizationDialogUi.Button(this, caption, () => { if (SelectedPage is not null) { Action = action; DialogResult = true; } }));
        var cancel = OrganizationDialogUi.Button(this, "Close", () => DialogResult = false); cancel.IsCancel = true; buttons.Children.Add(cancel); panel.Children.Add(buttons);
        _attempts.SelectedItem = _attempts.Items.Cast<ListBoxItem>().FirstOrDefault(item => ((NotePage)item.Tag).Id == selectedId) ?? _attempts.Items.Cast<ListBoxItem>().FirstOrDefault();
    }
}
