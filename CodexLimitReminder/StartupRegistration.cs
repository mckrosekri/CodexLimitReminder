using Microsoft.Win32;
using System.Text;

namespace CodexLimitReminder;

internal static class StartupRegistration
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "CodexLimitReminder";
    internal const string StartupScriptName = "CodexLimitReminder.vbs";

    public static string BuildCommand(string executablePath) => $"\"{Path.GetFullPath(executablePath)}\" --background";

    public static string BuildStartupScript(string executablePath)
    {
        string escapedCommand = BuildCommand(executablePath).Replace("\"", "\"\"");
        return "Option Explicit\r\n" +
               "Dim shell\r\n" +
               "Set shell = CreateObject(\"WScript.Shell\")\r\n" +
               $"shell.Run \"{escapedCommand}\", 0, False\r\n";
    }

    public static string GetStartupScriptPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        StartupScriptName);

    public static void Apply(bool enabled, string executablePath)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            key.SetValue(ValueName, BuildCommand(executablePath), RegistryValueKind.String);
            string startupScriptPath = GetStartupScriptPath();
            Directory.CreateDirectory(Path.GetDirectoryName(startupScriptPath)!);
            File.WriteAllText(startupScriptPath, BuildStartupScript(executablePath), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            File.Delete(GetStartupScriptPath());
        }
    }
}
