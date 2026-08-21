using System.Diagnostics;
using Microsoft.Win32;

namespace CodexLimitReminder;

internal static class CodexExecutableLocator
{
    private const string PackageRegistryPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    public static string Find()
    {
        string? overridden = Environment.GetEnvironmentVariable("CODEX_LIMIT_REMINDER_CODEX_EXE");
        if (IsExecutable(overridden))
        {
            return Path.GetFullPath(overridden!);
        }

        foreach (string candidate in FindInstalledCandidates())
        {
            if (IsExecutable(candidate))
            {
                return candidate;
            }
        }

        foreach (string candidate in FindRunningCodexExecutables())
        {
            if (IsExecutable(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Codex is installed, but its local app-server executable could not be found. Open or update Codex, then refresh.");
    }

    private static IEnumerable<string> FindRunningCodexExecutables()
    {
        foreach (Process process in Process.GetProcessesByName("codex"))
        {
            using (process)
            {
                string? path = null;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch
                {
                    // Another process can exit or deny module inspection between enumeration and access.
                }

                if (!string.IsNullOrWhiteSpace(path))
                {
                    yield return path;
                }
            }
        }
    }

    private static IEnumerable<string> FindInstalledCandidates()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Path.Combine(
            appData,
            "npm",
            "node_modules",
            "@openai",
            "codex",
            "node_modules",
            "@openai",
            "codex-win32-x64",
            "vendor",
            "x86_64-pc-windows-msvc",
            "bin",
            "codex.exe");

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localAppData, "Programs", "Codex", "resources", "codex.exe");

        using RegistryKey? packages = Registry.CurrentUser.OpenSubKey(PackageRegistryPath);
        if (packages is null)
        {
            yield break;
        }

        foreach (string packageName in packages.GetSubKeyNames()
                     .Where(name => name.StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(name => name, StringComparer.OrdinalIgnoreCase))
        {
            using RegistryKey? package = packages.OpenSubKey(packageName);
            if (package?.GetValue("PackageRootFolder") is string root)
            {
                yield return Path.Combine(root, "app", "resources", "codex.exe");
            }
        }
    }

    private static bool IsExecutable(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Path.GetFileName(path).Equals("codex.exe", StringComparison.OrdinalIgnoreCase) &&
        File.Exists(path);
}
