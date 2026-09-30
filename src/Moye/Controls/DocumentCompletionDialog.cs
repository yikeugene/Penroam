using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Moye.Controls;

public sealed class DocumentCompletionDialog : Window
{
    public DocumentCompletionDialog(Window owner, string path, int pages)
    {
        Owner = owner; Title = "PDF exported"; Width = 480; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = $"{pages} {(pages == 1 ? "page" : "pages")} exported", FontSize = 22, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = Path.GetFileName(path), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8) });
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
        var buttons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) }; panel.Children.Add(buttons);
        void Add(string text, Action action)
        {
            var button = new Button { Content = text, MinHeight = 44, Margin = new Thickness(0, 0, 8, 0), Style = TryFindResource("SecondaryButton") as Style };
            button.Click += (_, _) => { try { action(); } catch (Exception ex) { error.Text = ex.Message; } }; buttons.Children.Add(button);
        }
        Add("Open PDF", () => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }));
        Add("Show in folder", () => Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, Arguments = "/select,\"" + Path.GetFullPath(path) + "\"" }));
        var done = new Button { Content = "Done", IsDefault = true, IsCancel = true, MinHeight = 44, Style = TryFindResource("PrimaryButton") as Style };
        done.Click += (_, _) => DialogResult = true; buttons.Children.Add(done);
    }
}
