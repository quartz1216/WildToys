using System;
using System.IO;

namespace WildToys;

/// <summary>
/// Appends unhandled exceptions to %AppData%\WildToys\crash.log so a crash that
/// surfaces as a native stowed exception (e.g. combase E_POINTER from a hook
/// callback) can be traced back to the managed exception + stack that caused it.
/// </summary>
public static class CrashLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WildToys", "crash.log");

    public static void Write(string source, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath,
                $"[{DateTimeOffset.Now:O}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}");
        }
        catch
        {
            // Never let logging itself throw.
        }
    }
}
