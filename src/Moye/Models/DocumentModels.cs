using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace Moye.Models;

// Numeric values are persisted in existing notebooks and .moye backups.
public enum PaperTemplate { Plain = 0, Ruled = 1, Grid = 2, DotGrid = 3, Cornell = 4, Graph = 5 }
public enum InkTool { Pen, Highlighter, StrokeEraser, PointEraser, Lasso, Text, Select, Hand }
public enum NoteTextAlignment { Left = 0, Center = 1, Right = 2 }

public static class NoteTextLayout
{
    // WPF TextBoxView reserves this margin for its bidi caret even when TextBox.Padding is zero.
    public const double HorizontalInset = 2;
    public static double ContentWidth(double boxWidth) => Math.Max(1, boxWidth - 2 * HorizontalInset);
}

public sealed class NotebookDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Untitled Notebook";
    public string Folder { get; set; } = "My Notes";
    public bool IsPinned { get; set; }
    public string CoverColor { get; set; } = "";
    public bool IsQuickInbox { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<NoteSection> Sections { get; set; } = [];
    public List<NotePage> Pages { get; set; } = [];
    public NotebookDocument Snapshot() => new() { Id = Id, Title = Title, Folder = Folder, IsPinned = IsPinned, CoverColor = CoverColor, IsQuickInbox = IsQuickInbox, CreatedUtc = CreatedUtc, ModifiedUtc = ModifiedUtc, Sections = Sections.Select(s => s with {}).ToList(), Pages = Pages.Select(p => p.Snapshot()).ToList() };
}

public sealed record NoteSection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "General";
}

/// <summary>Repairs legacy membership and keeps the flat export order grouped by section, without dropping pages.</summary>
public static class NotebookStructure
{
    public static void Normalize(NotebookDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Sections ??= [];
        document.Pages ??= [];
        var sections = new List<NoteSection>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var original in document.Sections)
        {
            if (original is null) continue;
            var section = original;
            if (string.IsNullOrWhiteSpace(section.Id) || !ids.Add(section.Id))
            {
                section = section with {};
                do { section.Id = Guid.NewGuid().ToString("N"); } while (!ids.Add(section.Id));
            }
            if (string.IsNullOrWhiteSpace(section.Title)) section.Title = "General";
            sections.Add(section);
        }
        NoteSection? general = sections.FirstOrDefault(s => s.Title.Equals("General", StringComparison.OrdinalIgnoreCase));
        if (sections.Count == 0 || document.Pages.Any(page => !ids.Contains(page.SectionId ?? "")))
        {
            if (general is null)
            {
                // Stable legacy identity also makes saving an unchanged, unnormalized snapshot idempotent.
                var legacyId = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("moye:general:" + document.Id)).AsSpan(0, 16));
                while (!ids.Add(legacyId)) legacyId = Guid.NewGuid().ToString("N");
                general = new NoteSection { Id = legacyId };
                sections.Add(general);
            }
            foreach (var page in document.Pages)
                if (!ids.Contains(page.SectionId ?? "")) page.SectionId = general.Id;
        }
        document.Sections = sections;
        var order = sections.Select((section, index) => (section.Id, index)).ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
        document.Pages = document.Pages.OrderBy(page => order[page.SectionId]).ToList();
    }
}

public sealed class NotebookSummary
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Folder { get; set; } = "";
    public bool IsPinned { get; set; }
    public string CoverColor { get; set; } = "";
    public bool IsQuickInbox { get; set; }
    public string DisplayCoverColor => NotebookAppearance.ColorFor(Id, CoverColor);
    public string PinLabel => IsPinned ? "Pinned" : "";
    public DateTimeOffset ModifiedUtc { get; set; }
    public int PageCount { get; set; }
    public string PageCountText => $"{PageCount} {(PageCount == 1 ? "page" : "pages")}";
}

