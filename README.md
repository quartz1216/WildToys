# WildToys (WinUI 3)

All-in-one Windows desktop utility — WinUI 3 rebuild of [WildToys](../WildToys).

This is a ground-up rewrite on **Windows App SDK (WinUI 3)**. UI is built fresh in
WinUI; the hard-won Win32 / global-hook / detection logic is ported over from the
original WPF app rather than reimplemented.

## Structure

```
WildToys.slnx
src/
  WildToys.Core/   class library (net10.0-windows, WinForms) — modules, hooks, Win32, settings
  WildToys.App/    WinUI 3 app — windows, settings UI, tray
```

## Modules

| Module | Description |
|---|---|
| MouseWarp | Warp cursor to the center of the newly activated window on Alt+Tab |
| Power Switcher | Fast, minimalist Alt+Tab window switcher (formerly "BWS") |
| LumaEdges | Screen-edge hotkey zones triggered by mouse clicks |
| MouseGesture | Per-process mouse-gesture to hotkey mapping |

## Build

```
dotnet build src/WildToys.App/WildToys.App.csproj -c Debug -p:Platform=x64
```
