namespace Moye.Models;

public enum StudyQuestionStatus { NeedHelp = 0, Understood = 1, TryAgain = 2, Resolved = 3 }

/// <summary>A fixed region in page DIP coordinates. Null means the entire page.</summary>
public sealed record StudyAnchor
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public sealed record StudyQuestion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Prompt { get; set; } = "";
    public string Explanation { get; set; } = "";
    public string RetryNotes { get; set; } = "";
    public StudyQuestionStatus Status { get; set; }
    public StudyAnchor? Anchor { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record ResumeHint
{
    public string NextStep { get; set; } = "";
    public StudyAnchor? Anchor { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed partial class NotePage
{
    public List<StudyQuestion> StudyQuestions { get; set; } = [];
    public ResumeHint? ResumeHint { get; set; }
}
