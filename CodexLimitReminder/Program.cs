using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CodexLimitReminder;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        const string mutexName = @"Local\CodexLimitReminder.SingleInstance";
        using var mutex = new Mutex(initiallyOwned: true, mutexName, out bool ownsMutex);
        LaunchCommand command = LaunchCommand.Parse(args);

        if (!ownsMutex)
        {
            nint existing = NativeMethods.FindWindowEx(0, 0, NativeMethods.MessageWindowClass, null);
            if (existing != 0)
            {
                NativeMethods.PostMessage(existing, NativeMethods.WmExternalCommand, (nuint)command.Kind, 0);
            }

            return 0;
        }

        try
        {
            string executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("The application executable path could not be determined.");
            ActivityLog.Write($"Primary process started ({command.Kind}) from {executablePath}.");
            StartupRegistration.Apply(enabled: true, executablePath);
            ActivityLog.Write("Repaired HKCU Run and Startup-folder launch entries.");
            using var app = new TrayApplication(command);
            return app.Run();
        }
        catch (Exception exception)
        {
            ActivityLog.Write($"Fatal startup error: {exception}");
            NativeMethods.MessageBox(0, exception.ToString(), "Codex Limit Reminder could not start", NativeMethods.MbIconError);
            return 1;
        }
    }
}

internal enum LaunchCommandKind
{
    Background = 0,
    ShowSettings = 1,
    TestSummary = 2
}

internal readonly record struct LaunchCommand(LaunchCommandKind Kind)
{
    public static LaunchCommand Parse(string[] args)
    {
        if (args.Any(value => value.Equals("--test-summary", StringComparison.OrdinalIgnoreCase) ||
                              value.Equals("--test-day-6", StringComparison.OrdinalIgnoreCase) ||
                              value.Equals("--test-day-7", StringComparison.OrdinalIgnoreCase) ||
                              value.Equals("--test-usage-95", StringComparison.OrdinalIgnoreCase)))
        {
            return new(LaunchCommandKind.TestSummary);
        }

        if (args.Any(value => value.Equals("--show-settings", StringComparison.OrdinalIgnoreCase)))
        {
            return new(LaunchCommandKind.ShowSettings);
        }

        return new(LaunchCommandKind.Background);
    }
}
