using System;
using SkiaSharp;

namespace LiquidGlass
{
    /// <summary>
    /// 玻璃渲染器：对应 C++ 版 Render() 的完整流程
    ///   截图 → 上传纹理 → 横向高斯 → 纵向高斯 → 玻璃合成（SDF 折射 + 色散）→ 呈现。
    /// 在 Avalonia 里 D3D11 换成了 Skia，像素着色器换成了 SKSL（SKRuntimeEffect）。
    ///
    /// 【重要】SkiaSharp 2.88 的 CPU 光栅后端没有启用 SKSL ByteCode 解释器，
    /// 在 CPU 表面上跑 SKRuntimeEffect 会原生崩溃（ByteCode interpreter not enabled + SEHException）。
    /// 所以：只有拿到 GRContext（GPU）时才走着色器路径，否则走 DrawFallback 的纯 Skia 路径。
    /// </summary>
    public sealed class LiquidGlassRenderer : IDisposable
    {
        private readonly GlassSettings _settings;
        private readonly IScreenCapture _capture;
        private readonly Func<WindowGeometry> _geometry;

        private readonly float[] _weights = new float[8];

        // 状态
        private SKRectI _smooth;
        private bool _smoothValid;
        private SKRectI _captured;
        private bool _hasCapture;
        private SKPoint _mouseDip;
        private int _frames;
        private int _placeholders;

        private SKBitmap? _src;
        private SKSurface? _blurTemp;
        private SKSurface? _blurFinal;
        private SKSizeI _surfaceSize;
        private GRContext? _surfaceGr;

        // 着色器
        private SKRuntimeEffect? _blurEffect;
        private SKRuntimeEffect? _glassEffect;
        private SKRuntimeEffect? _highlightEffect;
        private SKRuntimeEffectUniforms? _uBlurH;
        private SKRuntimeEffectUniforms? _uBlurV;
        private SKRuntimeEffectUniforms? _uGlass;
        private SKRuntimeEffectUniforms? _uHighlight;
        private SKRuntimeEffectChildren? _cBlurH;
        private SKRuntimeEffectChildren? _cBlurV;
        private SKRuntimeEffectChildren? _cGlass;
        private SKRuntimeEffectChildren? _cHighlight;
        private bool _shadersReady;
        private bool _dispersionState = true;

        /// <summary>无 GPU / 着色器崩溃后置 true，永久禁用 SKSL 路径。</summary>
        private bool _shadersBlocked;

        /// <summary>SKSL 编译失败时的错误信息（正常为 null）。</summary>
        public string? ShaderError { get; private set; }

        /// <summary>是否走着色器路径；false 表示已降级到纯 Skia 实现。</summary>
        public bool UsingShaders => _shadersReady && !_shadersBlocked;

        /// <summary>诊断开关：强制纯色填充（证明渲染链路通不通）</summary>
        public bool TestFill;

        /// <summary>诊断开关：强制走纯 Skia 路径（跳过 SKSL）</summary>
        public bool ForceFallback;

        /// <summary>在左上角画诊断文字</summary>
        public bool ShowStatus;

        /// <summary>已成功绘制的帧数（0 = 一帧都没画出来）</summary>
        public int Frames => _frames;

        /// <summary>抓屏是否就绪（false 时会画占位面板）</summary>
        public bool HasCapture => _hasCapture;

        public string Status =>
            "capture=" + _capture.Name +
            ", hasCapture=" + _hasCapture +
            ", captured=" + _captured.Width + "x" + _captured.Height +
            ", src=" + (_src == null ? "null" : _src.Width + "x" + _src.Height) +
            ", shader=" + UsingShaders +
            (ShaderError == null ? "" : ", err=" + ShaderError.Replace("\n", " ")) +
            ", frames=" + _frames + ", placeholder=" + _placeholders;

