using System.Collections.Generic;

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
    public bool IsImIndicatorEnabled { get; set; } = true;

    // IM Indicator: how long (ms) the input-language badge stays on screen.
    public int ImIndicatorDurationMs { get; set; } = 1500;

    // MouseWarp: optionally animate the cursor to the window center (and how long).
    public bool MouseWarpAnimationEnabled { get; set; } = true;
    public int MouseWarpAnimationDurationMs { get; set; } = 180;

    // Power Switcher: dim and/or blur the desktop behind the switcher while it's open.
    public bool PowerSwitcherDimEnabled { get; set; } = true;
    public int PowerSwitcherDimAmount { get; set; } = 35; // darkness percent, 0-100 (tints blur too)
    public bool PowerSwitcherBlurEnabled { get; set; }

    // Delay (ms) before the switcher window appears; a quick Alt+Tab tap switches
    // without flashing the UI. The fade-in animates the backdrop + switcher when shown.
    public int PowerSwitcherShowDelayMs { get; set; } = 50;
    public bool PowerSwitcherFadeIn { get; set; } = true;

    // LumaEdges: edge-zone hit thickness (px) and per-button zone -> hotkey maps.
    // Zone keys are the HotZone enum names ("Top", "TopLeft", ...). An empty/absent
    // value means the click passes through normally.
    public int LumaEdgesThickness { get; set; } = 2;

    public Dictionary<string, string> LumaEdgesLeftZones { get; set; } = new();

    public Dictionary<string, string> LumaEdgesRightZones { get; set; } = new()
    {
        { "Top", "Ctrl+Alt+Up" },
        { "Bottom", "Ctrl+Alt+Down" },
        { "Left", "Alt+Left" },
        { "Right", "Alt+Right" },
        { "TopLeft", "Win+Tab" },
        { "TopRight", "Alt+F4" },
        { "BottomLeft", "Ctrl+Esc" },
        { "BottomRight", "Win+D" }
    };

    public Dictionary<string, string> LumaEdgesMiddleZones { get; set; } = new();

    // LumaEdges hover (hot-corner) triggers: fire a hotkey just by moving the cursor
    // into a zone, no click. Edge-triggered with a dwell delay (ms) before firing.
    public bool LumaEdgesHoverEnabled { get; set; } = true;
    public int LumaEdgesHoverDelayMs { get; set; } = 200;
    public Dictionary<string, string> LumaEdgesHoverZones { get; set; } = new();
}
