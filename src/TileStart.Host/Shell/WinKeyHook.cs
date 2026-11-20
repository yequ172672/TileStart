using System.Runtime.InteropServices;
using TileStart.Host.Utilities;

namespace TileStart.Host.Shell;

public sealed class WinKeyHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int HcAction = 0;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint VkLwin = 0x5B;
    private const uint VkRwin = 0x5C;
    private const ushort VkMenuMask = 0xE8;
    private const uint InputKeyboard = 1;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint LlkhfInjected = 0x00000010;
    private const string NativeStartBypassEventName = @"Local\TileStart.NativeStart.Bypass";

    private static readonly nuint TileStartInjectionMarker = IntPtr.Size == 8
        ? unchecked((nuint)0x54494C4553544152UL)
        : (nuint)0x54535452U;

    private readonly Action _onStandaloneWinKey;
    private readonly HookProcedure _callback;
    private nint _hook;
    private uint _winKeysDown;
    private uint _standaloneWinKey;

    public WinKeyHook(Action onStandaloneWinKey)
    {
        _onStandaloneWinKey = onStandaloneWinKey;
        _callback = Callback;
    }

    public bool Start()
    {
        if (_hook == 0)
        {
            _hook = SetWindowsHookExW(WhKeyboardLl, _callback, GetModuleHandleW(null), 0);
        }

        return _hook != 0;
    }

    public static void OpenNativeStartMenu()
    {
        SignalNativeStartBypass();
        InjectKey((ushort)VkLwin, 0, KeyEventExtendedKey);
        InjectKey((ushort)VkLwin, 0, KeyEventExtendedKey | KeyEventKeyUp);
    }

    public void Dispose()
    {
        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }

        _winKeysDown = 0;
        _standaloneWinKey = 0;
    }

    private nint Callback(int code, nint message, nint data)
    {
        if (code != HcAction)
        {
            return CallNextHookEx(_hook, code, message, data);
        }

        var key = Marshal.PtrToStructure<KeyboardData>(data);
        if (ShouldIgnoreInjectedEvent(key.Flags, key.ExtraInfo))
        {
            return CallNextHookEx(_hook, code, message, data);
        }

        var keyDown = message is WmKeyDown or WmSysKeyDown;
        var keyUp = message is WmKeyUp or WmSysKeyUp;
        var winKey = key.VirtualKey is VkLwin or VkRwin;
        var action = ProcessKey(key.VirtualKey, keyDown, keyUp,
            keyDown && winKey && _winKeysDown == 0 && IsAnyKeyboardKeyDown());

        if (winKey)
        {
            QueueDiagnostic(
                $"Win-key event: message=0x{message.ToInt64():X}, vk=0x{key.VirtualKey:X}, flags=0x{key.Flags:X}, " +
                $"extra=0x{key.ExtraInfo:X}, action={action}.");
        }

        // Always let the rest of the hook chain observe the original events.
        var next = CallNextHookEx(_hook, code, message, data);
        if (next != 0 || action != WinKeyAction.OpenTileStart || !TryMaskStandaloneRelease(key))
        {
            return next;
        }

        _onStandaloneWinKey();
        return 1;
    }

    internal WinKeyAction ProcessKey(uint virtualKey, bool keyDown, bool keyUp, bool anotherKeyHeld = false)
    {
        var winKeyBit = virtualKey switch
        {
            VkLwin => 1u,
            VkRwin => 2u,
            _ => 0u,
        };

        if (keyDown && winKeyBit != 0)
        {
            if ((_winKeysDown & winKeyBit) == 0)
            {
                _standaloneWinKey = _winKeysDown == 0 && !anotherKeyHeld ? virtualKey : 0;
                _winKeysDown |= winKeyBit;
            }
        }
        else if (keyUp && winKeyBit != 0)
        {
            _winKeysDown &= ~winKeyBit;
            if (_standaloneWinKey == virtualKey)
            {
                _standaloneWinKey = 0;
                return WinKeyAction.OpenTileStart;
            }
        }
        else if (keyDown || keyUp)
        {
            _standaloneWinKey = 0;
        }

        return WinKeyAction.None;
    }

    private static bool IsAnyKeyboardKeyDown()
    {
        // The current Win down has not changed asynchronous state yet. Read the keys held before it.
        for (var virtualKey = 0x08; virtualKey < 0xFF; virtualKey++)
        {
            if (GetAsyncKeyState(virtualKey) < 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryMaskStandaloneRelease(KeyboardData key)
    {
        // An unassigned key between Win down and up prevents Windows from opening Start.
        // Queue the mask and replacement release together so the system still sees a balanced Win press.
        Input[] inputs =
        [
            KeyboardInputEvent(VkMenuMask, 0, 0),
            KeyboardInputEvent(VkMenuMask, 0, KeyEventKeyUp),
            KeyboardInputEvent((ushort)key.VirtualKey, (ushort)key.ScanCode, KeyEventExtendedKey | KeyEventKeyUp),
        ];
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }

    private static Input KeyboardInputEvent(ushort virtualKey, ushort scanCode, uint flags) => new()
    {
        Type = InputKeyboard,
        Data = new InputData
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = virtualKey,
                ScanCode = scanCode,
                Flags = flags,
                ExtraInfo = TileStartInjectionMarker,
            },
        },
    };

    internal static bool ShouldIgnoreInjectedEvent(uint flags, nuint extraInfo) =>
        (flags & LlkhfInjected) != 0 && extraInfo == TileStartInjectionMarker;

    private static void InjectKey(ushort virtualKey, ushort scanCode, uint flags)
    {
        keybd_event((byte)virtualKey, (byte)scanCode, flags, TileStartInjectionMarker);
    }

    private static void SignalNativeStartBypass()
    {
        try
        {
            var bypassEvent = EventWaitHandle.OpenExisting(NativeStartBypassEventName);
            bypassEvent.Set();
            ThreadPool.QueueUserWorkItem(static state =>
            {
                using var eventHandle = (EventWaitHandle)state!;
                Thread.Sleep(1000);
                eventHandle.Reset();
            }, bypassEvent);
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // No ShellHook is active, so the injected Win key already reaches the native menu.
        }
        catch (UnauthorizedAccessException)
        {
            // Fail open: injection still gives Windows a chance to handle the native menu.
        }
    }

    private static void QueueDiagnostic(string message) =>
        ThreadPool.QueueUserWorkItem(static state => DiagnosticLog.Write((string)state!), message);

    private delegate nint HookProcedure(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputData Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SetWindowsHookExW(int hookType, HookProcedure procedure, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, [In] Input[] inputs, int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);
}
