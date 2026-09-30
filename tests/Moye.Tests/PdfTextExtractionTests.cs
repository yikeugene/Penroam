using Moye.Models;
using Moye.Services;

namespace Moye.Tests;

public sealed class PdfTextExtractionTests
{
    [Fact]
    public async Task ExtractsExactOriginalPageAndCachesSharedPdfWithoutPretendingBlankPagesHaveOcr()
    {
        using var repository = new WorkflowRepository();
        var asset = await repository.PutAssetAsync("reference.pdf", "application/pdf", WorkflowRepository.CreatePdf());
        var service = new PdfTextExtractionService(repository);
        var second = await service.ExtractPageTextAsync(new NotePage { Pdf = new() { AssetId = asset.Id, PageIndex = 1 } });
        var first = await service.ExtractPageTextAsync(asset.Id, 0);
        var blank = await service.ExtractPageTextAsync(asset.Id, 2);
        Assert.Contains("Algebra beta", second.Text); Assert.DoesNotContain("Calculus", second.Text);
        Assert.Contains("Calculus alpha", first.Text); Assert.Null(first.Warning);
        Assert.False(blank.HasText); Assert.Contains("OCR", blank.Warning);
        Assert.Equal(1, repository.Reads);
    }

    [Fact]
    public async Task InvalidOrMissingPageReturnsCoverageWarningAndDoesNotThrowAwayTypedSearch()
    {
        using var repository = new WorkflowRepository();
        var asset = await repository.PutAssetAsync("bad.pdf", "application/pdf", [1, 2, 3]);
        var service = new PdfTextExtractionService(repository);
        var result = await service.ExtractPageTextAsync(asset.Id, 0);
        Assert.False(result.HasText); Assert.NotNull(result.Warning);
        var plain = await service.ExtractPageTextAsync(new NotePage { Texts = [new() { Text = "Typed note" }] });
        Assert.Empty(plain.Text); Assert.Null(plain.Warning);
    }

    [Fact]
    public async Task CancellationBeforeExtractionDoesNotReadTheAsset()
    {
        using var repository = new WorkflowRepository(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PdfTextExtractionService(repository).ExtractPageTextAsync("unused", 0, cancellation.Token));
        Assert.Equal(0, repository.Reads);
    }
}
