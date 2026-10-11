using System;
using System.Collections.Generic;

namespace LiquidGlass
{
    /// <summary>抓屏后端。Auto 之外的值用于调试 / 特殊环境。</summary>
    public enum GlassCaptureMode
    {
        /// <summary>自动挑选：Magnification → GDI → 程序化背景。</summary>
        Auto = 0,

        /// <summary>只用 Magnification API（抓得最干净，能排除自身窗口）。</summary>
        Magnification = 1,

        /// <summary>只用 GDI BitBlt（抓屏瞬间隐藏窗口，可能轻微闪烁）。</summary>
        Gdi = 2,

        /// <summary>不抓屏，画程序化渐变背景（跨平台兜底）。</summary>
        Procedural = 3
    }

    /// <summary>
    /// 抓屏实现的工厂 + 按窗口句柄共享的池。
    /// 同一个窗口里放多个 LiquidGlassPanel 时，共用一份抓屏实例（Magnification 宿主窗口很昂贵）。
    /// </summary>
    public static class ScreenCaptureFactory
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<IntPtr, Entry> Pool = new Dictionary<IntPtr, Entry>();

        /// <summary>按平台/模式挑一个可用的抓取实现。</summary>
        public static IScreenCapture Create(IntPtr excludeWindow, GlassCaptureMode mode = GlassCaptureMode.Auto)
        {
            Log.Write("[capture] 模式=" + mode + "，平台=Windows:" + OperatingSystem.IsWindows() +
                      "，排除窗口=0x" + excludeWindow.ToInt64().ToString("X"));

            if (mode == GlassCaptureMode.Procedural || !OperatingSystem.IsWindows())
            {
                Log.Write("[capture] 使用程序化背景");
                return new ProceduralCapture();
            }

            if (mode == GlassCaptureMode.Gdi)
            {
                var g = CreateGdi(excludeWindow);
                return g ?? new ProceduralCapture();
            }

            if (mode == GlassCaptureMode.Magnification)
            {
                var m = CreateMagnification(excludeWindow);
                return m ?? new ProceduralCapture();
            }

            // ---- Auto ----
            IScreenCapture? primary = CreateMagnification(excludeWindow);
            // 说明：MagnificationCapture 只是轻量包装，真正的放大镜宿主窗口是
            // MagnificationHost 进程级单例。所以这里每次 new 都不会新建系统级窗口。

            IScreenCapture fallback = CreateGdi(excludeWindow) ?? (IScreenCapture)new ProceduralCapture();

            if (primary == null)
            {
                Log.Write("[capture] 主方案不可用，直接使用 " + fallback.Name);
                return fallback;
            }

            // 阈值调低到 5：Magnification 一旦间歇性失败，能尽快切到 GDI，
            // 避免窗口长时间停在灰色占位面板上（表现为"玻璃时有时无"）
            return new FailoverCapture(primary, fallback, threshold: 5);
        }

        private static IScreenCapture? CreateMagnification(IntPtr hwnd)
        {
            try
            {
                var mag = new MagnificationCapture(hwnd);
                if (mag.IsAvailable)
                {
                    Log.Write("[capture] 主方案=Magnification");
                    return mag;
                }

                Log.Write("[capture] Magnification 不可用（IsAvailable=false）");
                mag.Dispose();
            }
            catch (Exception ex)
            {
                Log.Write("[capture] Magnification 初始化异常: " + ex.Message);
            }

            return null;
        }

        private static IScreenCapture? CreateGdi(IntPtr hwnd)
        {
            try
            {
                var gdi = new GdiScreenCapture(hwnd);
                if (gdi.IsAvailable)
                {
                    // BitBlt 会把本窗口一起拍进去（WDA_EXCLUDEFROMCAPTURE 通常让它变黑），
                    // 所以抓屏瞬间把窗口藏起来再显示，避免玻璃里出现黑块/自反馈。
                    gdi.HideDuringCapture = true;
                    Log.Write("[capture] 备用方案=GDI BitBlt（抓屏瞬间隐藏窗口）");
                    return gdi;
                }

                Log.Write("[capture] GDI 不可用");
                gdi.Dispose();
            }
            catch (Exception ex)
            {
                Log.Write("[capture] GDI 初始化异常: " + ex.Message);
            }

            return null;
        }

        // ---------------- 共享池 ----------------

        /// <summary>取得（或复用）某个窗口对应的抓屏实例，引用计数 +1。</summary>
        public static IScreenCapture Acquire(IntPtr hwnd, GlassCaptureMode mode)
        {
            lock (Sync)
            {
                if (Pool.TryGetValue(hwnd, out var entry))
                {
                    entry.RefCount++;
                    return entry.Capture;
                }

                var capture = Create(hwnd, mode);
                Pool[hwnd] = new Entry(capture, 1);
                return capture;
            }
        }

        /// <summary>引用计数 -1，归零时释放。</summary>
        public static void Release(IntPtr hwnd)
        {
            Entry? dead = null;

            lock (Sync)
            {
                if (!Pool.TryGetValue(hwnd, out var entry)) return;

                if (--entry.RefCount <= 0)
                {
                    Pool.Remove(hwnd);
                    dead = entry;
                }
            }

            if (dead != null)
            {
                try
                {
                    dead.Capture.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Write("[capture] 释放异常: " + ex.Message);
                }
            }
        }

        private sealed class Entry
        {
            public Entry(IScreenCapture capture, int refCount)
            {
                Capture = capture;
                RefCount = refCount;
            }

            public IScreenCapture Capture { get; }
            public int RefCount { get; set; }
        }
    }
}
