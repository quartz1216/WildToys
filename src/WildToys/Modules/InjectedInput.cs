namespace WildToys.Modules;

/// <summary>
/// Shared tag stamped on mouse input the app synthesizes itself (via mouse_event /
/// SendInput). Our own low-level hooks recognize this value in the event's
/// dwExtraInfo and pass it straight through instead of re-processing it — which
/// otherwise lets one module's re-emitted click trigger another module's hook.
/// </summary>
internal static class InjectedInput
{
    public static readonly nuint SelfTag = 0x57544759; // "WTGY"
}
