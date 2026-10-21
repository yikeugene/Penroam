using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Moye.Services;
using L = Moye.Localization.LocalizationService;

namespace Moye.Controls;

public sealed class FolderImportSummaryDialog : Window
{
    public bool ImportSuccessfulFiles { get; private set; }

    public FolderImportSummaryDialog(Window owner, FolderImportSummary summary)
    {
        Owner = owner; Title = L.Text("Controls.FolderImport.Title"); Width = 660; Height = 560;
        MinWidth = 440; MinHeight = 380; MaxHeight = Math.Max(380, SystemParameters.WorkArea.Height - 24);
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        var panel = new DockPanel { Margin = new Thickness(24) }; Content = panel;
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        header.Children.Add(new TextBlock { Text = summary.NotebookTitle, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(new TextBlock
        {
            Text = L.Text("Controls.FolderImport.Summary", summary.ReadyCount, summary.FailedCount, summary.SkippedCount, summary.PageCount, L.Text(summary.PageCount == 1 ? "Controls.Common.PageSingular" : "Controls.Common.PagePlural")),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 8)
        });
        header.Children.Add(new TextBlock
        {
            Text = summary.RequiresConfirmation
                ? L.Text("Controls.FolderImport.Confirmation")
                : summary.ReadyCount == 0
                    ? L.Text("Controls.FolderImport.Empty")
                    : L.Text("Controls.FolderImport.Order"),
            TextWrapping = TextWrapping.Wrap
        });
        header.Children.Add(new TextBlock
        {
            Text = L.Text("Controls.FolderImport.Scope"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0)
        });
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        if (summary.RequiresConfirmation)
        {
            var cancel = ActionButton(L.Text("Controls.Common.Cancel"), false); cancel.IsCancel = true; actions.Children.Add(cancel);
            var accept = ActionButton(L.Text("Controls.FolderImport.Accept"), true); accept.IsDefault = true;
            accept.Click += (_, _) => { ImportSuccessfulFiles = true; DialogResult = true; };
            actions.Children.Add(accept);
        }
        else
        {
            var done = ActionButton(summary.ReadyCount == 0 ? L.Text("Controls.Common.Close") : L.Text("Controls.Common.Done"), true);
            done.IsDefault = true; done.IsCancel = true;
            done.Click += (_, _) => DialogResult = true; actions.Children.Add(done);
        }
        var list = new ListBox
        {
            ItemsSource = summary.Items.Select(item => new SummaryRow(item)).ToArray(),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(list, L.Text("Controls.FolderImport.Results"));
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        var rowStyle = new Style(typeof(ListBoxItem));
        rowStyle.Setters.Add(new Setter(MinHeightProperty, 44d));
        rowStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
        rowStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        rowStyle.Setters.Add(new Setter(AutomationProperties.NameProperty, new Binding(nameof(SummaryRow.AccessibleName))));
        list.ItemContainerStyle = rowStyle;
        var row = new FrameworkElementFactory(typeof(StackPanel));
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(SummaryRow.Name)));
        name.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); row.AppendChild(name);
        var detail = new FrameworkElementFactory(typeof(TextBlock));
        detail.SetBinding(TextBlock.TextProperty, new Binding(nameof(SummaryRow.Detail)));
        detail.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        detail.SetValue(MarginProperty, new Thickness(0, 4, 0, 0)); row.AppendChild(detail);
        list.ItemTemplate = new DataTemplate { VisualTree = row };
        panel.Children.Add(list);
    }

    private Button ActionButton(string text, bool primary)
    {
        var button = new Button
        {
            Content = text, MinHeight = 44, MinWidth = 100, Margin = new Thickness(8, 0, 0, 0),
            Style = TryFindResource(primary ? "PrimaryButton" : "SecondaryButton") as Style
        };
        AutomationProperties.SetName(button, text);
        return button;
    }

    private sealed class SummaryRow(FolderImportItem item)
    {
        public string Name => item.Name;
        public string Detail => (item.Status == FolderImportItemStatus.Ready
            ? L.Text("Controls.FolderImport.ReadyCount", item.PageCount, L.Text(item.PageCount == 1 ? "Controls.Common.PageSingular" : "Controls.Common.PagePlural")) : item.Status switch
            {
                FolderImportItemStatus.Failed => L.Text("Controls.FolderImport.Failed"),
                FolderImportItemStatus.Skipped => L.Text("Controls.FolderImport.Skipped"),
                _ => item.Status.ToString()
            })
            + (string.IsNullOrWhiteSpace(item.Message) ? "" : "\n" + item.Message);
        public string AccessibleName => Name + ". " + Detail;
    }
}
