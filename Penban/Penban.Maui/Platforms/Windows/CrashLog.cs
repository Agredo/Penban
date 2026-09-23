using System.Text;

namespace Penban.Maui.WinUI;

/// <summary>
/// Temporary: writes the unhandled exceptions of the Windows head to a file, because the crashes on
/// Windows come out of Microsoft.UI.Xaml.dll as a stowed exception without a managed stack.
/// </summary>
internal static class CrashLog
{
    private static readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "penban-crash.log");

    public static void Write(string source, Exception? exception)
    {
        try
        {
            var text = new StringBuilder();
            text.AppendLine($"=== {DateTime.Now:O} {source} ===");
            text.AppendLine(exception?.ToString() ?? "(no exception)");
            text.AppendLine();
            File.AppendAllText(Path, text.ToString());
        }
        catch
        {
            // A crash logger that throws is worse than no crash logger.
        }
    }
}
