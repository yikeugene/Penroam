using Moye.Models;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace Moye.Services;

public sealed record PdfTextResult(string Text, string? Warning = null)
{
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}

/// <summary>Local text extraction, never OCR. A bounded cache shares work for pages in the same original PDF.</summary>
public sealed class PdfTextExtractionService(INotebookRepository repository)
{
    private const int MaximumPageCharacters = 250_000;
    private const int MaximumCachedCharacters = 8_000_000;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, IReadOnlyList<PdfTextResult>> _cache = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private int _characters;

    public Task<PdfTextResult> ExtractPageTextAsync(NotePage page, CancellationToken cancellationToken = default) =>
        page.Pdf is { } pdf ? ExtractPageTextAsync(pdf.AssetId, pdf.PageIndex, cancellationToken) : Task.FromResult(new PdfTextResult(""));

    public async Task<PdfTextResult> ExtractPageTextAsync(string assetId, int pageIndex, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_cache.TryGetValue(assetId, out var pages))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var asset = await repository.GetAssetAsync(assetId).ConfigureAwait(false);
                    pages = await Task.Run(() => Extract(asset.Bytes, cancellationToken), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { return new("", "PDF text could not be read: " + ex.Message); }
                var characters = pages.Sum(page => page.Text.Length);
                while (_order.Count > 0 && (_cache.Count >= 8 || _characters + characters > MaximumCachedCharacters))
                {
                    var id = _order.Dequeue(); _characters -= _cache[id].Sum(page => page.Text.Length); _cache.Remove(id);
                }
                _cache[assetId] = pages; _order.Enqueue(assetId); _characters += characters;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return pageIndex >= 0 && pageIndex < pages.Count ? pages[pageIndex] : new("", "The original PDF page is unavailable.");
        }
        finally { _gate.Release(); }
    }

    private static IReadOnlyList<PdfTextResult> Extract(byte[] bytes, CancellationToken cancellationToken)
    {
        using var pdf = PigDocument.Open(bytes);
        var pages = new List<PdfTextResult>(pdf.NumberOfPages);
        var characters = 0;
        for (var number = 1; number <= pdf.NumberOfPages; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (characters >= MaximumCachedCharacters)
            {
                pages.Add(new("", "The PDF text limit was reached. This page was not indexed.")); continue;
            }
            try
            {
                var text = ContentOrderTextExtractor.GetText(pdf.GetPage(number));
                cancellationToken.ThrowIfCancellationRequested();
                var allowed = Math.Min(MaximumPageCharacters, MaximumCachedCharacters - characters);
                var truncated = text.Length > allowed;
                if (truncated) text = text[..allowed];
                characters += text.Length;
                pages.Add(new(text, truncated ? "Only the beginning of this very long PDF page was indexed."
                    : string.IsNullOrWhiteSpace(text) ? "No extractable PDF text. Scanned pages and handwriting require OCR, which is not enabled." : null));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { pages.Add(new("", $"PDF page {number} text could not be read: {ex.Message}")); }
        }
        return pages;
    }
}
