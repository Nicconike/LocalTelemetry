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
    public const int CS_DBLCLKS = 0x0008;
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_CHILD = 0x40000000;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    // Layered Window Blending
    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;
    public const int ULW_ALPHA = 0x02;
    public const uint LWA_COLORKEY = 0x01;
    public const uint LWA_ALPHA = 0x02;

    // SetWindowPos Flags
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    // ShowWindow Commands
    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;

    // Special HWND Handles
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);
    public static readonly IntPtr HWND_TOP = new(0);

    // Window Messages
    public const int WM_PAINT = 0x000F;
    public const int WM_ERASEBKGND = 0x0014;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONDBLCLK = 0x0203;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_TABLET_QUERYSYSTEMGESTURESTATUS = 0x02E4;
    public const int WM_NCHITTEST = 0x0084;
    public const int HTCLIENT = 1;

    // AppBar & Taskbar Position
    public const uint ABM_GETTASKBARPOS = 0x05;
    public const uint ABE_TOP = 1;
    public const uint ABE_BOTTOM = 3;
    public const int SM_CXSMICON = 49;

    // Structs

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

    /// <summary>Specifies alpha-blending parameters for <see cref="UpdateLayeredWindow"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
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

    // P/Invoke Method Declarations

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

    /// <summary>Retrieves a handle to a child window by class name.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);

    /// <summary>Changes the parent window of a specified child window.</summary>
    [DllImport("user32.dll")]
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

    /// <summary>Determines whether a window handle is valid.</summary>
    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    /// <summary>Changes window position, size and Z-order.</summary>
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>Updates position, size and alpha blending of a layered window.</summary>
    [DllImport("user32.dll")]
    public static extern bool UpdateLayeredWindow(
        IntPtr hWnd, IntPtr hdcDst,
        ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc,
        int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    /// <summary>Retrieves a device context for a window.</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    /// <summary>Releases a device context.</summary>
    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    /// <summary>Sets the specified window show state.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>Creates a compatible memory device context.</summary>
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    /// <summary>Deletes a memory device context.</summary>
    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hDC);

    /// <summary>Selects an object into a device context.</summary>
    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hDC, IntPtr obj);

    /// <summary>Deletes a GDI object.</summary>
    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr obj);

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

    /// <summary>Sets opacity and color key transparency for layered windows.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, int crKey, byte bAlpha, uint dwFlags);

    /// <summary>Retrieves double-click time in milliseconds.</summary>
    [DllImport("user32.dll")]
    public static extern uint GetDoubleClickTime();

    /// <summary>Repositions and resizes a window.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);
}