public sealed class NotePage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SectionId { get; set; } = "";
    public string Title { get; set; } = "";
    public bool IsBookmarked { get; set; }
    public double Width { get; set; } = 793.700787;
    public double Height { get; set; } = 1122.519685;
    public PaperTemplate Template { get; set; }
    public PaperPageLayout? PaperLayout { get; set; }
    // Byte arrays are immutable snapshots. Replace rather than mutate them.
    public byte[] InkData { get; set; } = [];
    public List<NoteText> Texts { get; set; } = [];
    public List<NoteImage> Images { get; set; } = [];
    public PdfPageSource? Pdf { get; set; }
    public NotePage Snapshot() => new() { Id = Id, SectionId = SectionId, Title = Title, IsBookmarked = IsBookmarked, Width = Width, Height = Height, Template = Template, PaperLayout = PaperLayout is null ? null : PaperLayout with {}, InkData = InkData, Texts = Texts.Select(t => t with {}).ToList(), Images = Images.Select(i => i with {}).ToList(), Pdf = Pdf is null ? null : Pdf with {} };
}

/// <summary>Original paper-guide region on an extended canvas. A null layout fills the page.</summary>
public sealed record PaperPageLayout
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public static class PaperPagePlacement
{
    public static bool IsValid(NotePage page)
    {
        if (page.PaperLayout is not { } paper) return true;
        return double.IsFinite(page.Width) && double.IsFinite(page.Height) && page.Width > 0 && page.Height > 0
            && double.IsFinite(paper.X) && double.IsFinite(paper.Y) && paper.X >= 0 && paper.Y >= 0
            && double.IsFinite(paper.Width) && double.IsFinite(paper.Height) && paper.Width > 0 && paper.Height > 0
            && paper.X + paper.Width <= page.Width + 0.000001
            && paper.Y + paper.Height <= page.Height + 0.000001;
    }
}

public static class NotebookAppearance
{
    public static IReadOnlyList<string> Colors { get; } = ["#3B6656", "#536980", "#8B624B", "#726482", "#85633D", "#566B70"];
    public static bool IsValidColor(string? color) => color is { Length: 7 } && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit);
    public static string ColorFor(string id, string? customColor)
    {
        if (IsValidColor(customColor)) return customColor!;
        // Stable across sessions, filtering and framework hash randomization.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(id));
        return Colors[hash[0] % Colors.Count];
    }
}

public sealed record NoteText
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public double X { get; set; } = 72;
    public double Y { get; set; } = 72;
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 130;
    public string Text { get; set; } = "";
    public string FontFamily { get; set; } = "Microsoft JhengHei";
    public double FontSize { get; set; } = 22;
    // Additive format fields: older documents remain regular, upright, and left aligned.
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public NoteTextAlignment Alignment { get; set; } = NoteTextAlignment.Left;
    public string Color { get; set; } = "#FF25334A";
}

public sealed record NoteImage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AssetId { get; set; } = "";
    public double X { get; set; } = 72;
    public double Y { get; set; } = 72;
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 240;
}

public sealed record PdfPageSource
{
    public string AssetId { get; set; } = "";
    public int PageIndex { get; set; }
    public int Rotation { get; set; }
    public double CropX { get; set; }
    public double CropY { get; set; }
    public double CropWidth { get; set; }
    public double CropHeight { get; set; }
    // Placement on the note canvas, in DIP. All zero keeps legacy PDFs filling their page.
    // Extending a page materializes the original display size before changing the canvas.
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double DisplayWidth { get; set; }
    public double DisplayHeight { get; set; }
}

public static class PdfPagePlacement
{
    public static bool IsValid(NotePage page)
    {
        if (page.Pdf is not { } pdf) return true;
        if (!double.IsFinite(page.Width) || !double.IsFinite(page.Height) || page.Width <= 0 || page.Height <= 0
            || !double.IsFinite(pdf.OffsetX) || !double.IsFinite(pdf.OffsetY) || pdf.OffsetX < 0 || pdf.OffsetY < 0
            || !double.IsFinite(pdf.DisplayWidth) || !double.IsFinite(pdf.DisplayHeight)) return false;
        if (pdf.DisplayWidth == 0 && pdf.DisplayHeight == 0) return pdf.OffsetX == 0 && pdf.OffsetY == 0;
        return pdf.DisplayWidth > 0 && pdf.DisplayHeight > 0
            && pdf.OffsetX + pdf.DisplayWidth <= page.Width + 0.000001
            && pdf.OffsetY + pdf.DisplayHeight <= page.Height + 0.000001;
    }
}

public sealed record AssetData(string Id, string FileName, string ContentType, byte[] Bytes);

public static class DocumentJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
}
