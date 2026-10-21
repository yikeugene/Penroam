using System.IO;
using L = Moye.Localization.LocalizationService;

namespace Moye.Services;

/// <summary>Preserves the inner cause of dependency and storage failures.</summary>
public sealed class ErrorReporter(string logPath)
{
    public string Report(Exception exception)
    {
        var message = Describe(exception);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);
            File.AppendAllText(logPath, $"{DateTimeOffset.Now:O} {exception}\n\n");
            return L.Text("Messages.Error.DetailsSaved", message, logPath);
        }
        catch { return message; } // Reporting must not replace the original failure.
    }

    public static string Describe(Exception exception)
    {
        for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
        {
            if (cause.HResult == unchecked((int)0x800711C7))
                return L.Text("Messages.Error.WindowsBlocked", cause.Message);
        }
        var root = exception.GetBaseException();
        var message = exception.Message;
        // Page-specific failures already include the underlying explanation after their context.
        // Keep that context without repeating the same explanation in the dialog.
        var includesRootMessage = string.Equals(message, root.Message, StringComparison.Ordinal) ||
            message.EndsWith(": " + root.Message, StringComparison.Ordinal);
        return ReferenceEquals(root, exception) || includesRootMessage
            ? message
            : message + "\n\n" + root.Message;
    }
}
