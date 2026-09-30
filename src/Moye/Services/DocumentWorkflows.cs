using System.Globalization;
using System.Security.Cryptography;
using Moye.Models;

namespace Moye.Services;

public sealed record DocumentProgress(string Message, int Completed = 0, int Total = 0, bool CanCancel = true)
{
    public double Percent => Total > 0 ? Math.Clamp(100d * Completed / Total, 0, 100) : 0;
}

/// <summary>Selection and placement are independent of UI virtualization and never mutate source pages.</summary>
public static class DocumentWorkflows
{
    public static IReadOnlyList<int> ParsePageRange(string value, int count)
    {
        if (count < 1) throw new ArgumentException("This document has no pages.");
        if (string.IsNullOrWhiteSpace(value)) throw new FormatException("Enter page numbers, for example 1–5, 8, 12.");
        var selected = new SortedSet<int>();
        foreach (var part in value.Replace('–', '-').Replace('—', '-').Split(','))
        {
            var limits = part.Trim().Split('-');
            if (limits.Length is < 1 or > 2 || !int.TryParse(limits[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var first))
                throw new FormatException("Use page numbers and ascending ranges, for example 1–5, 8, 12.");
            var last = first;
            if (limits.Length == 2 && !int.TryParse(limits[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out last))
                throw new FormatException("Enter a page number on both sides of a range.");
            if (first < 1 || last > count || last < first)
                throw new FormatException($"Choose pages from 1 to {count}; ranges must be in ascending order.");
            for (var page = first; page <= last; page++) selected.Add(page - 1);
        }
        return selected.ToArray();
    }

    public static NotebookDocument ExportSnapshot(NotebookDocument source, IEnumerable<string> pageIds, string? title = null)
    {
        var ids = pageIds.ToHashSet(StringComparer.Ordinal);
        var result = source.Snapshot();
        result.Title = title ?? source.Title;
        result.Pages = result.Pages.Where(page => ids.Contains(page.Id)).ToList();
        foreach (var page in result.Pages) page.InkData = page.InkData.ToArray();
        var sections = result.Pages.Select(page => page.SectionId).ToHashSet(StringComparer.Ordinal);
        result.Sections = result.Sections.Where(section => sections.Contains(section.Id)).ToList();
        return result;
    }

    public static IReadOnlyList<NotePage> InsertPages(NotebookDocument document, IReadOnlyList<NotePage> source,
        string sectionId, string? afterPageId = null)
    {
        if (!document.Sections.Any(section => section.Id == sectionId)) throw new ArgumentException("Choose an existing section.");
        var copies = source.Select(page => page.Snapshot()).ToArray();
        foreach (var page in copies)
        {
            page.Id = Guid.NewGuid().ToString("N"); page.SectionId = sectionId; page.InkData = page.InkData.ToArray();
            foreach (var text in page.Texts) text.Id = Guid.NewGuid().ToString("N");
            foreach (var image in page.Images) image.Id = Guid.NewGuid().ToString("N");
        }
        var index = afterPageId is null ? -1 : document.Pages.FindIndex(page => page.Id == afterPageId && page.SectionId == sectionId);
        if (index < 0) index = document.Pages.FindLastIndex(page => page.SectionId == sectionId);
        if (index < 0)
        {
            var following = document.Sections.SkipWhile(section => section.Id != sectionId).Skip(1).Select(section => section.Id).ToHashSet();
            var next = document.Pages.FindIndex(page => following.Contains(page.SectionId));
            index = (next < 0 ? document.Pages.Count : next) - 1;
        }
        document.Pages.InsertRange(index + 1, copies);
        return copies;
    }
}

/// <summary>Unaccepted imports stay outside the library, including their original PDF asset.</summary>
public sealed class PreparedDocumentImport : IDisposable
{
    private readonly ImportAssetStore _assets = new();
    public IReadOnlyList<NotePage> Pages { get; private set; } = [];
    public IPdfService Pdf { get; }
    public Func<string, Task<AssetData>> LoadAsset => _assets.GetAssetAsync;
    public PreparedDocumentImport() => Pdf = new PdfService(_assets);
    public async Task PrepareAsync(string path, IProgress<DocumentProgress>? progress, CancellationToken cancellationToken)
    {
        var service = new DocumentImportService(Pdf, new OfficePdfConverter());
        Pages = await service.ImportAsync(path, progress, cancellationToken);
    }
    public async Task CommitAssetsAsync(INotebookRepository repository, IEnumerable<NotePage> pages,
        IProgress<DocumentProgress>? progress, CancellationToken cancellationToken)
    {
        var ids = pages.SelectMany(page => page.Images.Select(image => image.AssetId)
            .Concat(page.Pdf is null ? [] : new[] { page.Pdf.AssetId })).Distinct().ToArray();
        for (var i = 0; i < ids.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new("Saving imported attachments…", i, ids.Length));
            var asset = await _assets.GetAssetAsync(ids[i]);
            var stored = await repository.PutAssetAsync(asset.FileName, asset.ContentType, asset.Bytes);
            if (stored.Id != asset.Id) throw new InvalidDataException("The imported attachment could not be verified.");
        }
    }
    public void Dispose() => _assets.Dispose();

    private sealed class ImportAssetStore : INotebookRepository
    {
        private readonly Dictionary<string, AssetData> _values = new(StringComparer.Ordinal);
        public Task<AssetData> PutAssetAsync(string fileName, string contentType, byte[] bytes)
        {
            var asset = new AssetData(Convert.ToHexStringLower(SHA256.HashData(bytes)), fileName, contentType, bytes.ToArray());
            _values[asset.Id] = asset; return Task.FromResult(asset);
        }
        public Task<AssetData> GetAssetAsync(string id) => Task.FromResult(_values.TryGetValue(id, out var asset)
            ? asset : throw new FileNotFoundException("The import preview attachment is no longer available."));
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<NotebookSummary>> ListAsync() => Task.FromResult<IReadOnlyList<NotebookSummary>>([]);
        public Task<NotebookDocument?> LoadAsync(string id) => Task.FromResult<NotebookDocument?>(null);
        public Task SaveAsync(NotebookDocument document) => throw new NotSupportedException();
        public Task DeleteAsync(string id) => throw new NotSupportedException();
        public void Dispose() => _values.Clear();
    }
}
