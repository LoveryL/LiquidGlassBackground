using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace LiquidGlass
{
    /// <summary>
    /// 用 Magnification API 抓取桌面，并把自身窗口排除在外。
    ///
    /// 本类是【轻量包装】：真正昂贵的放大镜宿主窗口是进程级单例（<see cref="MagnificationHost"/>），
    /// 所以反复 show/hide 窗口、反复创建本类，都不会再新建系统级的放大镜窗口。
    ///
    /// 实现要点：
    ///   - 宿主窗口由单例在调用线程上创建，不做子类化、不开独立消息循环；
    ///     UpdateWindow 会同步把 WM_PAINT 送到放大镜窗口过程，捕获在调用栈内完成；
    ///   - 宿主窗口尺寸/位置每次对齐到要抓的区域，transform 保持 1:1；
    ///   - 排除列表只在目标窗口变化时重设，避免每帧多次系统调用。
    /// </summary>
    public sealed class MagnificationCapture : IScreenCapture
    {
        private readonly IntPtr _excludeWindow;
        private readonly SKRectI _screen;

        private MagnificationHost? _host;
        private bool _disposed;

        public string Name => "Magnification";
        public SKRectI ScreenBounds => _screen;
        public bool IsAvailable => _host != null && _host.IsAvailable;

        public MagnificationCapture(IntPtr excludeWindow)
        {
            _excludeWindow = excludeWindow;

            var vs = NativeMethods.GetVirtualScreen();
            _screen = new SKRectI(vs.left, vs.top, vs.right, vs.bottom);

            _host = MagnificationHost.Acquire();

            if (_host == null)
            {
                Log.Write("[mag] 宿主不可用（Acquire 返回 null），" + MagnificationHost.DebugState);
                return;
            }

            _screen = _host.ScreenBounds;

            Log.Write("[mag] 已挂载到进程级宿主（轻量包装，未新建放大镜窗口），排除窗口=0x" +
                      excludeWindow.ToInt64().ToString("X") + "，" + MagnificationHost.DebugState);
        }

        public bool TryCapture(SKRectI region, SKBitmap target, out SKRectI captured)
        {
            captured = default;

            var host = _host;
            if (host == null || !host.IsAvailable || target == null || _disposed) return false;

            return host.TryCapture(region, target, _excludeWindow, out captured);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_host != null)
            {
                MagnificationHost.Release();
                _host = null;
                Log.Write("[mag] 已释放宿主引用，" + MagnificationHost.DebugState);
            }
        }
    }
}
