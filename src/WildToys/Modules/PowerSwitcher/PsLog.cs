using System;
using System.IO;

namespace WildToys;

/// <summary>Lightweight file logger for diagnosing the Power Switcher.</summary>
internal static class PsLog
{
    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "wildtoys-ps.log");

    public static void Write(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch { }
    }
}
