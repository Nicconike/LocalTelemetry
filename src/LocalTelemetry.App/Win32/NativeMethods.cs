using System.Runtime.InteropServices;

namespace LocalTelemetry.App.Win32;

/// <summary>
/// Win32 P/Invoke declarations and constants used by the taskbar embedder,
/// GDI+ overlay renderer and AppBar/shell queries.
/// </summary>
internal static class NativeMethods
{
    // Window Style Flags
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int GWLP_HWNDPARENT = -8;
    public const int CS_DBLCLKS = 0x0008;
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_CHILD = 0x40000000;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TOPMOST = 0x00000008;

    // Layered Window Blending
    public const uint LWA_COLORKEY = 0x01;
    public const uint LWA_ALPHA = 0x02;

    // SetWindowPos Flags
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_FRAMECHANGED = 0x0020;

    // ShowWindow Commands
    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;

    // Special HWND Handles
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);
    public static readonly IntPtr HWND_TOP = new(0);

    // Window Messages
    public const int WM_DESTROY = 0x0002;
    public const int WM_PAINT = 0x000F;
    public const int WM_ERASEBKGND = 0x0014;
    public const int WM_NCDESTROY = 0x0082;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONDBLCLK = 0x0203;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MBUTTONDOWN = 0x0207;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int WM_NCRBUTTONDOWN = 0x00A4;
    public const int WM_NCMBUTTONDOWN = 0x00A7;
    public const int WM_CONTEXTMENU = 0x007B;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_DPICHANGED_BEFOREPARENT = 0x02E2;
    public const int WM_DPICHANGED_AFTERPARENT = 0x02E3;
    public const int WM_TABLET_QUERYSYSTEMGESTURESTATUS = 0x02E4;
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_DISPLAYCHANGE = 0x007E;
    public const int WM_DEVICECHANGE = 0x0219;
    public const int HTCLIENT = 1;

    // AppBar & Taskbar Position
    public const uint ABM_GETTASKBARPOS = 0x05;
    public const uint ABE_LEFT = 0;
    public const uint ABE_TOP = 1;
    public const uint ABE_RIGHT = 2;
    public const uint ABE_BOTTOM = 3;
    public const int SM_CXSMICON = 49;

    // Device Change Notifications (WM_DEVICECHANGE wParam)
    public const int DBT_DEVICEARRIVAL = 0x8000;
    public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    public const int DBT_DEVNODES_CHANGED = 0x0007;

    // Monitor From Window Flags
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    // Structs
#pragma warning disable S101 // Win32 SDK P/Invoke struct naming convention matches Windows API definitions
    /// <summary>Contains information about a display monitor.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>Contains painting information for a window client area.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore, fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    /// <summary>Defines the x- and y-coordinates of a point.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    /// <summary>Defines the width and height of a rectangle.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx, cy; }

    /// <summary>Defines the coordinates of a rectangle.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    /// <summary>Contains taskbar AppBar message information.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage, uEdge;
        public RECT rc;
        public IntPtr lParam;
    }
#pragma warning restore S101

    // P/Invoke Method Declarations

    /// <summary>Retrieves a handle to the foreground window.</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    /// <summary>Retrieves a handle to the display monitor that has the largest area of intersection with the window.</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    /// <summary>Retrieves information about a display monitor.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    /// <summary>Retrieves a system metric value.</summary>
    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    /// <summary>Begins painting in the specified window.</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr BeginPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

    /// <summary>Ends painting in the specified window.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

    /// <summary>Registers a new window message guaranteed to be unique throughout the system.</summary>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string lpString);

    /// <summary>Retrieves a handle to a child window by class name.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);

    /// <summary>Changes the parent window of a specified child window.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr child, IntPtr parent);

    /// <summary>Retrieves the bounding rectangle of a window.</summary>
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);

    /// <summary>Retrieves the client-area rectangle of a window.</summary>
    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT r);

    /// <summary>Retrieves a window style long value.</summary>
    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int idx);

    /// <summary>Sets a window style long value.</summary>
    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int idx, int val);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    /// <summary>Sets an attribute of the specified window (pointer-safe for 32-bit and 64-bit platforms).</summary>
    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    /// <summary>Determines whether a window handle is valid.</summary>
    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    /// <summary>Determines whether the specified window is visible.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>Retrieves the class name of the specified window.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, char[] lpClassName, int nMaxCount);

    /// <summary>Changes window position, size and Z-order.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>Sets the specified window show state.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>Sends an AppBar message to Windows Explorer.</summary>
    [DllImport("shell32.dll")]
    public static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);

    /// <summary>Retrieves thread and process ID for a window.</summary>
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>Converts screen coordinates to client coordinates.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    /// <summary>Retrieves the DPI value for a window.</summary>
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    /// <summary>Invalidates a window client area for repainting.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

    /// <summary>Updates the client area of the specified window by sending a WM_PAINT message immediately.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UpdateWindow(IntPtr hWnd);

    /// <summary>Sets opacity and color key transparency for layered windows.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, int crKey, byte bAlpha, uint dwFlags);

    /// <summary>Retrieves double-click time in milliseconds.</summary>
    [DllImport("user32.dll")]
    public static extern uint GetDoubleClickTime();

    /// <summary>Posts a message to the window's message queue.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Repositions and resizes a window.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

    /// <summary>Window procedure used by <see cref="SetWindowSubclass"/>.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate IntPtr SUBCLASSPROC(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, UIntPtr dwRefData);

    /// <summary>Installs a window subclass callback.</summary>
    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

    /// <summary>Calls the default window procedure for a subclasses window.</summary>
    [DllImport("comctl32.dll")]
    public static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    /// <summary>Removes a window subclass callback.</summary>
    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass);

    // Mouse Hook & Cursor Constants & Declarations
    public const int WM_SETCURSOR = 0x0020;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int IDC_ARROW = 32512;

#pragma warning disable S101 // Win32 SDK P/Invoke struct naming convention matches Windows API definitions
    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
#pragma warning restore S101

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    // Native Win32 Popup Menu (HMENU)
    public const uint MF_STRING = 0x00000000;
    public const uint MF_GRAYED = 0x00000001;
    public const uint MF_DISABLED = 0x00000002;
    public const uint MF_SEPARATOR = 0x00000800;

    public const uint TPM_LEFTALIGN = 0x0000;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_BOTTOMALIGN = 0x0020;
    public const uint TPM_NONOTIFY = 0x0080;
    public const uint TPM_RETURNCMD = 0x0100;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AppendMenu(IntPtr hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    // Dark mode for native Win32 menus (Windows 11)
    /// <summary>Forces native Win32 popup menus to render in dark mode.</summary>
    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
    public static extern int SetPreferredAppMode(int mode);

    /// <summary>Flushes cached menu theme so dark mode takes effect immediately.</summary>
    [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = true)]
    public static extern void FlushMenuThemes();

    // App mode constants for SetPreferredAppMode
    public const int APPMODE_FORCEDARK = 2;

    // UIPI Message Filter Flags
    public const uint MSGFLT_ADD = 1;
    public const uint MSGFLT_ALLOW = 1;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ChangeWindowMessageFilter(uint message, uint dwFlag);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message, uint action, IntPtr pChangeFilterStruct);
}
