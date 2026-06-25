namespace WildToys;

/// <summary>One mouse-gesture binding.</summary>
public class GestureEntry
{
    /// <summary>Comma-separated 4-direction stroke sequence, e.g. "Right,Left,Up".</summary>
    public string Gesture { get; set; } = "";

    /// <summary>Which mouse button performs the gesture: "Right" or "Middle".</summary>
    public string Button { get; set; } = "Right";

    /// <summary>Hotkey to send when matched, in HotkeySender notation, e.g. "Ctrl+Alt+Left".</summary>
    public string Action { get; set; } = "";

    /// <summary>Process name this applies to (no extension). Empty = global (all apps).</summary>
    public string Process { get; set; } = "";
}
