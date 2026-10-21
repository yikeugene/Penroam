using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Moye.Models;
using Moye.Services;
using L = Moye.Localization.LocalizationService;

namespace Moye.Controls;

public sealed class RegionHistoryDialog : Window
{
    private readonly ComboBox _versions = new() { MinHeight = 44 };
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _use;
    private readonly IReadOnlyList<NotePage> _pages;
    private readonly PagePreviewRenderer _renderer;
    private int _revision;
    public NotePage? Result => _versions.SelectedIndex < 0 ? null : _pages[_versions.SelectedIndex];

    public RegionHistoryDialog(Window owner, IReadOnlyList<NotePage> pages, PagePreviewRenderer renderer)
    {
        Owner = owner?.IsLoaded == true ? owner : null; Title = L.Text("Controls.RegionHistory.Title"); Width = 660; Height = 650;
        MinWidth = 450; MinHeight = 400; MaxHeight = SystemParameters.WorkArea.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false; Background = Brushes.White; _pages = pages; _renderer = renderer;
        var root = new DockPanel { Margin = new Thickness(24) }; var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top);
        heading.Children.Add(new TextBlock { Text = Title, FontSize = 24, TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(new TextBlock { Text = L.Text("Controls.RegionHistory.Intro"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 12) });
        for (var i = 0; i < pages.Count; i++) _versions.Items.Add(L.Text("Controls.RegionHistory.Version", i + 1, pages[i].Texts.Count, pages[i].Images.Count));
        heading.Children.Add(_versions); heading.Children.Add(_status); root.Children.Add(heading);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var cancel = OrganizationDialogUi.Button(this, L.Text("Controls.Common.Cancel"), () => DialogResult = false); cancel.IsCancel = true; buttons.Children.Add(cancel);
        _use = OrganizationDialogUi.Button(this, L.Text("Controls.RegionHistory.ChooseArea"), () => { if (Result is not null) DialogResult = true; }); _use.IsEnabled = false;
        buttons.Children.Add(_use); root.Children.Add(buttons); root.Children.Add(_preview); Content = root;
        _versions.SelectionChanged += async (_, _) =>
        {
            var revision = ++_revision; _use.IsEnabled = false; _status.Text = L.Text("Controls.RegionHistory.Loading");
            try
            {
                if (Result is not { } page) return;
                var bitmap = await _renderer.RenderAsync(page, 600);
                if (revision != _revision) return; _preview.Source = bitmap; _status.Text = ""; _use.IsEnabled = true;
            }
            catch (Exception ex) { if (revision == _revision) _status.Text = L.Text("Controls.Common.PreviewUnavailable", ex.Message); }
        };
        Loaded += (_, _) => _versions.SelectedIndex = 0; Closed += (_, _) => _revision++;
    }
}
