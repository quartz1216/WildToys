using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WildToys.Modules.PowerSwitcher;
using Drawing = System.Drawing;

namespace WildToys;

/// <summary>
/// Resolves a window's icon as a WinUI <see cref="ImageSource"/>. Tries the
/// modern Shell (UWP/AUMID) logo first, then Win32 icon handles, then the
/// executable's associated icon. Results are cached by window handle.
/// </summary>
public sealed class WindowIconService
{
    private readonly Dictionary<IntPtr, ImageSource?> _cache = new();

    public ImageSource? GetIcon(IntPtr hWnd)
    {
        if (_cache.TryGetValue(hWnd, out var cached))
            return cached;

        ImageSource? image = null;
        using (var bmp = GetBitmap(hWnd))
        {
            if (bmp != null)
                image = ToImageSource(bmp);
        }

        _cache[hWnd] = image;
        return image;
    }

    private static Bitmap? GetBitmap(IntPtr hWnd)
    {
        // 0. Modern Shell (UWP / packaged) logo via AUMID.
        try
        {
            string aumid = ResolveAumid(hWnd);
            if (!string.IsNullOrEmpty(aumid))
            {
                var appInfo = Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(aumid);
                var logoRef = appInfo?.DisplayInfo.GetLogo(new Windows.Foundation.Size(256, 256));
                if (logoRef != null)
                {
                    var task = logoRef.OpenReadAsync().AsTask();
                    task.Wait();
                    using var managed = task.Result.AsStreamForRead();
                    using var full = new Bitmap(managed);
                    // UWP logos carry large transparent padding; zoom into the
                    // center (~37.5%) to match the original's framing.
                    return CropCenter(full, 0.375);
                }
            }
        }
        catch { /* fall through to Win32 */ }

        IntPtr hIcon = Win32Interop.SendMessage(hWnd, Win32Interop.WM_GETICON, (IntPtr)Win32Interop.ICON_BIG, IntPtr.Zero);
        if (hIcon == IntPtr.Zero)
            hIcon = Win32Interop.SendMessage(hWnd, Win32Interop.WM_GETICON, (IntPtr)Win32Interop.ICON_SMALL, IntPtr.Zero);
        if (hIcon == IntPtr.Zero)
            hIcon = Win32Interop.GetClassLongSafe(hWnd, Win32Interop.GCLP_HICON);
        if (hIcon == IntPtr.Zero)
            hIcon = Win32Interop.GetClassLongSafe(hWnd, Win32Interop.GCLP_HICONSM);

        if (hIcon != IntPtr.Zero)
        {
            try
            {
                using var icon = Icon.FromHandle(hIcon);
                return new Bitmap(icon.ToBitmap());
            }
            catch { }
        }

        // Final fallback: the executable's associated icon.
        try
        {
            Win32Interop.GetWindowThreadProcessId(hWnd, out uint pid);
            IntPtr hProc = Win32Interop.OpenProcess(0x0400 | 0x0010, false, pid);
            if (hProc != IntPtr.Zero)
            {
                try
                {
                    var sb = new StringBuilder(1024);
                    int capacity = sb.Capacity;
                    if (Win32Interop.QueryFullProcessImageName(hProc, 0, sb, ref capacity))
                    {
                        using var exeIcon = Icon.ExtractAssociatedIcon(sb.ToString());
                        if (exeIcon != null)
                            return new Bitmap(exeIcon.ToBitmap());
                    }
                }
                finally
                {
                    Win32Interop.CloseHandle(hProc);
                }
            }
        }
        catch { }

        return null;
    }

    private static string ResolveAumid(IntPtr hWnd)
    {
        string aumid = Win32Interop.GetAppUserModelId(hWnd);
        if (!string.IsNullOrEmpty(aumid))
            return aumid;

        var sbClass = new StringBuilder(256);
        Win32Interop.GetClassName(hWnd, sbClass, sbClass.Capacity);
        if (sbClass.ToString() != "ApplicationFrameWindow")
            return string.Empty;

        // UWP host: try the hosted child process first.
        string childAumid = string.Empty;
        Win32Interop.EnumChildWindows(hWnd, (childHwnd, _) =>
        {
            Win32Interop.GetWindowThreadProcessId(childHwnd, out uint cPid);
            string pAumid = Win32Interop.GetAumidFromProcess(cPid);
            if (!string.IsNullOrEmpty(pAumid))
            {
                childAumid = pAumid;
                return false; // stop
            }
            return true;
        }, IntPtr.Zero);

        if (!string.IsNullOrEmpty(childAumid))
            return childAumid;

        // Cloaked UWP on another desktop: fall back to the host process.
        Win32Interop.GetWindowThreadProcessId(hWnd, out uint hostPid);
        return Win32Interop.GetAumidFromProcess(hostPid);
    }

    private static Bitmap CropCenter(Bitmap source, double fraction)
    {
        int cw = Math.Max(1, (int)(source.Width * fraction));
        int ch = Math.Max(1, (int)(source.Height * fraction));
        int x = (source.Width - cw) / 2;
        int y = (source.Height - ch) / 2;

        var cropped = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(cropped);
        g.DrawImage(source, new Rectangle(0, 0, cw, ch), new Rectangle(x, y, cw, ch), GraphicsUnit.Pixel);
        return cropped;
    }

    private static WriteableBitmap ToImageSource(Bitmap bmp)
    {
        int w = bmp.Width;
        int h = bmp.Height;

        var wb = new WriteableBitmap(w, h);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var buffer = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, buffer, y * w * 4, w * 4);

            // GDI Format32bppArgb is BGRA with straight alpha; WinUI expects
            // premultiplied BGRA8. Premultiply to avoid edge halos.
            for (int i = 0; i < buffer.Length; i += 4)
            {
                byte a = buffer[i + 3];
                if (a == 255) continue;
                buffer[i] = (byte)(buffer[i] * a / 255);
                buffer[i + 1] = (byte)(buffer[i + 1] * a / 255);
                buffer[i + 2] = (byte)(buffer[i + 2] * a / 255);
            }

            using var stream = wb.PixelBuffer.AsStream();
            stream.Write(buffer, 0, buffer.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        wb.Invalidate();
        return wb;
    }
}
