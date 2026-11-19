// Windows 8.x 及更早系统的托盘气泡通知（balloon tip）兜底。
// 这些系统的桌面应用无法使用 Win10 的 toast，因此降级为经典 Shell_NotifyIcon(NIF_INFO) 气泡。
// 实现要点：
//   1) 气泡必须挂在托盘图标上 → 创建隐藏消息窗口 + 临时托盘图标，气泡消失后 NIM_DELETE 移除图标；
//   2) 独立 STA 线程 + 自带消息循环，不干扰 ClassIsland 的 Avalonia UI 线程；
//   3) 全程静默降级：非 Windows / 任何 Win32 失败都不抛出，不影响插件其余功能。
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace DeepSeekBalance.Helpers;

internal static class WindowsBalloonNotifier
{
    private const int WM_APP = 0x8000;
    private const int WM_TRAYICON = WM_APP + 1;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_INFO = 0x00000010;

    private const int NIIF_INFO = 0x00000001;

    // 气泡回调事件（WM_TRAYICON 的 lParam 低 16 位；未设 uVersion，故为旧版布局）
    private const int NIN_BALLOONTIMEOUT = 0x0404;
    private const int NIN_BALLOONUSERCLICK = 0x0405;

    private const int NID_TIP_LENGTH = 128;
    private const uint PM_REMOVE = 0x0001;

    /// <summary>气泡未被点击/未收到超时回调时的强制收尾上限，避免线程与托盘图标残留。</summary>
    private const int BalloonMaxWaitMs = 20000;

    private const string WindowClassName = "DeepSeekBalanceBalloonWnd";
    private const string ToolTip = "DeepSeek余额";

    private static readonly object ClassLock = new();
    private static bool _classRegistered;
    private static WndProcDelegate? _wndProcKeepAlive;   // 保持委托引用，防止 GC 回收导致回调失效
    private static int _busy;                           // 同一时刻只允许一个气泡

    /// <summary>
    /// 弹出托盘气泡（非阻塞，立即返回）。仅 Windows 生效；重复调用时后到的请求被忽略。
    /// </summary>
    public static void Show(string title, string body)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (Interlocked.Exchange(ref _busy, 1) != 0) return;
        try
        {
            var t = new Thread(() => Run(title, body))
            {
                IsBackground = true,
                Name = "DeepSeekBalanceBalloon",
            };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }
        catch { Interlocked.Exchange(ref _busy, 0); }
    }

    private static void Run(string title, string body)
    {
        IntPtr hwnd = IntPtr.Zero;
        var data = default(NOTIFYICONDATA);
        bool iconAdded = false;
        try
        {
            hwnd = CreateHiddenWindow();
            if (hwnd == IntPtr.Zero) return;

            data = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = hwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = LoadProcessIcon(),
                szTip = ToolTip,
            };
            if (!Shell_NotifyIcon(NIM_ADD, ref data)) return;
            iconAdded = true;

            // 图标就位后再单独发气泡：部分 shell 在 NIM_ADD 同时带 NIF_INFO 时会忽略气泡。
            data.uFlags = NIF_INFO;
            data.szInfoTitle = Truncate(title, 63);
            data.szInfo = Truncate(body, 255);
            data.dwInfoFlags = NIIF_INFO;
            Shell_NotifyIcon(NIM_MODIFY, ref data);

            WaitForBalloonDismissed(hwnd);
        }
        catch { /* 静默降级 */ }
        finally
        {
            try { if (iconAdded) Shell_NotifyIcon(NIM_DELETE, ref data); } catch { }
            try { if (hwnd != IntPtr.Zero) DestroyWindow(hwnd); } catch { }
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>泵本线程消息直到气泡被点击/超时，或到达上限（气泡消失后才能安全移除托盘图标）。</summary>
    private static void WaitForBalloonDismissed(IntPtr hwnd)
    {
        var deadline = Environment.TickCount64 + BalloonMaxWaitMs;
        while (Environment.TickCount64 < deadline)
        {
            if (!PeekMessage(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE))
            {
                Thread.Sleep(50);
                continue;
            }
            if (msg.hwnd == hwnd && msg.message == WM_TRAYICON)
            {
                var evt = (int)(msg.lParam.ToInt64() & 0xFFFF);
                if (evt == NIN_BALLOONTIMEOUT || evt == NIN_BALLOONUSERCLICK) return;
            }
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private static IntPtr CreateHiddenWindow()
    {
        if (!EnsureClassRegistered()) return IntPtr.Zero;
        return CreateWindowEx(0, WindowClassName, "", 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
    }

    private static bool EnsureClassRegistered()
    {
        lock (ClassLock)
        {
            if (_classRegistered) return true;
            _wndProcKeepAlive = WndProc;
            var wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = _wndProcKeepAlive,
                hInstance = GetModuleHandle(null),
                lpszClassName = WindowClassName,
            };
            _classRegistered = RegisterClassEx(ref wc) != 0;
            return _classRegistered;
        }
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        => DefWindowProc(hWnd, msg, wParam, lParam);

    /// <summary>取宿主进程 exe 的图标（通知身份与 ClassIsland 一致）；失败退回系统信息图标。</summary>
    private static IntPtr LoadProcessIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                if (ExtractIconEx(exe, 0, out var large, out var small, 1) > 0)
                    return large != IntPtr.Zero ? large : small;
            }
        }
        catch { }
        try { return LoadIcon(IntPtr.Zero, (IntPtr)32516 /* IDI_INFORMATION */); } catch { return IntPtr.Zero; }
    }

    /// <summary>按 NOTIFYICONDATA 固定缓冲长度截断（避免编组越界）。</summary>
    private static string Truncate(string? s, int maxChars)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= maxChars ? s : s[..maxChars];
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public WndProcDelegate? lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = NID_TIP_LENGTH)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin,
        uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex,
        out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);
}