        public LiquidGlassRenderer(
            IScreenCapture capture,
            Func<WindowGeometry> geometry,
            GlassSettings? settings = null)
        {
            _capture = capture;
            _geometry = geometry;
            _settings = settings ?? GlassSettings.CreateDefault();
            _dispersionState = _settings.UseDispersion;

            ComputeGaussianWeights();
            InitShaders();

            Log.Write("[renderer] 构造完成，抓屏=" + _capture.Name +
                      "，SKSL 编译=" + _shadersReady +
                      "，采样语法=" + (SkRuntime.IsVersion3 ? "3.x eval()" : "2.x sample()") +
                      (ShaderError == null ? "" : "，SKSL 错误=" + ShaderError));
        }

        // ---------------- 初始化 ----------------
        private void ComputeGaussianWeights()
        {
            float sigma = Math.Max(0.0001f, _settings.BlurSigma);
            int radius = Math.Clamp(_settings.BlurRadius, 1, 7);

            double sum = 0.0;
            for (int i = 0; i <= radius; i++)
            {
                _weights[i] = (float)Math.Exp(-0.5 * (i * i) / (sigma * sigma));
                sum += (i == 0) ? _weights[i] : 2.0 * _weights[i];
            }

            for (int i = 0; i <= radius; i++) _weights[i] /= (float)sum;
            for (int i = radius + 1; i < _weights.Length; i++) _weights[i] = 0.0f;
        }

        private void InitShaders()
        {
            try
            {
                _blurEffect = Compile(Sksl.BlurPass);
                _glassEffect = Compile(_settings.UseDispersion ? Sksl.GlassDispersion : Sksl.GlassRefraction);
                _highlightEffect = Compile(Sksl.Highlight);

                _shadersReady = _blurEffect != null && _glassEffect != null;

                if (!_shadersReady)
                {
                    Log.Write("[renderer] SKSL 编译失败，将使用纯 Skia 路径。错误=" + ShaderError);
                    return;
                }

                _uBlurH = new SKRuntimeEffectUniforms(_blurEffect);
                _uBlurV = new SKRuntimeEffectUniforms(_blurEffect);
                _uGlass = new SKRuntimeEffectUniforms(_glassEffect);
                _cBlurH = new SKRuntimeEffectChildren(_blurEffect);
                _cBlurV = new SKRuntimeEffectChildren(_blurEffect);
                _cGlass = new SKRuntimeEffectChildren(_glassEffect);

                if (_highlightEffect != null)
                {
                    _uHighlight = new SKRuntimeEffectUniforms(_highlightEffect);
                    _cHighlight = new SKRuntimeEffectChildren(_highlightEffect);
                }
            }
            catch (Exception ex)
            {
                ShaderError = ex.Message;
                _shadersReady = false;
                Log.Write("[renderer] InitShaders 异常: " + ex.Message);
            }
        }

        /// <summary>先试 sample()（2.x 语法），失败再试 .eval()（3.x 语法）。</summary>
        private SKRuntimeEffect? Compile(string source)
        {
            foreach (bool useEval in new[] { false, true })
            {
                var effect = SkRuntime.Create(Sksl.Expand(source, useEval), out var error);

                if (effect != null)
                {
                    ShaderError = null;
                    return effect;
                }

                ShaderError = error;
            }

            return null;
        }

        /// <summary>
        /// 宿主控件改了 GlassSettings 后调用：重算模糊权重，
        /// 并在「是否启用色散」发生切换时重建玻璃着色器。
        /// </summary>
        public void SettingsChanged()
        {
            ComputeGaussianWeights();

            if (_dispersionState == _settings.UseDispersion) return;

            _dispersionState = _settings.UseDispersion;

            try
            {
                _glassEffect?.Dispose();
                _glassEffect = Compile(_settings.UseDispersion ? Sksl.GlassDispersion : Sksl.GlassRefraction);

                if (_glassEffect != null)
                {
                    _uGlass = new SKRuntimeEffectUniforms(_glassEffect);
                    _cGlass = new SKRuntimeEffectChildren(_glassEffect);
                }

                _shadersReady = _blurEffect != null && _glassEffect != null;
                Log.Write("[renderer] 色散开关切换为 " + _settings.UseDispersion + "，SKSL 就绪=" + _shadersReady);
            }
            catch (Exception ex)
            {
                ShaderError = ex.Message;
                _shadersReady = false;
                Log.Write("[renderer] 重建玻璃着色器失败: " + ex.Message);
            }
        }

