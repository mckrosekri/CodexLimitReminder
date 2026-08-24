using System.Reflection;

namespace CodexLimitReminder;

internal static class ActivityLog
{
    private const long MaximumBytes = 256 * 1024;
    private static readonly object Sync = new();

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexLimitReminder",
        "activity.log");

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length >= MaximumBytes)
                {
                    File.Move(FilePath, FilePath + ".previous", overwrite: true);
                }

                string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
                File.AppendAllText(
                    FilePath,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} [v{version}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never prevent the reminder from running.
        }
    }
}
