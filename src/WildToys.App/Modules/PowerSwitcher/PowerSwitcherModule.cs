using System;

namespace WildToys.Modules.PowerSwitcher;

/// <summary>
/// Drives the Power Switcher: owns the global keyboard hook and the overlay
/// window, wiring hook events to switcher actions. Must be started on the UI
/// thread (the hook is installed there and its events arrive there).
/// </summary>
public sealed class PowerSwitcherModule : IDisposable
{
    private KeyboardHook? _hook;
    private SwitcherWindow? _window;
    private bool _running;

    public void Start()
    {
        if (_running) return;
        _running = true;

        _window = new SwitcherWindow();

        WindowManager.InitializeMruTracking();
        WorkspaceManager.Initialize();

        _hook = new KeyboardHook
        {
            IsSwitcherActive = () => _window?.IsSwitcherActive == true,
        };
        _hook.AltTabOpen += (_, sticky) => _window?.ShowSwitcher(sticky);
        _hook.AltReleased += (_, _) => { if (_window?.IsSwitcherActive == true) _window.CommitSelection(); };
        _hook.EnterPressed += (_, _) => { if (_window?.IsSwitcherActive == true) _window.CommitSelection(true); };
        _hook.EscPressed += (_, _) => { if (_window?.IsSwitcherActive == true) _window.HideSwitcher(); };
        _hook.QPressed += (_, _) => { if (_window?.IsSwitcherActive == true) _window.CloseSelection(); };
        _hook.DirectionKeyPressed += (_, dir) => { if (_window?.IsSwitcherActive == true) _window.MoveSelection(dir); };
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;

        _hook?.Dispose();
        _hook = null;

        WindowManager.ShutdownMruTracking();
        WorkspaceManager.Shutdown();

        _window?.Close();
        _window = null;
    }

    public void Dispose() => Stop();
}