        // ---------------- 每帧：抓取 + 缓动 ----------------
        public void Tick()
        {
            try
            {
                var geo = _geometry();
                var win = geo.Physical;
                if (win.Width <= 1 || win.Height <= 1) return;

                Log.Once("geo-ok", "[tick] 玻璃区域(物理像素)=" + win.Left + "," + win.Top + " " +
                                   win.Width + "x" + win.Height + "，scale=" + geo.Scale.ToString("0.00"));

                double scale = geo.Scale > 0.1 ? geo.Scale : 1.0;

                if (!_smoothValid)
                {
                    _smooth = win;
                    _smoothValid = true;
                }
                else
                {
                    _smooth = Ease(_smooth, win, _settings.EaseSpeed);
                }

                int margin = MarginPixels((float)scale);

                // 贴边时整体平移进屏幕（而不是截断），保住折射采样需要的边距
                var patch = RectUtil.FitInside(
                    RectUtil.Inflate(_smooth, margin, margin),
                    _capture.ScreenBounds);

                // 尺寸按 64 对齐，避免窗口每移动 1px 就重建位图
                int qw = ((patch.Width + 63) / 64) * 64;
                int qh = ((patch.Height + 63) / 64) * 64;

                var src = EnsureBitmap(qw, qh);
                if (src == null)
                {
                    _hasCapture = false;
                    return;
                }

                if (_capture.TryCapture(patch, src, out var got))
                {
                    bool was = _hasCapture;
                    _captured = got;
                    _hasCapture = got.Width > 1 && got.Height > 1;

                    if (!was && _hasCapture)
                    {
                        Log.Write("[tick] 首次抓屏成功: " + got.Left + "," + got.Top + " " +
                                  got.Width + "x" + got.Height);
                    }
                }
                else
                {
                    _hasCapture = false;
                }
            }
            catch (Exception ex)
            {
                Log.Once("tick-ex", "[tick] 异常: " + ex.Message);
            }
        }

        public void SetMousePosition(SKPoint dipPosition)
        {
            _mouseDip = dipPosition;
        }

        // ---------------- 每帧：绘制 ----------------
        public void Draw(SKCanvas canvas, GRContext? grContext)
        {
            try
            {
                var geo = _geometry();
                var win = geo.Physical;
                if (win.Width <= 1 || win.Height <= 1) return;

                float canvasScale = canvas.TotalMatrix.ScaleX;
                float scale = canvasScale > 0.01f
                    ? canvasScale
                    : (float)(geo.Scale > 0.1 ? geo.Scale : 1.0);

                var rect = new SKRect(0, 0, win.Width / scale, win.Height / scale);

                // 没有 GPU 就绝不能跑 SKSL —— CPU 后端没有 ByteCode 解释器，会原生崩溃
                if (grContext == null && !_shadersBlocked)
                {
                    _shadersBlocked = true;
                    Log.Write("[draw] 当前为软件渲染（无 GRContext），SKSL 路径已禁用，改用纯 Skia 玻璃实现");
                }

                DrawInternal(canvas, rect, scale, grContext);
                _frames++;

                if (_frames == 1)
                {
                    Log.Write("[draw] 第一帧: rect=" + rect.Width.ToString("0.0") + "x" + rect.Height.ToString("0.0") +
                              ", scale=" + scale.ToString("0.00") + ", " + Status);
                }
            }
            catch (Exception ex)
            {
                // 着色器路径一旦抛异常（含原生 SEHException），永久退回纯 Skia，避免反复崩溃
                if (!_shadersBlocked)
                {
                    _shadersBlocked = true;
                    Log.Write("[draw] 着色器绘制异常，永久禁用 SKSL: " + ex.GetType().Name + " " + ex.Message);
                }
                else
                {
                    Log.Once("draw-ex", "[draw] 异常: " + ex.Message);
                }
            }
        }

