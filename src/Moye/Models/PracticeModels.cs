namespace Moye.Models;

public sealed partial class NotePage
{
    public PracticeAttempt? Practice { get; set; }
}

public sealed record PracticeAttempt
{
    public string GroupId { get; set; } = Guid.NewGuid().ToString("N");
    public string SourcePageId { get; set; } = "";
    public string QuestionAssetId { get; set; } = "";
    public string Question { get; set; } = "";
    public double QuestionWidth { get; set; }
    public double QuestionHeight { get; set; }
    public int Number { get; set; } = 1;
    public string Reflection { get; set; } = "";
    public bool Reviewed { get; set; }
}
