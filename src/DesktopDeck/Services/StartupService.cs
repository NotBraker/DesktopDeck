using Microsoft.Win32;

namespace DesktopDeck.Services;

public sealed class StartupService
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesktopDeck";
    private static string StartupCommand
    {
        get
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The current executable path is unavailable.");
            if (string.Equals(System.IO.Path.GetFileName(executable), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Run DesktopDeck.exe directly to configure start at login.");
            return "\"" + executable + "\"";
        }
    }

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            return string.Equals(key?.GetValue(ValueName) as string, StartupCommand, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            key.SetValue(ValueName, StartupCommand, RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
