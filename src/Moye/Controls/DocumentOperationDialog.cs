using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Moye.Services;

namespace Moye.Controls;

/// <summary>Cancellation is acknowledged before the dialog closes; completed commits remain successful.</summary>
public sealed class DocumentOperationDialog : Window
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 14) };
    private readonly ProgressBar _progress = new() { Height = 6, IsIndeterminate = true, Minimum = 0, Maximum = 100 };
    private readonly Button _cancel = new() { Content = "Cancel", MinWidth = 100, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Right };
    private bool _finished, _canCancel = true;
    private Exception? _error;
    private bool _succeeded;
    private DocumentOperationDialog(Window owner, string title)
    {
        Owner = owner; Title = title; Width = 470; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold });
        _message.Text = title; AutomationProperties.SetLiveSetting(_message, System.Windows.Automation.AutomationLiveSetting.Polite);
        panel.Children.Add(_message); panel.Children.Add(_progress); _cancel.Margin = new Thickness(0, 20, 0, 0); panel.Children.Add(_cancel); Content = panel;
        _cancel.Style = TryFindResource("SecondaryButton") as Style; _cancel.Click += (_, _) => Cancel();
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { Cancel(); e.Handled = true; } };
        Closing += OnClosing;
    }
    public static Task<bool> RunAsync(Window owner, string title, Func<CancellationToken, IProgress<DocumentProgress>, Task> operation)
    {
        var dialog = new DocumentOperationDialog(owner, title);
        dialog.Loaded += async (_, _) =>
        {
            try
            {
                var progress = new Progress<DocumentProgress>(update =>
                {
                    if (dialog._finished) return;
                    dialog._message.Text = update.Message; dialog._progress.IsIndeterminate = update.Total <= 0;
                    dialog._progress.Value = update.Percent; dialog._canCancel = update.CanCancel;
                    dialog._cancel.IsEnabled = update.CanCancel && !dialog._cancellation.IsCancellationRequested;
                });
                await operation(dialog._cancellation.Token, progress); dialog._succeeded = true;
            }
            catch (OperationCanceledException) when (dialog._cancellation.IsCancellationRequested) { }
            catch (Exception ex) { dialog._error = ex; }
            finally { dialog._finished = true; dialog.Close(); }
        };
        dialog.ShowDialog(); dialog._cancellation.Dispose();
        return dialog._error is null ? Task.FromResult(dialog._succeeded) : Task.FromException<bool>(dialog._error);
    }
    private void Cancel()
    {
        if (_finished || !_canCancel) return;
        _cancel.IsEnabled = false; _message.Text = "Canceling… Please wait for the current step to stop."; _cancellation.Cancel();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_finished) return;
        e.Cancel = true; Cancel();
    }
}