        private void DrawInternal(SKCanvas canvas, SKRect rect, float scale, GRContext? grContext)
        {
            // ---- 诊断：纯色填充，用来确认「渲染链路到底通不通」 ----
            if (TestFill)
            {
                using var tp = new SKPaint { Color = new SKColor(255, 0, 200, 220) };
                canvas.DrawRect(rect, tp);
                DrawStatusText(canvas, rect);
                return;
            }

            bool useShaders = _shadersReady && !_shadersBlocked && !ForceFallback && _hasCapture && _src != null;

            if (!_hasCapture || _src == null)
            {
                // 关键：抓屏没就绪时也要画出可见内容，否则透明窗口 = 完全隐形
                DrawPlaceholder(canvas, rect, "等待抓屏…");
                _placeholders++;
                Log.Once("placeholder", "[draw] 抓屏未就绪，绘制占位面板。" + Status);
                DrawStatusText(canvas, rect);
                return;
            }

            if (!useShaders)
            {
                DrawFallback(canvas, rect, scale);
                DrawStatusText(canvas, rect);
                return;
            }

            if (DrawWithShaders(canvas, rect, scale, grContext)) return;

            // 着色器这一步失败 → 立刻画保底内容，保证这一帧不完全透明
            DrawFallback(canvas, rect, scale);
            DrawStatusText(canvas, rect);
        }

        private bool DrawWithShaders(SKCanvas canvas, SKRect rect, float scale, GRContext? grContext)
        {
            int cw = _captured.Width;
            int ch = _captured.Height;
            if (cw <= 1 || ch <= 1) return false;

            EnsureSurfaces(cw, ch, grContext);
            if (_blurTemp == null || _blurFinal == null) return false;

            // ---- 1) 背景模糊：横向 ----
            using (var srcImage = SKImage.FromBitmap(_src!))
            using (var srcShader = srcImage.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp))
            {
                if (srcShader == null || _uBlurH == null || _cBlurH == null) return false;

                SetBlurUniforms(_uBlurH, scale, 0.0f);
                if (!SkRuntime.SetChild(_cBlurH, "inputTex", srcShader)) return false;

                using var shaderH = SkRuntime.ToShader(_blurEffect!, _uBlurH, _cBlurH);
                if (shaderH == null) return false;

                using var paintH = new SKPaint { Shader = shaderH };
                var cvTemp = _blurTemp.Canvas;
                cvTemp.Clear(SKColors.Transparent);
                cvTemp.DrawRect(new SKRect(0, 0, cw, ch), paintH);
                cvTemp.Flush();
            }

            // ---- 2) 背景模糊：纵向 ----
            using (var tempImage = _blurTemp.Snapshot())
            using (var tempShader = tempImage.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp))
            {
                if (tempShader == null || _uBlurV == null || _cBlurV == null) return false;

                SetBlurUniforms(_uBlurV, 0.0f, scale);
                if (!SkRuntime.SetChild(_cBlurV, "inputTex", tempShader)) return false;

                using var shaderV = SkRuntime.ToShader(_blurEffect!, _uBlurV, _cBlurV);
                if (shaderV == null) return false;

                using var paintV = new SKPaint { Shader = shaderV };
                var cvFinal = _blurFinal.Canvas;
                cvFinal.Clear(SKColors.Transparent);
                cvFinal.DrawRect(new SKRect(0, 0, cw, ch), paintV);
                cvFinal.Flush();
            }

