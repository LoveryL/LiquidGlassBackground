using System;
using SkiaSharp;

namespace LiquidGlass
{
    /// <summary>
    /// 最后的兜底：不抓屏，直接生成一张彩色渐变 + 网格的“假桌面”。
    /// 用于非 Windows 平台，或者 Magnification / GDI 都不可用的时候 ——
    /// 玻璃、折射、色散、模糊依然看得见效果，只是背景不是真实桌面。
    /// </summary>
    public sealed class ProceduralCapture : IScreenCapture
    {
        private SKBitmap? _canvas;
        private int _w;
        private int _h;

        public string Name => "Procedural";
        public SKRectI ScreenBounds => new SKRectI(-100000, -100000, 100000, 100000);
        public bool IsAvailable => true;

        public bool TryCapture(SKRectI region, SKBitmap target, out SKRectI captured)
        {
            captured = region;

            if (target == null || region.Width <= 0 || region.Height <= 0) return false;

            if (_canvas == null || _w != region.Width || _h != region.Height)
            {
                _canvas?.Dispose();
                _canvas = new SKBitmap(region.Width, region.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                _w = region.Width;
                _h = region.Height;
                Draw(_canvas);
            }

            int w = Math.Min(region.Width, target.Width);
            int h = Math.Min(region.Height, target.Height);

            PixelCopy.Rows(
                target.GetPixels(), target.RowBytes,
                _canvas.GetPixels(), _canvas.RowBytes,
                w * 4, h);

            Log.Once("proc-frame", "[proc] 程序化背景就绪: " + region.Width + "x" + region.Height);
            return true;
        }

        private void Draw(SKBitmap bmp)
        {
            using var canvas = new SKCanvas(bmp);

            // 底色：斜向渐变
            using var bg = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0),
                    new SKPoint(bmp.Width, bmp.Height),
                    new[] { new SKColor(24, 26, 46), new SKColor(70, 40, 90), new SKColor(16, 60, 80) },
                    new[] { 0.0f, 0.55f, 1.0f },
                    SKShaderTileMode.Clamp)
            };
            canvas.DrawRect(new SKRect(0, 0, bmp.Width, bmp.Height), bg);

            // 几个彩色光斑，方便观察折射与色散
            DrawBlob(canvas, bmp, 0.25f, 0.30f, 0.42f, new SKColor(255, 90, 130));
            DrawBlob(canvas, bmp, 0.75f, 0.28f, 0.36f, new SKColor(90, 200, 255));
            DrawBlob(canvas, bmp, 0.55f, 0.78f, 0.45f, new SKColor(120, 255, 190));
            DrawBlob(canvas, bmp, 0.15f, 0.80f, 0.30f, new SKColor(255, 210, 90));

            // 网格线，折射是否生效一眼就能看出来
            using var grid = new SKPaint
            {
                Color = new SKColor(255, 255, 255, 40),
                StrokeWidth = 1,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke
            };

            for (int x = 0; x < bmp.Width; x += 40)
            {
                canvas.DrawLine(x, 0, x, bmp.Height, grid);
            }
            for (int y = 0; y < bmp.Height; y += 40)
            {
                canvas.DrawLine(0, y, bmp.Width, y, grid);
            }
        }

        private static void DrawBlob(SKCanvas canvas, SKBitmap bmp, float fx, float fy, float fr, SKColor color)
        {
            float cx = bmp.Width * fx;
            float cy = bmp.Height * fy;
            float r = Math.Max(bmp.Width, bmp.Height) * fr;

            using var paint = new SKPaint
            {
                Shader = SKShader.CreateRadialGradient(
                    new SKPoint(cx, cy), r,
                    new[] { color, color.WithAlpha(0) },
                    new[] { 0.0f, 1.0f },
                    SKShaderTileMode.Clamp)
            };

            canvas.DrawRect(new SKRect(0, 0, bmp.Width, bmp.Height), paint);
        }

        public void Dispose()
        {
            _canvas?.Dispose();
            _canvas = null;
        }
    }
}
