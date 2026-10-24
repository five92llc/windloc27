using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinLoc27;

/// <summary>
/// Swallows all keyboard and mouse input system-wide with low-level hooks while active,
/// and watches the keyboard for the secret combination. Must be used from a thread with a
/// message loop (the UI thread). Hook callbacks must stay fast: Windows silently removes a
/// hook that takes longer than ~300 ms.
/// </summary>
sealed class InputBlocker : IDisposable
{
    readonly Native.HookProc _keyboardProc;
    readonly Native.HookProc _mouseProc;
    readonly HashSet<int> _down = new();
    IntPtr _keyboardHook, _mouseHook;

    /// <summary>Set while locked; called with the keys currently held. Return true to signal a match.</summary>
    public Func<IReadOnlyCollection<int>, bool>? ComboCheck { get; set; }

    /// <summary>Raised (on the hook thread, i.e. the UI thread) when ComboCheck matches.</summary>
    public event Action? ComboMatched;

    public bool Active => _keyboardHook != IntPtr.Zero;

    public InputBlocker()
    {
        _keyboardProc = KeyboardProc;
        _mouseProc = MouseProc;
    }

    public void Start()
    {
        if (Active) return;
        _down.Clear();
        using var module = Process.GetCurrentProcess().MainModule!;
        var hMod = Native.GetModuleHandle(module.ModuleName);
        _keyboardHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
        _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, hMod, 0);
        if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            Stop();
            throw new InvalidOperationException($"Could not install input hooks (error {err}).");
        }
    }

    public void Stop()
    {
        if (_keyboardHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = _mouseHook = IntPtr.Zero;
        _down.Clear();
    }

    IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return Native.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

        var info = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
        var msg = (int)wParam;
        var vk = Normalize((int)info.vkCode);

        if (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN)
        {
            if (_down.Add(vk) && ComboCheck?.Invoke(_down) == true)
            {
                _down.Clear();
                ComboMatched?.Invoke();
            }
        }
        else if (msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP)
        {
            _down.Remove(vk);
        }

        return (IntPtr)1; // swallow
    }

    static IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        => nCode < 0 ? Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam) : (IntPtr)1;

    /// <summary>Folds left/right modifier variants into the generic Shift/Ctrl/Alt codes.</summary>
    public static int Normalize(int vk) => vk switch
    {
        0xA0 or 0xA1 => 0x10, // Shift
        0xA2 or 0xA3 => 0x11, // Ctrl
        0xA4 or 0xA5 => 0x12, // Alt
        0x5C => 0x5B,         // Right Win -> Left Win
        _ => vk,
    };

    public void Dispose() => Stop();
}
