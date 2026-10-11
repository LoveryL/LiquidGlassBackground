using System;
using System.Runtime.InteropServices;

namespace LiquidGlass
{
    /// <summary>
    /// Win32 P/Invoke 声明。仅在 Windows 上会被调用，其它平台由 <see cref="ProceduralCapture"/> 兜底。
    /// </summary>
    internal static class NativeMethods
    {
        // ---------- 窗口样式 ----------
        public const uint WS_POPUP = 0x80000000;
        public const uint WS_VISIBLE = 0x10000000;
        public const uint WS_EX_LAYERED = 0x00080000;
        public const uint WS_EX_TRANSPARENT = 0x00000020;
        public const uint WS_EX_NOACTIVATE = 0x08000000;
        public const uint WS_EX_TOOLWINDOW = 0x00000080;

        public const uint LWA_ALPHA = 0x00000002;
        public const uint WM_USER = 0x0400;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;

        public const uint SRCCOPY = 0x00CC0020;
        public const uint CAPTUREBLT = 0x40000000;
        public const uint DIB_RGB_COLORS = 0;
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        public const int SM_XVIRTUALSCREEN = 76;
        public const int SM_YVIRTUALSCREEN = 77;
        public const int SM_CXVIRTUALSCREEN = 78;
        public const int SM_CYVIRTUALSCREEN = 79;

        public const int MW_FILTERMODE_EXCLUDE = 0;
        public const int MW_FILTERMODE_INCLUDE = 1;

        // ShowWindow 命令
        public const int SW_HIDE = 0;
        public const int SW_SHOWNA = 8;

        // ---------- 结构体 ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left, top, right, bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x, y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MAGIMAGEHEADER
        {
            public uint width;
            public uint height;
            public int stride;
            public uint offset;
            public UIntPtr cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MAGTRANSFORM
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
            public float[] v;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }

        // ---------- 委托 ----------
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate bool MagImageScalingCallback(
            IntPtr hwnd,
            IntPtr srcdata,
            MAGIMAGEHEADER srcheader,
            IntPtr destdata,
            MAGIMAGEHEADER destheader,
            RECT unclipped,
            RECT clipped,
            IntPtr dirty);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rc, IntPtr data);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        // ---------- user32 ----------
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        /// <summary>客户端坐标 → 屏幕坐标（物理像素）。用于把控件位置换算到屏幕。</summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowEx(
            uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [DllImport("user32.dll")]
        public static extern bool UpdateWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll")]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumThreadWindows(uint dwThreadId, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        // ---------- gdi32 ----------
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        public static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

        // ---------- magnification ----------
        [DllImport("magnification.dll", CharSet = CharSet.Unicode)]
        public static extern bool MagInitialize();

        [DllImport("magnification.dll", CharSet = CharSet.Unicode)]
        public static extern bool MagUninitialize();

        [DllImport("magnification.dll", CharSet = CharSet.Unicode)]
        public static extern bool MagSetWindowSource(IntPtr hwnd, RECT rect);

        [DllImport("magnification.dll", CharSet = CharSet.Unicode)]
        public static extern bool MagSetWindowTransform(IntPtr hwnd, ref MAGTRANSFORM transform);

        [DllImport("magnification.dll", CharSet = CharSet.Unicode)]
        public static extern bool MagSetWindowFilterList(IntPtr hwnd, int filterMode, int count, IntPtr hwnds);

        [DllImport("magnification.dll", CharSet = CharSet.Unicode)]
        public static extern bool MagSetImageScalingCallback(IntPtr hwnd, MagImageScalingCallback callback);

        // ---------- 工具 ----------
        public static void TryEnableDpiAwareness()
        {
            if (!OperatingSystem.IsWindows()) return;

            try
            {
                // 先试 Per-Monitor v2（-4），失败再退到 System Aware（-2），再退到老接口
                if (!SetProcessDpiAwarenessContext(new IntPtr(-4)))
                {
                    if (!SetProcessDpiAwarenessContext(new IntPtr(-2)))
                    {
                        SetProcessDPIAware();
                    }
                }
            }
            catch
            {
                // DPI 感知设置失败不影响主流程
            }
        }

        /// <summary>枚举所有显示器，取并集得到虚拟屏幕（与 GetWindowRect 同一坐标系，物理像素）。</summary>
        public static RECT GetVirtualScreen()
        {
            RECT union = default;
            bool first = true;

            try
            {
                MonitorEnumProc proc = (IntPtr hMonitor, IntPtr hdc, ref RECT rc, IntPtr data) =>
                {
                    if (first)
                    {
                        union = rc;
                        first = false;
                    }
                    else
                    {
                        union.left = Math.Min(union.left, rc.left);
                        union.top = Math.Min(union.top, rc.top);
                        union.right = Math.Max(union.right, rc.right);
                        union.bottom = Math.Max(union.bottom, rc.bottom);
                    }
                    return true;
                };

                if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero) || first)
                {
                    // 退回到 GetSystemMetrics
                    int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
                    int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
                    int w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
                    int h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
                    if (w > 0 && h > 0)
                    {
                        union = new RECT { left = x, top = y, right = x + w, bottom = y + h };
                    }
                    else
                    {
                        union = new RECT { left = 0, top = 0, right = 1920, bottom = 1080 };
                    }
                }

                GC.KeepAlive(proc);
            }
            catch
            {
                union = new RECT { left = 0, top = 0, right = 1920, bottom = 1080 };
            }

            return union;
        }
    }
}
