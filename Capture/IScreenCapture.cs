using System;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace LiquidGlass
{
    /// <summary>
    /// 屏幕抓取抽象：把「窗口背后的桌面」拿给玻璃着色器当输入。
    /// 对应 C++ 版里的 Magnification API + MyMagnificationCallback。
    /// </summary>
    public interface IScreenCapture : IDisposable
    {
        string Name { get; }

        /// <summary>可用屏幕范围（物理像素），用于把抓取区域裁剪进屏幕内。</summary>
        SKRectI ScreenBounds { get; }

        /// <summary>
        /// 抓取 region（物理像素）区域的像素写入 target（target 尺寸 >= region 尺寸）。
        /// 返回实际抓到的区域；一般情况下等于 region。
        /// </summary>
        bool TryCapture(SKRectI region, SKBitmap target, out SKRectI captured);
    }

    /// <summary>
    /// 主方案连续失败若干次后，永久切换到备用方案。
    /// 这样即使 Magnification 被系统策略挡住（远程桌面、部分虚拟机、权限），
    /// 也不会一直画占位面板，而是自动切到 GDI / 程序化背景。
    /// </summary>
    public sealed class FailoverCapture : IScreenCapture
    {
        private readonly IScreenCapture _primary;
        private readonly IScreenCapture _fallback;
        private readonly int _threshold;
        private int _fails;
        private bool _switched;

        public FailoverCapture(IScreenCapture primary, IScreenCapture fallback, int threshold = 20)
        {
            _primary = primary;
            _fallback = fallback;
            _threshold = threshold;
        }

        private IScreenCapture Active => _switched ? _fallback : _primary;

        public string Name => Active.Name + (_switched ? "(降级)" : "");

        public SKRectI ScreenBounds => Active.ScreenBounds;

        public bool TryCapture(SKRectI region, SKBitmap target, out SKRectI captured)
        {
            if (!_switched)
            {
                if (_primary.TryCapture(region, target, out captured))
                {
                    _fails = 0;
                    return true;
                }

                _fails++;
                if (_fails >= _threshold)
                {
                    _switched = true;
                    Log.Write("[capture] " + _primary.Name + " 连续失败 " + _fails +
                              " 次，永久切换到 " + _fallback.Name);
                    _primary.Dispose();
                }
                else
                {
                    return false;
                }
            }

            return _fallback.TryCapture(region, target, out captured);
        }

        public void Dispose()
        {
            _primary.Dispose();
            _fallback.Dispose();
        }
    }

    /// <summary>逐行搬运像素（源/目标 stride 不同）。</summary>
    public static unsafe class PixelCopy
    {
        public static void Rows(IntPtr dst, int dstStride, IntPtr src, int srcStride, int rowBytes, int rows)
        {
            if (dst == IntPtr.Zero || src == IntPtr.Zero || rows <= 0 || rowBytes <= 0) return;

            byte* d = (byte*)dst.ToPointer();
            byte* s = (byte*)src.ToPointer();
            int n = Math.Min(rowBytes, Math.Min(dstStride, srcStride));

            for (int y = 0; y < rows; y++)
            {
                Buffer.MemoryCopy(
                    s + (long)y * srcStride,
                    d + (long)y * dstStride,
                    n, n);
            }
        }

        /// <summary>源为自下而上 DIB 时用：第 0 行是图像最后一行。</summary>
        public static void RowsBottomUp(IntPtr dst, int dstStride, IntPtr src, int srcStride, int rowBytes, int rows)
        {
            if (dst == IntPtr.Zero || src == IntPtr.Zero || rows <= 0 || rowBytes <= 0) return;

            byte* d = (byte*)dst.ToPointer();
            byte* s = (byte*)src.ToPointer();
            int n = Math.Min(rowBytes, Math.Min(dstStride, srcStride));

            for (int y = 0; y < rows; y++)
            {
                Buffer.MemoryCopy(
                    s + (long)(rows - 1 - y) * srcStride,
                    d + (long)y * dstStride,
                    n, n);
            }
        }
    }

    public static class RectUtil
    {
        public static SKRectI Inflate(SKRectI r, int dx, int dy)
            => new SKRectI(r.Left - dx, r.Top - dy, r.Right + dx, r.Bottom + dy);

        public static SKRectI Clamp(SKRectI r, SKRectI bounds)
        {
            int left = Math.Max(r.Left, bounds.Left);
            int top = Math.Max(r.Top, bounds.Top);
            int right = Math.Min(r.Right, bounds.Right);
            int bottom = Math.Min(r.Bottom, bounds.Bottom);

            if (right <= left) right = left + 1;
            if (bottom <= top) bottom = top + 1;

            return new SKRectI(left, top, right, bottom);
        }

        /// <summary>
        /// 保持尺寸不变、整体平移进 bounds 内（放不下时才截断）。
        /// 窗口贴屏幕边缘时用这个而不是 Clamp，可以保住折射所需的外侧边距；
        /// 且抓取区域尺寸稳定，位图不用频繁重建。
        /// </summary>
        public static SKRectI FitInside(SKRectI r, SKRectI bounds)
        {
            int w = Math.Min(r.Width, bounds.Width);
            int h = Math.Min(r.Height, bounds.Height);
            if (w < 1) w = 1;
            if (h < 1) h = 1;

            int left = r.Left;
            int top = r.Top;

            if (left < bounds.Left) left = bounds.Left;
            if (left + w > bounds.Right) left = bounds.Right - w;

            if (top < bounds.Top) top = bounds.Top;
            if (top + h > bounds.Bottom) top = bounds.Bottom - h;

            // 二次兜底，防止 bounds 自身异常
            if (left < bounds.Left) left = bounds.Left;
            if (top < bounds.Top) top = bounds.Top;

            return new SKRectI(left, top, left + w, top + h);
        }
    }
}
