using System;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace LiquidGlass
{
    /// <summary>
    /// 兜底方案：GDI BitBlt 抓屏。
    /// 比 Magnification 简单可靠，但无法保证把自身窗口排除掉，所以：
    ///   1) 先试 SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)（Win10 2004+）；
    ///   2) 若仍然能拍到自己，把 HideDuringCapture 改成 true —— 抓屏瞬间隐藏窗口再显示，
    ///      代价是可能有轻微闪烁。
    /// </summary>
    public sealed class GdiScreenCapture : IScreenCapture
    {
        private readonly SKRectI _screen;
        private readonly IntPtr _hdcScreen;
        private readonly IntPtr _hdcMem;
        private readonly IntPtr _excludeWindow;

        private IntPtr _dib;
        private IntPtr _bits;
        private IntPtr _oldBitmap;
        private int _dibW;
        private int _dibH;

        /// <summary>true = 抓屏瞬间把窗口藏起来（能彻底避免拍到自己，但可能闪烁）。</summary>
        public bool HideDuringCapture;

        public string Name => "GDI BitBlt";
        public SKRectI ScreenBounds => _screen;
        public bool IsAvailable => _hdcScreen != IntPtr.Zero && _hdcMem != IntPtr.Zero;

        public GdiScreenCapture(IntPtr excludeWindow)
        {
            _excludeWindow = excludeWindow;

            var vs = NativeMethods.GetVirtualScreen();
            _screen = new SKRectI(vs.left, vs.top, vs.right, vs.bottom);

            _hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
            _hdcMem = _hdcScreen == IntPtr.Zero
                ? IntPtr.Zero
                : NativeMethods.CreateCompatibleDC(_hdcScreen);

            Log.Write("[gdi] 初始化: 屏幕DC=" + (_hdcScreen != IntPtr.Zero) +
                      "，内存DC=" + (_hdcMem != IntPtr.Zero) +
                      "，虚拟屏=" + vs.left + "," + vs.top + " " + (vs.right - vs.left) + "x" + (vs.bottom - vs.top));

            if (excludeWindow != IntPtr.Zero)
            {
                try
                {
                    bool ok = NativeMethods.SetWindowDisplayAffinity(
                        excludeWindow, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                    Log.Write("[gdi] SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)=" + ok);
                }
                catch (Exception ex)
                {
                    Log.Write("[gdi] SetWindowDisplayAffinity 不可用: " + ex.Message);
                }
            }
        }

        private bool EnsureDib(int w, int h)
        {
            if (_dib != IntPtr.Zero && _dibW == w && _dibH == h) return true;

            ReleaseDib();

            var info = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    biWidth = w,
                    biHeight = -h,          // 负数 = 自上而下，行序与 Skia 一致
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0
                }
            };

            _dib = NativeMethods.CreateDIBSection(
                _hdcScreen, ref info, NativeMethods.DIB_RGB_COLORS, out _bits, IntPtr.Zero, 0);

            if (_dib == IntPtr.Zero)
            {
                _bits = IntPtr.Zero;
                Log.Once("gdi-dib", "[gdi] CreateDIBSection 失败 " + w + "x" + h);
                return false;
            }

            _dibW = w;
            _dibH = h;
            _oldBitmap = NativeMethods.SelectObject(_hdcMem, _dib);
            return true;
        }

        private void ReleaseDib()
        {
            if (_dib == IntPtr.Zero) return;

            if (_oldBitmap != IntPtr.Zero)
            {
                NativeMethods.SelectObject(_hdcMem, _oldBitmap);
                _oldBitmap = IntPtr.Zero;
            }

            NativeMethods.DeleteObject(_dib);
            _dib = IntPtr.Zero;
            _bits = IntPtr.Zero;
            _dibW = 0;
            _dibH = 0;
        }

        public bool TryCapture(SKRectI region, SKBitmap target, out SKRectI captured)
        {
            captured = default;

            if (!IsAvailable || target == null) return false;

            var r = RectUtil.Clamp(region, _screen);
            if (!EnsureDib(r.Width, r.Height)) return false;

            bool hidden = false;
            if (HideDuringCapture && _excludeWindow != IntPtr.Zero)
            {
                hidden = NativeMethods.ShowWindow(_excludeWindow, NativeMethods.SW_HIDE);
            }

            bool ok = NativeMethods.BitBlt(
                _hdcMem, 0, 0, r.Width, r.Height,
                _hdcScreen, r.Left, r.Top,
                NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);

            if (hidden)
            {
                NativeMethods.ShowWindow(_excludeWindow, NativeMethods.SW_SHOWNA);
            }

            if (!ok)
            {
                Log.Once("gdi-blit", "[gdi] BitBlt 失败，区域=" + r.Left + "," + r.Top + " " + r.Width + "x" + r.Height);
                return false;
            }

            int w = Math.Min(r.Width, target.Width);
            int h = Math.Min(r.Height, target.Height);

            PixelCopy.Rows(
                target.GetPixels(), target.RowBytes,
                _bits, r.Width * 4,
                w * 4, h);

            captured = r;
            Log.Once("gdi-frame", "[gdi] 首帧抓取成功: " + r.Left + "," + r.Top + " " + r.Width + "x" + r.Height);
            return true;
        }

        public void Dispose()
        {
            ReleaseDib();

            if (_hdcMem != IntPtr.Zero) NativeMethods.DeleteDC(_hdcMem);
            if (_hdcScreen != IntPtr.Zero) NativeMethods.ReleaseDC(IntPtr.Zero, _hdcScreen);
        }
    }
}
