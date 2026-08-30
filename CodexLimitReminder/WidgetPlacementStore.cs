using Microsoft.Win32;

namespace CodexLimitReminder;

internal readonly record struct WidgetPlacement(int X, int Y);

internal static class WidgetPlacementStore
{
    private const string SettingsKeyPath = @"Software\CodexLimitReminder";

    internal static WidgetPlacement? Load()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath);
        return key?.GetValue("WidgetX") is int x && key.GetValue("WidgetY") is int y
            ? new WidgetPlacement(x, y)
            : null;
    }

    internal static void Save(int x, int y)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath);
        key.SetValue("WidgetX", x, RegistryValueKind.DWord);
        key.SetValue("WidgetY", y, RegistryValueKind.DWord);
    }
}