            // ---- 3) 玻璃合成 ----
            using (var blurImage = _blurFinal.Snapshot())
            using (var blurShader = blurImage.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp))
            {
                if (blurShader == null || _uGlass == null || _cGlass == null) return false;

                SetGlassUniforms(scale);
                if (!SkRuntime.SetChild(_cGlass, "blurTex", blurShader)) return false;

                using var shaderG = SkRuntime.ToShader(_glassEffect!, _uGlass, _cGlass);
                if (shaderG == null) return false;

                using var paintG = new SKPaint { Shader = shaderG };
                canvas.DrawRect(rect, paintG);
            }

            // ---- 4) 可选：鼠标聚光高光 ----
            if (_settings.EnableHighlight && _highlightEffect != null && _uHighlight != null && _cHighlight != null)
            {
                SetHighlightUniforms(scale);

                using var shaderHl = SkRuntime.ToShader(_highlightEffect, _uHighlight, _cHighlight);
                if (shaderHl != null)
                {
                    using var paintHl = new SKPaint { Shader = shaderHl, BlendMode = SKBlendMode.Plus };
                    canvas.DrawRect(rect, paintHl);
                }
            }

            DrawStatusText(canvas, rect);
            return true;
        }

        // ---------------- 保底可见：占位面板 ----------------
        private void DrawPlaceholder(SKCanvas canvas, SKRect rect, string tip)
        {
            float radius = Math.Min(_settings.CornerRadius, (float)Math.Min(rect.Width, rect.Height) * 0.5f);
            var rrect = new SKRoundRect(rect, radius, radius);

            using var fill = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(rect.Left, rect.Top),
                    new SKPoint(rect.Right, rect.Bottom),
                    new[] { new SKColor(235, 238, 248, 190), new SKColor(190, 200, 225, 170) },
                    new[] { 0.0f, 1.0f },
                    SKShaderTileMode.Clamp),
                IsAntialias = true
            };
            canvas.DrawRoundRect(rrect, fill);

            using var border = new SKPaint
            {
                Color = new SKColor(255, 255, 255, 200),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f,
                IsAntialias = true
            };
            canvas.DrawRoundRect(rrect, border);

            DrawText(canvas, tip + "  (" + _capture.Name + ")", rect.Left + 16, rect.Top + 34,
                     new SKColor(30, 30, 40));
        }

        // ---------------- 纯 Skia 玻璃实现（无 SKSL 时使用） ----------------
        private void DrawFallback(SKCanvas canvas, SKRect rect, float scale)
        {
            if (_src == null) return;

            using var img = SKImage.FromBitmap(_src);

            float radius = Math.Min(_settings.CornerRadius,
                (float)Math.Min(rect.Width, rect.Height) * 0.5f);
            var rrect = new SKRoundRect(rect, radius, radius);

            // 源区域：把窗口背后的那块桌面抠出来
            float sx = _smooth.Left - _captured.Left;
            float sy = _smooth.Top - _captured.Top;
            var srcRect = new SKRect(sx, sy, sx + rect.Width * scale, sy + rect.Height * scale);

            // 1) 模糊
            float sigma = Math.Max(0.5f, _settings.BlurSigma * scale * 2.0f);
            using var blurFilter = SKImageFilter.CreateBlur(sigma, sigma);

            // 2) 边缘折射近似：以中心为原点轻微放大，边缘内容被向外"拉伸"，
            //    视觉上接近 SDF 折射把背景往外推的效果
            float zoom = 1.0f + (_settings.RefractionAmount / 1000.0f);
            float dx = rect.Width * (zoom - 1.0f) * 0.5f;
            float dy = rect.Height * (zoom - 1.0f) * 0.5f;
            var destZoom = new SKRect(rect.Left - dx, rect.Top - dy, rect.Right + dx, rect.Bottom + dy);

            canvas.Save();
            canvas.ClipRoundRect(rrect, SKClipOperation.Intersect, true);

            using (var bgPaint = new SKPaint
            {
                ImageFilter = blurFilter,
                IsAntialias = true,
                FilterQuality = SKFilterQuality.High
            })
            {
                canvas.DrawImage(img, srcRect, destZoom, bgPaint);
            }

            // 3) 玻璃染色 + 压暗
            byte tintA = (byte)(_settings.GlassTint.Alpha * 3);
            using (var tint = new SKPaint { Color = new SKColor(_settings.GlassTint.Red, _settings.GlassTint.Green, _settings.GlassTint.Blue, tintA) })
            {
                canvas.DrawRect(rect, tint);
            }

            using (var dark = new SKPaint { Color = new SKColor(0, 0, 0, (byte)(40 * (1.0f - _settings.Darkening) * 10.0f)) })
            {
                canvas.DrawRect(rect, dark);
            }

            canvas.Restore();

            // 4) 顶部高光描边（模拟玻璃边缘的镜面反射）
            using (var spec = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(rect.Left, rect.Top),
                    new SKPoint(rect.Left, rect.Bottom),
                    new[]
                    {
                        new SKColor(255, 255, 255, 130),
                        new SKColor(255, 255, 255, 40),
                        new SKColor(255, 255, 255, 0)
                    },
                    new[] { 0.0f, 0.22f, 1.0f },
                    SKShaderTileMode.Clamp),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f,
                IsAntialias = true
            })
            {
                canvas.DrawRoundRect(rrect, spec);
            }

            // 5) 底部一圈暗边，增加厚度感
            using (var rim = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(rect.Left, rect.Bottom),
                    new SKPoint(rect.Left, rect.Top),
                    new[]
                    {
                        new SKColor(0, 0, 0, 60),
                        new SKColor(0, 0, 0, 0)
                    },
                    new[] { 0.0f, 0.18f },
                    SKShaderTileMode.Clamp),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f,
                IsAntialias = true
            })
            {
                canvas.DrawRoundRect(rrect, rim);
            }
        }

        // ---------------- 状态文字（诊断） ----------------
        private void DrawStatusText(SKCanvas canvas, SKRect rect)
        {
            if (!ShowStatus) return;

            float y = rect.Top + 34;
            DrawText(canvas, "capture=" + _capture.Name, rect.Left + 16, y, new SKColor(255, 255, 0)); y += 22;
            DrawText(canvas, "hasCapture=" + _hasCapture + "  rect=" + _captured.Width + "x" + _captured.Height,
                     rect.Left + 16, y, new SKColor(255, 255, 0)); y += 22;
            DrawText(canvas, "shader=" + UsingShaders + "  frames=" + _frames,
                     rect.Left + 16, y, new SKColor(255, 255, 0)); y += 22;

            if (ShaderError != null)
            {
                string err = ShaderError.Length > 90 ? ShaderError.Substring(0, 90) : ShaderError;
                DrawText(canvas, "sksl: " + err.Replace("\n", " "), rect.Left + 16, y, new SKColor(255, 120, 120));
            }
        }

        private static void DrawText(SKCanvas canvas, string text, float x, float y, SKColor color)
        {
            using var paint = new SKPaint
            {
                Color = color,
                TextSize = 16,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            canvas.DrawText(text, x, y, paint);
        }

        // ---------------- uniform ----------------
        private void SetBlurUniforms(SKRuntimeEffectUniforms? u, float stepX, float stepY)
        {
            if (u == null) return;

            u["uStep"] = new[] { stepX, stepY };
            u["uWeights0"] = new[] { _weights[0], _weights[1], _weights[2], _weights[3] };
            u["uWeights1"] = new[] { _weights[4], _weights[5], _weights[6], _weights[7] };
        }

        private void SetGlassUniforms(float scale)
        {
            var u = _uGlass;
            if (u == null) return;

            float px = _smooth.Left - _captured.Left;
            float py = _smooth.Top - _captured.Top;
            float ew = _smooth.Width;
            float eh = _smooth.Height;

            float radius = Math.Min(_settings.CornerRadius * scale, Math.Min(ew, eh) * 0.5f);

            u["uElementPos"] = new[] { px, py };
            u["uElementSize"] = new[] { ew, eh };
            u["uCornerRadii"] = new[] { radius, radius, radius, radius };
            u["uScale"] = scale;
            u["uRefractionHeight"] = _settings.RefractionHeight * scale;
            u["uRefractionAmount"] = _settings.RefractionAmount * scale;
            u["uDepthEffect"] = _settings.DepthEffect;
            u["uSaturation"] = _settings.Saturation;
            u["uDarkening"] = _settings.Darkening;
            u["uGlassTint"] = ColorToFloat4(_settings.GlassTint);

            // 纹理里真正有数据的范围（位图按 64 对齐放大过，且贴边时抓取区域会被裁剪）。
            // 着色器据此 clamp 采样坐标，防止窗口移到屏幕边缘时采到纹理外面。
            u["uTexSize"] = new[] { (float)Math.Max(1, _captured.Width), (float)Math.Max(1, _captured.Height) };

            if (_settings.UseDispersion)
            {
                u["uDispersion"] = _settings.Dispersion;
            }
        }

        private void SetHighlightUniforms(float scale)
        {
            var u = _uHighlight;
            if (u == null) return;

            float radius = Math.Min(_settings.CornerRadius * scale,
                Math.Min(_smooth.Width, _smooth.Height) * 0.5f);

            u["uElementSize"] = new[] { (float)_smooth.Width, (float)_smooth.Height };
            u["uCornerRadii"] = new[] { radius, radius, radius, radius };
            u["uScale"] = scale;
            u["uHighlightColor"] = ColorToFloat4(_settings.HighlightColor);
            u["uMousePos"] = new[] { _mouseDip.X * scale, _mouseDip.Y * scale };
            u["uSpotRadius"] = _settings.SpotRadius * scale;
        }

        private static float[] ColorToFloat4(SKColor c)
            => new[] { c.Red / 255.0f, c.Green / 255.0f, c.Blue / 255.0f, c.Alpha / 255.0f };

        // ---------------- 资源 ----------------
        private int MarginPixels(float scale)
        {
            float reach = _settings.RefractionAmount * (1.0f + Math.Abs(_settings.Dispersion))
                          + 4.0f * _settings.BlurSigma;
            return (int)Math.Ceiling((reach + 8.0f) * scale);
        }

        private static SKRectI Ease(SKRectI cur, SKRectI target, float t)
        {
            return new SKRectI(
                (int)Math.Round(cur.Left + (target.Left - cur.Left) * t),
                (int)Math.Round(cur.Top + (target.Top - cur.Top) * t),
                (int)Math.Round(cur.Right + (target.Right - cur.Right) * t),
                (int)Math.Round(cur.Bottom + (target.Bottom - cur.Bottom) * t));
        }

        private SKBitmap? EnsureBitmap(int w, int h)
        {
            w = Math.Max(1, w);
            h = Math.Max(1, h);

            if (_src != null && _src.Width == w && _src.Height == h) return _src;

            _src?.Dispose();
            _src = null;

            try
            {
                _src = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
            }
            catch (Exception ex)
            {
                Log.Once("bitmap-ex", "[renderer] 位图创建失败 " + w + "x" + h + ": " + ex.Message);
                _src = null;
            }

            return _src;
        }

        /// <summary>
        /// 模糊用的离屏表面。必须跟画布同一个后端：
        /// 有 GPU 时创建 GPU 表面（CPU 表面跑 SKSL 会因缺少 ByteCode 解释器而崩溃）。
        /// </summary>
        private void EnsureSurfaces(int w, int h, GRContext? grContext)
        {
            if (grContext == null) return;   // 无 GPU → 不走着色器路径，不需要离屏表面

            if (_blurTemp != null && _blurFinal != null &&
                _surfaceSize.Width == w && _surfaceSize.Height == h &&
                ReferenceEquals(_surfaceGr, grContext))
            {
                return;
            }

            _blurTemp?.Dispose();
            _blurFinal?.Dispose();
            _blurTemp = null;
            _blurFinal = null;

            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);

            try
            {
                _blurTemp = SKSurface.Create(grContext, false, info);
                _blurFinal = SKSurface.Create(grContext, false, info);
                _surfaceGr = grContext;
                _surfaceSize = new SKSizeI(w, h);

                Log.Once("gpu-surface", "[renderer] 已创建 GPU 离屏表面 " + w + "x" + h);
            }
            catch (Exception ex)
            {
                Log.Write("[renderer] GPU 离屏表面创建失败: " + ex.Message);
                _blurTemp = null;
                _blurFinal = null;
            }
        }

        public void Dispose()
        {
            _blurTemp?.Dispose();
            _blurFinal?.Dispose();
            _src?.Dispose();

            _blurEffect?.Dispose();
            _glassEffect?.Dispose();
            _highlightEffect?.Dispose();

            _blurTemp = null;
            _blurFinal = null;
            _src = null;
            _blurEffect = null;
            _glassEffect = null;
            _highlightEffect = null;
        }
    }
}
