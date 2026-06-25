using System;
using Microsoft.UI.Dispatching;

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
    private DispatcherQueue? _dispatcher;
    private bool _running;

    public void Start()
    {
        if (_running) return;
        _running = true;

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _window = new SwitcherWindow();

        WindowManager.InitializeMruTracking();
        WorkspaceManager.Initialize();

        _hook = new KeyboardHook
        {
            IsSwitcherActive = () => _window?.IsSwitcherActive == true,
        };

        // Hook callbacks run in an input-synchronous context where cross-process
        // COM calls (virtual desktops) are forbidden (RPC_E_CANTCALLOUT_ININPUTSYNCCALL),
        // so marshal the work onto the UI message loop, which runs outside that context.
        _hook.AltTabOpen += (_, sticky) => Post(() => _window?.ShowSwitcher(sticky));
        _hook.AltReleased += (_, _) => Post(() => { if (_window?.IsSwitcherActive == true) _window.CommitSelection(); });
        _hook.EnterPressed += (_, _) => Post(() => { if (_window?.IsSwitcherActive == true) _window.CommitSelection(true); });
        _hook.EscPressed += (_, _) => Post(() => { if (_window?.IsSwitcherActive == true) _window.HideSwitcher(); });
        _hook.QPressed += (_, _) => Post(() => { if (_window?.IsSwitcherActive == true) _window.CloseSelection(); });
        _hook.DirectionKeyPressed += (_, dir) => Post(() => { if (_window?.IsSwitcherActive == true) _window.MoveSelection(dir); });
    }

    private void Post(DispatcherQueueHandler action) => _dispatcher?.TryEnqueue(action);

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
