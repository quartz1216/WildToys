using System;
using System.Collections.Generic;
using System.Linq;
using VirtualDesktop; // MScholtes namespace

namespace WildToys.Modules.PowerSwitcher
{
    public class WorkspaceItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsCurrent { get; set; }
    }

    public static class WorkspaceManager
    {
        private static List<Guid> _mruDesktops = new();

        private static string? GetDesktopNameFromRegistry(Guid desktopId)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    $@"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops\Desktops\{desktopId:B}");
                
                if (key != null)
                {
                    var name = key.GetValue("Name") as string;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }
            }
            catch {}
            return null;
        }

        public static Guid? GetDesktopIdForWindow(IntPtr hWnd)
        {
            try
            {
                var d = Desktop.FromWindow(hWnd);
                if (d != null) return d.Id;
            }
            catch {}
            return null;
        }

        public static void Initialize()
        {
            try
            {
                // Disable native Windows 11 slide animation
                Desktop.SetAnimation(false);

                Desktop? currentDesk = null;
                try { currentDesk = Desktop.Current; } 
                catch { try { currentDesk = Desktop.FromWindow(Win32Interop.GetForegroundWindow()); } catch {} }
                
                if (currentDesk == null) currentDesk = Desktop.FromIndex(0); // Fallback
                
                _mruDesktops.Add(currentDesk.Id);
                
                for (int i = 0; i < Desktop.Count; i++)
                {
                    var d = Desktop.FromIndex(i);
                    if (d != null && d.Id != currentDesk.Id)
                    {
                        _mruDesktops.Add(d.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WorkspaceManager failed to initialize: {ex.Message}");
            }
        }

        public static void Shutdown()
        {
            // No events to unhook in MScholtes implementation
        }

        public static List<WorkspaceItem> GetWorkspacesInMruOrder()
        {
            var result = new List<WorkspaceItem>();

            try
            {
                Desktop? currentDesk = null;
                try { currentDesk = Desktop.Current; } 
                catch { try { currentDesk = Desktop.FromWindow(Win32Interop.GetForegroundWindow()); } catch {} }

                if (currentDesk == null) currentDesk = Desktop.FromIndex(0);

                // Just-In-Time MRU update: Always ensure Current is at the front
                if (currentDesk != null)
                {
                    _mruDesktops.Remove(currentDesk.Id);
                    _mruDesktops.Insert(0, currentDesk.Id);
                }

                // Collect all known live desktops
                var liveIds = new HashSet<Guid>();
                for (int i = 0; i < Desktop.Count; i++)
                {
                    var d = Desktop.FromIndex(i);
                    if (d != null)
                    {
                        liveIds.Add(d.Id);
                        if (!_mruDesktops.Contains(d.Id))
                        {
                            _mruDesktops.Add(d.Id);
                        }
                    }
                }

                // Remove deleted desktops
                _mruDesktops.RemoveAll(id => !liveIds.Contains(id));

                // Process in MRU order
                for (int i = 0; i < _mruDesktops.Count; i++)
                {
                    var id = _mruDesktops[i];
                    Desktop? desktop = null;
                    int index = -1;

                    for (int j = 0; j < Desktop.Count; j++)
                    {
                        var d = Desktop.FromIndex(j);
                        if (d != null && d.Id == id)
                        {
                            desktop = d;
                            index = j;
                            break;
                        }
                    }

                    if (desktop != null && currentDesk != null)
                    {
                        result.Add(new WorkspaceItem
                        {
                            Id = desktop.Id,
                            Name = GetDesktopNameFromRegistry(desktop.Id) ?? desktop.Name ?? $"Desktop {index + 1}",
                            IsCurrent = desktop.Id == currentDesk.Id
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get workspaces: {ex.Message}");
            }

            return result;
        }

        public static void SwitchToWorkspace(Guid id)
        {
            try
            {
                // Use direct COM SwitchDesktop instead of MakeVisible.
                // MakeVisible manipulates the taskbar focus (AttachThreadInput + SetForegroundWindow + SW_MINIMIZE)
                // which causes pinned/all-desktop windows to steal focus from the intended target.
                // Direct SwitchDesktop avoids this and lets the caller set focus immediately after.
                var desktopId = id;
                var ivd = DesktopManager.VirtualDesktopManagerInternal.FindDesktop(ref desktopId);
                DesktopManager.VirtualDesktopManagerInternal.SwitchDesktop(ivd);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to switch workspace: {ex.Message}");
            }
        }
    }
}



