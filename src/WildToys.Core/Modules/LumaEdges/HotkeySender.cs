using System.Runtime.InteropServices;

namespace WildToys.Modules.LumaEdges;

/// <summary>
/// Synthesizes keystrokes via SendInput from hotkey strings like "Ctrl+Alt+Up".
/// Shared by LumaEdges and MouseGesture.
/// </summary>
public static class HotkeySender
{
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfExtendedkey = 0x0001;

    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkShift = 0x10;
    private const ushort VkLWin = 0x5B;
    private const uint MapvkVkToVsc = 0;

    public static bool Send(string? hotkey)
    {
        return SendDetailed(hotkey).Succeeded;
    }

    public static HotkeySendResult SendDetailed(string? hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return HotkeySendResult.Failed("Hotkey is empty.");
        }

        if (!TryParse(hotkey, out var modifiers, out var key))
        {
            return HotkeySendResult.Failed($"Unsupported hotkey: {hotkey}");
        }

        var inputs = new List<INPUT>((modifiers.Count * 2) + 2);

        foreach (var modifier in modifiers)
        {
            inputs.Add(CreateKeyboardInput(modifier, keyUp: false));
        }

        if (key != 0)
        {
            inputs.Add(CreateKeyboardInput(key, keyUp: false));
            inputs.Add(CreateKeyboardInput(key, keyUp: true));
        }

        for (var i = modifiers.Count - 1; i >= 0; i--)
        {
            inputs.Add(CreateKeyboardInput(modifiers[i], keyUp: true));
        }

        try
        {
            var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
            var errorCode = sent == inputs.Count ? 0 : Marshal.GetLastWin32Error();
            return new HotkeySendResult(
                sent == inputs.Count,
                sent,
                inputs.Count,
                errorCode,
                sent == inputs.Count ? "SendInput succeeded." : $"SendInput sent {sent}/{inputs.Count}. Win32Error={errorCode}");
        }
        catch (Exception ex)
        {
            return HotkeySendResult.Failed(ex.Message);
        }
    }

    private static bool TryParse(string hotkey, out List<ushort> modifiers, out ushort key)
    {
        modifiers = [];
        key = 0;

        var tokens = hotkey.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        foreach (var token in tokens)
        {
            if (TryMapModifier(token, out var modifier))
            {
                modifiers.Add(modifier);
                continue;
            }

            if (key != 0 || !TryMapKey(token, out key))
            {
                return false;
            }
        }

        return key != 0 || modifiers.Count > 0;
    }

    private static bool TryMapModifier(string token, out ushort key)
    {
        key = token.Trim().ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => VkControl,
            "ALT" => VkMenu,
            "SHIFT" => VkShift,
            "WIN" or "WINDOWS" => VkLWin,
            _ => 0
        };

        return key != 0;
    }

    private static bool TryMapKey(string token, out ushort key)
    {
        key = 0;
        var normalized = token.Trim().ToUpperInvariant();

        if (normalized.Length == 1)
        {
            var c = normalized[0];
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                key = c;
                return true;
            }
        }

        if (normalized.Length is 2 or 3 && normalized[0] == 'F' && int.TryParse(normalized[1..], out var functionKey) && functionKey is >= 1 and <= 24)
        {
            key = (ushort)(0x70 + functionKey - 1);
            return true;
        }

        key = normalized switch
        {
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "TAB" => 0x09,
            "ESC" or "ESCAPE" => 0x1B,
            "ENTER" or "RETURN" => 0x0D,
            "SPACE" => 0x20,
            _ => 0
        };

        return key != 0;
    }

    private static INPUT CreateKeyboardInput(ushort virtualKey, bool keyUp)
    {
        var flags = keyUp ? KeyeventfKeyup : 0;
        if (IsExtendedKey(virtualKey))
        {
            flags |= KeyeventfExtendedkey;
        }

        return new INPUT
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KEYBDINPUT
                {
                    VirtualKey = virtualKey,
                    ScanCode = (ushort)MapVirtualKey(virtualKey, MapvkVkToVsc),
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = nint.Zero
                }
            }
        };
    }

    private static bool IsExtendedKey(ushort virtualKey)
    {
        return virtualKey is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or VkLWin;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT Mouse;

        [FieldOffset(0)]
        public KEYBDINPUT Keyboard;

        [FieldOffset(0)]
        public HARDWAREINPUT Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint Message;
        public ushort ParamL;
        public ushort ParamH;
    }
}

public readonly record struct HotkeySendResult(
    bool Succeeded,
    uint SentInputs,
    int ExpectedInputs,
    int ErrorCode,
    string Message)
{
    public static HotkeySendResult Failed(string message)
    {
        return new HotkeySendResult(false, 0, 0, 0, message);
    }
}
