using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace TileStart.Host.Updates;

internal sealed class UpdateIdleMonitor : IDisposable
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private const int WmInput = 0x00FF;
    private const int WmInputDeviceChange = 0x00FE;
    private const ushort GenericDesktop = 1;
    private const uint RidevRemove = 0x00000001;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevDevNotify = 0x00002000;
    private const uint ErrorDeviceNotConnected = 1167;
    private const uint MonitorDefaultToNearest = 2;
    private const int NotificationStateAcceptsNotifications = 5;
    private const int NotificationStateNotPresent = 1;
    private const int SystemExecutionState = 16;
    private const uint DisplayRequired = 0x00000002;

    private readonly HwndSource _inputWindow;
    private readonly uint?[] _gamepadPackets = new uint?[4];
    private readonly UpdateIdleClock _idleClock = new(Environment.TickCount64);
    private bool _controllerActivity;
    private bool _haveLastInput;
    private uint _lastInput;

    internal UpdateIdleMonitor()
    {
        _inputWindow = new HwndSource(new HwndSourceParameters("TileStart update input") { ParentWindow = new nint(-3) });
        _inputWindow.AddHook(OnInputMessage);
        if (!RegisterControllers(RidevInputSink | RidevDevNotify, _inputWindow.Handle))
        {
            _inputWindow.Dispose();
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    internal bool HasBeenIdle()
    {
        var input = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        var controllerActive = SampleGamepads() | _controllerActivity;
        _controllerActivity = false;
        var now = Environment.TickCount64;
        var hasInput = GetLastInputInfo(ref input);
        var inputChanged = !_haveLastInput || _lastInput != input.Tick;
        _haveLastInput = hasInput;
        _lastInput = input.Tick;
        return _idleClock.Observe(now, !hasInput || inputChanged || controllerActive);
    }

    internal static bool CanInterruptDesktop()
    {
        if (SHQueryUserNotificationState(out var state) != 0
            || state != NotificationStateAcceptsNotifications && state != NotificationStateNotPresent
            || CallNtPowerInformation(SystemExecutionState, 0, 0, out var executionState, sizeof(uint)) != 0
            || (executionState & DisplayRequired) != 0)
        {
            return false;
        }

        if (state == NotificationStateNotPresent)
        {
            return ProcessIdToSessionId((uint)Environment.ProcessId, out var session)
                   && session == WTSGetActiveConsoleSessionId();
        }

        var foreground = GetForegroundWindow();
        if (foreground == 0 || foreground == GetShellWindow() || foreground == GetDesktopWindow())
        {
            return true;
        }

        var className = new char[32];
        var classLength = GetClassNameW(foreground, className, className.Length);
        if (classLength > 0 && className.AsSpan(0, classLength).SequenceEqual("WorkerW"))
        {
            return true;
        }

        var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        return GetWindowRect(foreground, out var bounds) && GetMonitorInfoW(monitor, ref info)
               && !(bounds.Left <= info.Bounds.Left && bounds.Top <= info.Bounds.Top
                    && bounds.Right >= info.Bounds.Right && bounds.Bottom >= info.Bounds.Bottom);
    }

    private bool SampleGamepads()
    {
        var activity = false;
        for (var index = 0; index < _gamepadPackets.Length; index++)
        {
            var result = XInputGetState((uint)index, out var state);
            if (result != 0)
            {
                activity |= result != ErrorDeviceNotConnected || _gamepadPackets[index] is not null;
                _gamepadPackets[index] = null;
                continue;
            }

            activity |= _gamepadPackets[index] != state.Packet || state.HasActiveControls;
            _gamepadPackets[index] = state.Packet;
        }

        return activity;
    }

    private nint OnInputMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is WmInput or WmInputDeviceChange)
        {
            // Only controller usages are registered here. Report contents are never read or stored.
            _controllerActivity = true;
        }

        return 0;
    }

    private static bool RegisterControllers(uint flags, nint target)
    {
        RawInputDevice[] devices = [
            new(GenericDesktop, 0x04, flags, target), // Joystick
            new(GenericDesktop, 0x05, flags, target), // Gamepad
            new(GenericDesktop, 0x08, flags, target), // Multi-axis controller
        ];
        return RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>());
    }

    public void Dispose()
    {
        RegisterControllers(RidevRemove, 0);
        _inputWindow.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Tick;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RawInputDevice(ushort page, ushort usage, uint flags, nint target)
    {
        public readonly ushort Page = page;
        public readonly ushort Usage = usage;
        public readonly uint Flags = flags;
        public readonly nint Target = target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GamepadState
    {
        public uint Packet;
        private ushort _buttons;
        private byte _leftTrigger;
        private byte _rightTrigger;
        private short _leftX;
        private short _leftY;
        private short _rightX;
        private short _rightY;

        public readonly bool HasActiveControls => _buttons != 0 || _leftTrigger > 30 || _rightTrigger > 30
            || Math.Abs((int)_leftX) > 7849 || Math.Abs((int)_leftY) > 7849
            || Math.Abs((int)_rightX) > 8689 || Math.Abs((int)_rightY) > 8689;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Bounds;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo input);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint userIndex, out GamepadState state);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("powrprof.dll")]
    private static extern int CallNtPowerInformation(int level, nint input, uint inputLength, out uint result, uint resultLength);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetClassNameW(nint window, [Out] char[] name, int size);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out Rect bounds);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo information);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("kernel32.dll")]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
}

internal sealed class UpdateIdleClock(long startMilliseconds)
{
    private long _lastObservation = startMilliseconds;
    private long _lastActivity = startMilliseconds;

    internal bool Observe(long milliseconds, bool inputObserved)
    {
        if (inputObserved || milliseconds - _lastObservation > UpdateIdleMonitor.PollInterval.TotalMilliseconds * 3)
        {
            _lastActivity = milliseconds;
        }

        _lastObservation = milliseconds;
        return milliseconds - _lastActivity >= TimeSpan.FromHours(1).TotalMilliseconds;
    }
}
