namespace WildToys;

/// <summary>Persisted application + per-module settings.</summary>
public class AppSettings
{
    public bool StartWithWindows { get; set; }
    public bool StartAsAdmin { get; set; }

    // Module enable flags
    public bool IsMouseWarpEnabled { get; set; } = true;
    public bool IsPowerSwitcherEnabled { get; set; } = true;
    public bool IsLumaEdgesEnabled { get; set; } = true;
    public bool IsMouseGestureEnabled { get; set; } = true;
}
