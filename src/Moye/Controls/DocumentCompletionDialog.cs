using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using L = Moye.Localization.LocalizationService;

namespace Moye.Controls;

public sealed class DocumentCompletionDialog : Window
{
    public DocumentCompletionDialog(Window owner, string path, int pages)
    {
        Owner = owner; Title = L.Text("Controls.DocumentCompletion.Title"); Width = 480; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = L.Text("Controls.DocumentCompletion.Exported", pages, L.Text(pages == 1 ? "Controls.Common.PageSingular" : "Controls.Common.PagePlural")), FontSize = 22, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = Path.GetFileName(path), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8) });
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
        var buttons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) }; panel.Children.Add(buttons);
        void Add(string text, Action action)
        {
            var button = new Button { Content = text, MinHeight = 44, Margin = new Thickness(0, 0, 8, 0), Style = TryFindResource("SecondaryButton") as Style };
            button.Click += (_, _) => { try { action(); } catch (Exception ex) { error.Text = ex.Message; } }; buttons.Children.Add(button);
        }
        Add(L.Text("Controls.DocumentCompletion.Open"), () => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }));
        Add(L.Text("Controls.DocumentCompletion.ShowFolder"), () => Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, Arguments = "/select,\"" + Path.GetFullPath(path) + "\"" }));
        var done = new Button { Content = L.Text("Controls.Common.Done"), IsDefault = true, IsCancel = true, MinHeight = 44, Style = TryFindResource("PrimaryButton") as Style };
        done.Click += (_, _) => DialogResult = true; buttons.Children.Add(done);
    }
}
