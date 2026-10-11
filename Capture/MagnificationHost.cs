using System;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace LiquidGlass
{
    /// <summary>
    /// Magnification 宿主窗口的【进程级单例】。
    ///
    /// 为什么必须单例：
    ///   每次 new 一个放大镜宿主窗口都会消耗一个系统合成器资源，且 MagInitialize 有进程级状态。
    ///   如果一个程序反复 show/hide 窗口、每次都重建抓屏实例，多个放大镜窗口会并存，
    ///   Windows 桌面合成器会被拖垮 —— 表现就是"卡死"以及"其他窗口拖过时留下拖影"。
    ///
    /// 所以这里做引用计数：整个进程只创建【一个】宿主窗口、只调【一次】MagInitialize，
    /// 所有玻璃面板共用它，最后一个使用者释放时才真正销毁。
    /// </summary>
    internal sealed class MagnificationHost : IDisposable
    {
        private static readonly object Sync = new object();
        private static MagnificationHost? _instance;
        private static int _refCount;
        private static bool _initFailed;

        private readonly NativeMethods.MagImageScalingCallback _callback;
        private readonly SKRectI _screen;

        private IntPtr _hwndMag;
        private IntPtr _buffer;
        private int _bufferBytes;

        // 回调填的最后一帧信息
        private int _cbW;
        private int _cbH;
        private SKRectI _cbRect;
        private bool _frameReady;

        // 上一次设置的排除窗口（避免每帧重复设置过滤列表，那也是一次系统调用）
        private IntPtr _lastExclude;
        private int _lastExcludeVersion;
        private int _excludeVersion;

        // 连续失败计数：失败太多就认为宿主已失效，交给上层降级
        private int _consecutiveFailures;

        private bool _disposed;

        private MagnificationHost()
        {
            _callback = OnScalingCallback;

            var vs = NativeMethods.GetVirtualScreen();
            _screen = new SKRectI(vs.left, vs.top, vs.right, vs.bottom);

            if (!NativeMethods.MagInitialize())
            {
                Log.Write("[mag-host] MagInitialize 失败");
                return;
            }

            _hwndMag = NativeMethods.CreateWindowEx(
                NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT |
                NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW,
                "Magnifier", null,
                NativeMethods.WS_POPUP | NativeMethods.WS_VISIBLE,
                vs.left, vs.top, vs.right - vs.left, vs.bottom - vs.top,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (_hwndMag == IntPtr.Zero)
            {
                Log.Write("[mag-host] CreateWindowEx(Magnifier) 失败，错误码=" + Marshal.GetLastWin32Error());
                NativeMethods.MagUninitialize();
                return;
            }

            NativeMethods.SetLayeredWindowAttributes(_hwndMag, 0, 0, NativeMethods.LWA_ALPHA);

            var mt = new NativeMethods.MAGTRANSFORM
            {
                v = new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }
            };

            bool tOk = NativeMethods.MagSetWindowTransform(_hwndMag, ref mt);
            bool cOk = NativeMethods.MagSetImageScalingCallback(_hwndMag, _callback);

            Log.Write("[mag-host] ★ 进程级单例已创建：宿主窗口=0x" + _hwndMag.ToInt64().ToString("X") +
                      "，虚拟屏=" + vs.left + "," + vs.top + " " + (vs.right - vs.left) + "x" + (vs.bottom - vs.top) +
                      "，transform=" + tOk + "，回调注册=" + cOk +
                      "（整个进程只会有这一个放大镜窗口，反复 show/hide 不会重建）");
        }

        public SKRectI ScreenBounds => _screen;
        public bool IsAvailable => _hwndMag != IntPtr.Zero && !_disposed;

        // ---------------- 引用计数式获取 / 释放 ----------------

        /// <summary>取得进程级宿主（不存在则创建）。失败返回 null。</summary>
        public static MagnificationHost? Acquire()
        {
            lock (Sync)
            {
                if (_initFailed) return null;

                if (_instance != null)
                {
                    if (!_instance.IsAvailable) return null;

                    _refCount++;
                    Log.Write("[mag-host] 复用现有宿主，引用计数=" + _refCount);
                    return _instance;
                }

                var host = new MagnificationHost();
                if (!host.IsAvailable)
                {
                    _initFailed = true;
                    host.Dispose();
                    return null;
                }

                _instance = host;
                _refCount = 1;
                return host;
            }
        }

        /// <summary>引用计数 -1，归零时真正销毁。</summary>
        public static void Release()
        {
            MagnificationHost? dead = null;

            lock (Sync)
            {
                if (_instance == null) return;

                _refCount--;
                Log.Write("[mag-host] 释放，剩余引用计数=" + _refCount);

                if (_refCount > 0) return;

                dead = _instance;
                _instance = null;
            }

            dead?.Dispose();
        }

        /// <summary>诊断用：当前引用计数与宿主状态。</summary>
        public static string DebugState
        {
            get
            {
                lock (Sync)
                {
                    return "refCount=" + _refCount +
                           "，instance=" + (_instance == null ? "null" : "0x" + _instance._hwndMag.ToInt64().ToString("X")) +
                           "，initFailed=" + _initFailed;
                }
            }
        }

        /// <summary>
        /// 窗口集变化时调用（例如新开了窗口），让排除列表失效并重新计算。
        /// 不主动枚举，避免每次都做多次 EnumWindows。
        /// </summary>
        public void InvalidateFilter() => _excludeVersion++;

        // ---------------- 抓屏 ----------------

        public bool TryCapture(SKRectI region, SKBitmap target, IntPtr excludeWindow, out SKRectI captured)
        {
            captured = default;

            if (!IsAvailable || target == null) return false;

            var r = RectUtil.Clamp(region, _screen);

            _frameReady = false;

            // 排除列表只在「排除窗口变了」或「版本号变了」时才重设
            if (excludeWindow != _lastExclude || _excludeVersion != _lastExcludeVersion)
            {
                UpdateFilterList(excludeWindow);
                _lastExclude = excludeWindow;
                _lastExcludeVersion = _excludeVersion;
            }

            NativeMethods.SetWindowPos(_hwndMag, IntPtr.Zero,
                r.Left, r.Top, r.Width, r.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

            var rc = new NativeMethods.RECT
            {
                left = r.Left,
                top = r.Top,
                right = r.Right,
                bottom = r.Bottom
            };

            NativeMethods.MagSetWindowSource(_hwndMag, rc);
            NativeMethods.InvalidateRect(_hwndMag, IntPtr.Zero, false);

            // UpdateWindow 同步把 WM_PAINT 送到放大镜窗口过程，回调就在这次调用里发生
            NativeMethods.UpdateWindow(_hwndMag);

            if (!_frameReady || _buffer == IntPtr.Zero)
            {
                _consecutiveFailures++;
                if (_consecutiveFailures == 5 || _consecutiveFailures == 50)
                {
                    Log.Write("[mag-host] ⚠ 连续 " + _consecutiveFailures + " 帧没拿到回调数据。若持续出现，" +
                              "可能是系统合成器忙碌或其它放大镜实例冲突。");
                }
                return false;
            }

            _consecutiveFailures = 0;

            int w = Math.Min(Math.Min(_cbW, r.Width), target.Width);
            int h = Math.Min(Math.Min(_cbH, r.Height), target.Height);
            if (w <= 0 || h <= 0) return false;

            PixelCopy.Rows(
                target.GetPixels(), target.RowBytes,
                _buffer, _cbW * 4,
                w * 4, h);

            // unclipped 是【相对放大镜宿主窗口】的坐标，宿主已对齐到 r，所以换算成 r.Left + offset
            int ox = (_cbRect.Left >= 0 && _cbRect.Left <= r.Width - w) ? _cbRect.Left : 0;
            int oy = (_cbRect.Top >= 0 && _cbRect.Top <= r.Height - h) ? _cbRect.Top : 0;

            captured = new SKRectI(r.Left + ox, r.Top + oy, r.Left + ox + w, r.Top + oy + h);
            return true;
        }

        private void UpdateFilterList(IntPtr excludeWindow)
        {
            if (_hwndMag == IntPtr.Zero) return;

            var list = new System.Collections.Generic.List<IntPtr>();

            if (excludeWindow != IntPtr.Zero)
            {
                list.Add(excludeWindow);

                NativeMethods.EnumWindowsProc childProc = (h, l) => { list.Add(h); return true; };
                NativeMethods.EnumChildWindows(excludeWindow, childProc, IntPtr.Zero);

                uint tid = NativeMethods.GetWindowThreadProcessId(excludeWindow, IntPtr.Zero);
                NativeMethods.EnumThreadWindows(tid, childProc, IntPtr.Zero);

                GC.KeepAlive(childProc);
            }

            list.Add(_hwndMag);

            // 去重
            var unique = new System.Collections.Generic.List<IntPtr>(list.Count);
            foreach (var h in list)
            {
                if (!unique.Contains(h)) unique.Add(h);
            }

            IntPtr mem = Marshal.AllocHGlobal(IntPtr.Size * Math.Max(1, unique.Count));
            try
            {
                for (int i = 0; i < unique.Count; i++)
                {
                    Marshal.WriteIntPtr(mem, i * IntPtr.Size, unique[i]);
                }

                NativeMethods.MagSetWindowFilterList(
                    _hwndMag, NativeMethods.MW_FILTERMODE_EXCLUDE, unique.Count, mem);

                Log.Once("mag-host-filter", "[mag-host] 排除列表已设置：" + unique.Count + " 个窗口");
            }
            finally
            {
                Marshal.FreeHGlobal(mem);
            }
        }

        private bool OnScalingCallback(
            IntPtr hwnd,
            IntPtr srcdata,
            NativeMethods.MAGIMAGEHEADER srcheader,
            IntPtr destdata,
            NativeMethods.MAGIMAGEHEADER destheader,
            NativeMethods.RECT unclipped,
            NativeMethods.RECT clipped,
            IntPtr dirty)
        {
            try
            {
                int w = (int)srcheader.width;
                int h = (int)srcheader.height;

                if (srcdata == IntPtr.Zero) return true;
                if (w <= 0 || h <= 0 || w > 8192 || h > 8192) return true;

                // Magnification 给的 srcheader.stride 在某些系统上是未初始化的垃圾值
                int rawStride = (int)srcheader.stride;
                bool bottomUp = rawStride < 0;
                int srcStride = bottomUp ? -rawStride : rawStride;

                int tight = w * 4;
                if (srcStride < tight || srcStride > w * 8)
                {
                    Log.Once("mag-host-stride", "[mag-host] stride 不可信(" + rawStride + ")，按 " + tight + " 紧密排列处理");
                    srcStride = tight;
                    bottomUp = false;
                }

                int dstStride = tight;
                long need = (long)h * dstStride;

                if (_buffer == IntPtr.Zero || need > _bufferBytes)
                {
                    if (_buffer != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(_buffer);
                        _buffer = IntPtr.Zero;
                    }

                    _buffer = Marshal.AllocHGlobal((IntPtr)need);
                    _bufferBytes = (int)need;
                }

                int rows = (int)Math.Min(h, (long)(_bufferBytes / dstStride));

                if (bottomUp)
                {
                    PixelCopy.RowsBottomUp(_buffer, dstStride, srcdata, srcStride, dstStride, rows);
                }
                else
                {
                    PixelCopy.Rows(_buffer, dstStride, srcdata, srcStride, dstStride, rows);
                }

                _cbW = w;
                _cbH = h;
                _cbRect = new SKRectI(unclipped.left, unclipped.top, unclipped.left + w, unclipped.top + h);
                _frameReady = true;
            }
            catch (Exception ex)
            {
                Log.Once("mag-host-cb-ex", "[mag-host] 回调异常: " + ex);
            }

            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Log.Write("[mag-host] 销毁宿主窗口 0x" + _hwndMag.ToInt64().ToString("X"));

            if (_hwndMag != IntPtr.Zero)
            {
                try
                {
                    NativeMethods.MagSetImageScalingCallback(_hwndMag, null!);
                    NativeMethods.MagSetWindowFilterList(_hwndMag, NativeMethods.MW_FILTERMODE_EXCLUDE, 0, IntPtr.Zero);
                    NativeMethods.DestroyWindow(_hwndMag);
                }
                catch
                {
                    // ignored
                }

                try
                {
                    NativeMethods.MagUninitialize();
                }
                catch
                {
                    // ignored
                }

                _hwndMag = IntPtr.Zero;
            }

            if (_buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_buffer);
                _buffer = IntPtr.Zero;
                _bufferBytes = 0;
            }
        }
    }
}
