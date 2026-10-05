using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Moye.Controls;
using Moye.Services;

namespace Moye;

public partial class MainWindow
{
    private async void ExtendPageClick(object sender, RoutedEventArgs e)
    {
        if (!CanOrganize || ViewModel.IsLibraryVisible) return;
        if (sender is MenuItem { Tag: PageActionTarget target } && !ActivatePageAction(target)) return;
        CommitEditors(); ClearTouches(); CloseSettingsPopups();
        if (ViewModel.Document is not { } document || ViewModel.SelectedPage is not { } selected) return;
        var page = selected.Page;
        BitmapSource? preview = null;
        await RunAsync("Preparing page preview…", async () =>
            preview = await new PagePreviewRenderer(ViewModel.Pdf, ViewModel.Repository.GetAssetAsync).RenderAsync(page, 600));
        if (preview is null || _closing || !ReferenceEquals(ViewModel.Document, document)
            || !document.Pages.Any(candidate => ReferenceEquals(candidate, page))) return;
        var dialog = new ExtendPageDialog(page, preview) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            if (!ViewModel.ExtendPage(page.Id, dialog.Margins)) return;
            ScrollToSelected();
            if (_fitWidthActive) FitWidth();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, "The page could not be extended. Your original page is unchanged.\n\n" + exception.Message,
                "Extend page", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
