using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DualSenseUltimate.Core.Mapping;
using DualSenseUltimate.Core.Runtime;
using DualSenseUltimate.Core.Virtual;

namespace DualSenseUltimate.Platform;

/// <summary>Keyboard and mouse output through SendInput (no driver, no admin rights). Limitation: input
/// cannot reach windows of processes running elevated unless this app is elevated too (UIPI).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsInputInjector : IInputInjector
{
    public bool IsAvailable => true;

    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public nint dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nint dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public INPUTUNION u; }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);

    private const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2, KEYEVENTF_SCANCODE = 8;
    private const uint MOUSE_MOVE = 0x1, LDOWN = 0x2, LUP = 0x4, RDOWN = 0x8, RUP = 0x10, MDOWN = 0x20, MUP = 0x40, XDOWN = 0x80, XUP = 0x100, WHEEL = 0x800;
    private static readonly HashSet<ushort> Extended = [0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x5B, 0xA3, 0xA5, 0x2C];

    private static void Send(INPUT i) => SendInput(1, [i], Marshal.SizeOf<INPUT>());

    public void Key(string keyName, bool down)
    {
        if (!KeyNames.VirtualKeys.TryGetValue(keyName, out ushort vk)) return;
        uint scan = MapVirtualKey(vk, 0);
        uint flags = down ? 0 : KEYEVENTF_KEYUP;
        if (Extended.Contains(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        // Games read scan codes (DirectInput/RawInput); media keys have none, so fall back to the virtual key.
        var ki = scan != 0 ? new KEYBDINPUT { wScan = (ushort)scan, dwFlags = flags | KEYEVENTF_SCANCODE } : new KEYBDINPUT { wVk = vk, dwFlags = flags };
        Send(new INPUT { type = 1, u = new INPUTUNION { ki = ki } });
    }

    public void MouseMove(int dx, int dy) => Send(new INPUT { type = 0, u = new INPUTUNION { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = MOUSE_MOVE } } });

    public void MouseButton(MouseButtons button, bool down)
    {
        var (flag, data) = button switch
        {
            MouseButtons.Left => (down ? LDOWN : LUP, 0u), MouseButtons.Right => (down ? RDOWN : RUP, 0u), MouseButtons.Middle => (down ? MDOWN : MUP, 0u),
            MouseButtons.X1 => (down ? XDOWN : XUP, 1u), MouseButtons.X2 => (down ? XDOWN : XUP, 2u), _ => (0u, 0u),
        };
        if (flag != 0) Send(new INPUT { type = 0, u = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = flag, mouseData = data } } });
    }

    public void Wheel(int notches) => Send(new INPUT { type = 0, u = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = WHEEL, mouseData = unchecked((uint)(notches * 120)) } } });
}

[SupportedOSPlatform("windows")]
public sealed class WindowsProcessSource : IProcessSource
{
    private readonly SystemProcessSource _inner = new();
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);

    public IReadOnlyCollection<string> RunningProcessNames() => _inner.RunningProcessNames();

    public IReadOnlyList<string> ExecutablePaths(string processName)
    {
        var list = new List<string>();
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(ProcessWatcher.Normalize(processName)))
        {
            // Fails for elevated or protected processes: those must be added by hand.
            try { if (p.MainModule?.FileName is { Length: > 0 } f && !list.Contains(f, StringComparer.OrdinalIgnoreCase)) list.Add(f); } catch { }
            p.Dispose();
        }
        return list;
    }

    public string? ForegroundProcessName()
    {
        try
        {
            nint h = GetForegroundWindow();
            if (h == 0 || GetWindowThreadProcessId(h, out uint pid) == 0) return null;
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return null; }
    }
}

/// <summary>Start-with-Windows toggle (per user, no admin).</summary>
[SupportedOSPlatform("windows")]
public static class WindowsAutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "DualSenseUltimate";
    public static bool IsEnabled()
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(Name) != null;
    }
    public static void Set(bool enabled)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } exe) k.SetValue(Name, $"\"{exe}\" --minimized");
        else k.DeleteValue(Name, false);
    }
}
