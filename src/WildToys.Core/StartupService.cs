using System.Diagnostics;
using Microsoft.Win32;

namespace WildToys;

/// <summary>Registers/unregisters the app in the per-user Windows startup (Run) key.</summary>
public static class StartupService
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "WildToys";

    public static void SetRunAtStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (key is null || string.IsNullOrEmpty(exePath))
                return;

            if (enable)
                key.SetValue(AppName, $"\"{exePath}\"");
            else
                key.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch
        {
            // Ignore registry failures (e.g. insufficient permissions).
        }
    }
}
