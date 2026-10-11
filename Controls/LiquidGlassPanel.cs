using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace LiquidGlass
{
    /// <summary>
    /// 液态玻璃背景面板。
    /// 在 AXAML 里直接当容器使用，玻璃画在所有子控件下面：
    /// <code>
    /// &lt;lg:LiquidGlassPanel CornerRadius="24" BlurSigma="2"&gt;
    ///     &lt;TextBlock Text="Hello Liquid Glass" /&gt;
    /// &lt;/lg:LiquidGlassPanel&gt;
    /// </code>
    /// 宿主窗口需要透明背景（TransparencyLevelHint="Transparent" Background="Transparent"），
    /// 且必须引用 Avalonia.Skia。
    ///
    /// 实现说明：Avalonia 11 的 <see cref="Panel"/> 把 Render 封死了（sealed），
    /// 所以玻璃实际由内部的 GlassLayer（一个 Control，永远排在 Children[0]）绘制，
    /// 从而天然位于其它子控件之下。
    /// </summary>
    public class LiquidGlassPanel : Panel
    {
        #region 玻璃参数（StyledProperty，支持 AXAML 赋值与绑定）

        public static readonly StyledProperty<double> CornerRadiusProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, double>(nameof(CornerRadius), 20d);

        public static readonly StyledProperty<double> RefractionHeightProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, double>(nameof(RefractionHeight), 20d);

        public static readonly StyledProperty<double> RefractionAmountProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, double>(nameof(RefractionAmount), 50d);

        public static readonly StyledProperty<double> DepthEffectProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, double>(nameof(DepthEffect), 0.8d);

        public static readonly StyledProperty<double> SaturationProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, double>(nameof(Saturation), 1.2d);

        public static readonly StyledProperty<double> DispersionProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, double>(nameof(Dispersion), 0.4d);

        public static readonly StyledProperty<double> DarkeningProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, double>(nameof(Darkening), 0.9d);

        public static readonly StyledProperty<Color> GlassTintProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, Color>(nameof(GlassTint),
                Color.FromArgb(26, 204, 204, 204));

        public static readonly StyledProperty<double> BlurSigmaProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, double>(nameof(BlurSigma), 1.0d);

        public static readonly StyledProperty<int> BlurRadiusProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, int>(nameof(BlurRadius), 7);

        public static readonly StyledProperty<double> EaseSpeedProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, double>(nameof(EaseSpeed), 0.5d);

        public static readonly StyledProperty<bool> UseDispersionProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, bool>(nameof(UseDispersion), true);

        public static readonly StyledProperty<bool> EnableHighlightProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, bool>(nameof(EnableHighlight), false);

        public static readonly StyledProperty<Color> HighlightColorProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, Color>(nameof(HighlightColor),
                Color.FromArgb(48, 255, 255, 255));

        public static readonly StyledProperty<double> SpotRadiusProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, double>(nameof(SpotRadius), 180d);

        /// <summary>抓屏帧率（默认 30fps）。设为 0 表示不自动刷新（只在需要时手动调 Refresh）。</summary>
        public static readonly StyledProperty<int> FrameRateProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, int>(nameof(FrameRate), 30);

        /// <summary>抓屏后端，一般保持 Auto 即可。</summary>
        public static readonly StyledProperty<GlassCaptureMode> CaptureModeProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, GlassCaptureMode>(nameof(CaptureMode), GlassCaptureMode.Auto);

        /// <summary>玻璃是否运行（false = 暂停抓屏与重绘，保持最后一帧）。</summary>
        public static readonly StyledProperty<bool> IsGlassActiveProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, bool>(nameof(IsGlassActive), true);

        /// <summary>在左上角叠加诊断信息（抓屏实现 / 是否走着色器 / SKSL 错误）。</summary>
        public static readonly StyledProperty<bool> ShowDiagnosticsProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, bool>(nameof(ShowDiagnostics), false);

        /// <summary>
        /// 窗口/控件不可见时自动暂停抓屏（默认 true）。
        /// 反复 show/hide 的场景（通知弹窗）务必保持开启 —— 隐藏时还在抓屏既浪费又容易卡。
        /// </summary>
        public static readonly StyledProperty<bool> PauseWhenHiddenProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, bool>(nameof(PauseWhenHidden), true);

        /// <summary>
        /// 抓屏未就绪时是否画「占位面板」（默认 true）。
        /// 关掉后抓屏失败就完全透明 —— 适合不想看到灰板、宁可让内容直接浮在桌面上的场景。
        /// </summary>
        public static readonly StyledProperty<bool> ShowPlaceholderProperty =
            AvaloniaProperty.Register<LiquidGlassPanel, bool>(nameof(ShowPlaceholder), true);

        public double CornerRadius { get => GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
        public double RefractionHeight { get => GetValue(RefractionHeightProperty); set => SetValue(RefractionHeightProperty, value); }
        public double RefractionAmount { get => GetValue(RefractionAmountProperty); set => SetValue(RefractionAmountProperty, value); }
        public double DepthEffect { get => GetValue(DepthEffectProperty); set => SetValue(DepthEffectProperty, value); }
        public double Saturation { get => GetValue(SaturationProperty); set => SetValue(SaturationProperty, value); }
        public double Dispersion { get => GetValue(DispersionProperty); set => SetValue(DispersionProperty, value); }
        public double Darkening { get => GetValue(DarkeningProperty); set => SetValue(DarkeningProperty, value); }
        public Color GlassTint { get => GetValue(GlassTintProperty); set => SetValue(GlassTintProperty, value); }
        public double BlurSigma { get => GetValue(BlurSigmaProperty); set => SetValue(BlurSigmaProperty, value); }
        public int BlurRadius { get => GetValue(BlurRadiusProperty); set => SetValue(BlurRadiusProperty, value); }
        public double EaseSpeed { get => GetValue(EaseSpeedProperty); set => SetValue(EaseSpeedProperty, value); }
        public bool UseDispersion { get => GetValue(UseDispersionProperty); set => SetValue(UseDispersionProperty, value); }
        public bool EnableHighlight { get => GetValue(EnableHighlightProperty); set => SetValue(EnableHighlightProperty, value); }
        public Color HighlightColor { get => GetValue(HighlightColorProperty); set => SetValue(HighlightColorProperty, value); }
        public double SpotRadius { get => GetValue(SpotRadiusProperty); set => SetValue(SpotRadiusProperty, value); }
        public int FrameRate { get => GetValue(FrameRateProperty); set => SetValue(FrameRateProperty, value); }
        public GlassCaptureMode CaptureMode { get => GetValue(CaptureModeProperty); set => SetValue(CaptureModeProperty, value); }
        public bool IsGlassActive { get => GetValue(IsGlassActiveProperty); set => SetValue(IsGlassActiveProperty, value); }
        public bool ShowDiagnostics { get => GetValue(ShowDiagnosticsProperty); set => SetValue(ShowDiagnosticsProperty, value); }
        public bool ShowPlaceholder { get => GetValue(ShowPlaceholderProperty); set => SetValue(ShowPlaceholderProperty, value); }
        public bool PauseWhenHidden { get => GetValue(PauseWhenHiddenProperty); set => SetValue(PauseWhenHiddenProperty, value); }

        #endregion

        private readonly GlassSettings _settings = GlassSettings.CreateDefault();
        private readonly GlassLayer _layer;

        // 抓屏未就绪时的占位外观（静态复用，避免每帧 new）
        private static readonly SolidColorBrush PlaceholderFill =
            new SolidColorBrush(Color.FromArgb(150, 232, 236, 246));
        private static readonly Pen PlaceholderPen =
            new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 2);

        private IntPtr _hwnd;
        private Window? _hostWindow;
        private IScreenCapture? _capture;
        private LiquidGlassRenderer? _renderer;
        private DispatcherTimer? _timer;
        private bool _started;
        private bool _retryScheduled;
        private bool _captureReady;
        private bool _fixingOrder;

        /// <summary>当前渲染器（未就绪时为 null）。</summary>
        public LiquidGlassRenderer? Renderer => _renderer;

        /// <summary>当前抓屏实现名（诊断用）。</summary>
        public string CaptureName => _capture?.Name ?? "(未初始化)";

        static LiquidGlassPanel()
        {
            CornerRadiusProperty.Changed.AddClassHandler<LiquidGlassPanel>((p, _) => p.SyncSettings());
            RefractionHeightProperty.Changed.AddClassHandler<LiquidGlassPanel>((p, _) => p.SyncSettings());
            RefractionAmountProperty.Changed.AddClassHandler<LiquidGlassPanel>((p, _) => p.SyncSettings());
            DepthEffectProperty.Changed.AddClassHandler<LiquidGlassPanel>((p, _) => p.SyncSettings());
            SaturationProperty.Changed.AddClassHandler<LiquidGlassPanel>((p, _) => p.SyncSettings());
            DispersionProperty.Changed.AddClassHandler<LiquidGlassPanel>((p, _) => p.SyncSettings());
            DarkeningProperty.Changed.AddClassHandler<LiquidGlassPanel>((p, _) => p.SyncSettings());
            GlassTintProperty.Changed.AddClassHandler<LiquidGlassPanel>((p, _) => p.SyncSettings());
            BlurSigmaProperty.Changed.AddClassHandler<LiquidGlassPanel>((p, _) => p.SyncSettings());
            BlurRadiusProperty.Changed.AddClassHandler<LiquidGlassPanel>((p, _) => p.SyncSettings());
            EaseSpeedProperty.Changed.AddClassHandler<LiquidGlassPanel>((p, _) => p.SyncSettings());
            UseDispersionProperty.Changed.AddClassHandler<LiquidGlassPanel>((p, _) => p.SyncSettings());
            EnableHighlightProperty.Changed.AddClassHandler<LiquidGlassPanel>((p, _) => p.SyncSettings());
            HighlightColorProperty.Changed.AddClassHandler<LiquidGlassPanel>((p, _) => p.SyncSettings());
            SpotRadiusProperty.Changed.AddClassHandler<LiquidGlassPanel>((p, _) => p.SyncSettings());
            ShowDiagnosticsProperty.Changed.AddClassHandler<LiquidGlassPanel>((p, _) => p.SyncSettings());

            FrameRateProperty.Changed.AddClassHandler<LiquidGlassPanel>((p, _) => p.SyncTimer());
            IsGlassActiveProperty.Changed.AddClassHandler<LiquidGlassPanel>((p, _) => p.SyncTimer());
            CaptureModeProperty.Changed.AddClassHandler<LiquidGlassPanel>((p, _) => p.OnCaptureModeChanged());
        }

        public LiquidGlassPanel()
        {
            ClipToBounds = true;

            // 可见性变化通过 OnPropertyChanged 监听（Avalonia 11 已移除 IsVisibleChanged 事件）

            // 玻璃层：永远排在 Children[0]，于是天然位于其它子控件之下
            _layer = new GlassLayer(this);
            Children.Add(_layer);
            Children.CollectionChanged += OnChildrenChanged;

            SyncSettings();
        }

        // ---------------- 生命周期 ----------------

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            _hostWindow = e.Root as Window;
            if (_hostWindow != null)
            {
                _hostWindow.Opened += OnHostWindowOpened;
                _hostWindow.Closed += OnHostWindowClosed;
            }

            TryStart();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            if (_hostWindow != null)
            {
                _hostWindow.Opened -= OnHostWindowOpened;
                _hostWindow.Closed -= OnHostWindowClosed;
                _hostWindow = null;
            }

            Stop();
        }

        /// <summary>当前是否"对外可见"：自身可见 && 宿主窗口可见（若有）。</summary>
        private bool IsEffectivelyShown
        {
            get
            {
                if (!IsVisible || !IsEffectivelyVisible) return false;

                var w = _hostWindow;
                if (w != null && !w.IsVisible) return false;

                return true;
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            // Avalonia 11 没有 IsVisibleChanged 事件，靠属性变更通知来监听
            if (change.Property == IsVisibleProperty && PauseWhenHidden)
            {
                bool shown = IsEffectivelyShown;

                Log.Write("[panel] 可见性变化：shown=" + shown + "，抓屏" + (shown ? "恢复" : "暂停"));

                if (shown)
                {
                    SyncTimer();     // 重新可见 → 恢复计时
                }
                else
                {
                    _timer?.Stop();  // 隐藏 → 立刻停表，不再抓屏
                }
            }
        }

        private void OnHostWindowOpened(object? sender, EventArgs e) => TryStart();

        private void OnHostWindowClosed(object? sender, EventArgs e) => Stop();

        private void TryStart()
        {
            if (_started) return;

            var hwnd = GetHostHandle();
            if (hwnd == IntPtr.Zero)
            {
                // 顶层窗口还没建好平台句柄，下一轮再试（Opened 事件会兜底）
                ScheduleRetry();
                return;
            }

            Start(hwnd);
        }

        private void ScheduleRetry()
        {
            if (_retryScheduled) return;
            _retryScheduled = true;

            Dispatcher.UIThread.Post(() =>
            {
                _retryScheduled = false;
                if (!_started) TryStart();
            }, DispatcherPriority.Loaded);
        }

        private void Start(IntPtr hwnd)
        {
            try
            {
                _hwnd = hwnd;

                _capture = ScreenCaptureFactory.Acquire(hwnd, CaptureMode);
                _renderer = new LiquidGlassRenderer(_capture, GetGeometry, _settings);
                _captureReady = true;
                _started = true;

                SyncSettings();
                SyncTimer();

                Log.Write("[panel] 启动完成，hwnd=0x" + hwnd.ToInt64().ToString("X") +
                          "，模式=" + CaptureMode +
                          "，抓屏=" + _capture.Name +
                          "，屏幕=" + _capture.ScreenBounds.Width + "x" + _capture.ScreenBounds.Height);

                WarnIfHostNotTransparent();
            }
            catch (Exception ex)
            {
                Log.Write("[panel] 启动失败: " + ex.Message);
                Stop();
            }

            InvalidateGlass();
        }

        /// <summary>
        /// 「窗口看不见 / 玻璃没效果」的头号原因就是宿主窗口其实不透明。
        /// 这里主动检查并打一条明确的日志，省得靠猜。
        /// </summary>
        private void WarnIfHostNotTransparent()
        {
            var w = _hostWindow;
            if (w == null) return;

            try
            {
                bool bgOk = true;
                string bgDesc = "(null)";

                if (w.Background != null)
                {
                    bgDesc = w.Background.ToString() ?? "?";
                    if (w.Background is ISolidColorBrush sb && sb.Color.A >= 255)
                    {
                        bgOk = false;
                    }
                }

                bool hintOk = false;
                string hintDesc = "(空)";
                var hint = w.TransparencyLevelHint;
                if (hint != null && hint.Count > 0)
                {
                    hintDesc = string.Join(",", hint);
                    foreach (var lvl in hint)
                    {
                        if (lvl == WindowTransparencyLevel.Transparent ||
                            lvl == WindowTransparencyLevel.Blur ||
                            lvl == WindowTransparencyLevel.AcrylicBlur ||
                            lvl == WindowTransparencyLevel.Mica)
                        {
                            hintOk = true;
                        }
                    }
                }

                if (!bgOk || !hintOk)
                {
                    Log.Write("[panel] ⚠ 宿主窗口可能不透明：Background=" + bgDesc +
                              "，TransparencyLevelHint=" + hintDesc +
                              "。玻璃需要透明窗口才看得见，请给宿主 Window 加 " +
                              "Background=\"Transparent\" 与 TransparencyLevelHint=\"Transparent\"。");
                }
                else
                {
                    Log.Write("[panel] 宿主透明性检查通过：Background=" + bgDesc +
                              "，TransparencyLevelHint=" + hintDesc);
                }

                Log.Once("actual-transparency", "[panel] 实际透明级别=" + w.ActualTransparencyLevel +
                                               "（若为 None/Opaque 说明平台没给到透明，玻璃会看不见）");
            }
            catch (Exception ex)
            {
                Log.Once("transparency-check-ex", "[panel] 透明性检查异常: " + ex.Message);
            }
        }

        private void Stop()
        {
            _started = false;
            _captureReady = false;

            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnTick;
                _timer = null;
            }

            if (_renderer != null)
            {
                _renderer.Dispose();
                _renderer = null;
            }

            if (_capture != null)
            {
                if (_hwnd != IntPtr.Zero) ScreenCaptureFactory.Release(_hwnd);
                _capture = null;
            }

            _hwnd = IntPtr.Zero;
        }

        private void OnCaptureModeChanged()
        {
            if (!_started || _hwnd == IntPtr.Zero) return;

            // 抓屏后端切换 = 重建整条链路
            Stop();
            Dispatcher.UIThread.Post(TryStart, DispatcherPriority.Loaded);
        }

        private IntPtr GetHostHandle()
        {
            var topLevel = VisualRoot as TopLevel;
            if (topLevel == null) return IntPtr.Zero;

            try
            {
                var handle = topLevel.TryGetPlatformHandle();
                if (handle != null && handle.Handle != IntPtr.Zero)
                {
                    return handle.Handle;
                }
            }
            catch (Exception ex)
            {
                Log.Once("hwnd-ex", "[panel] 获取平台句柄失败: " + ex.Message);
            }

            return IntPtr.Zero;
        }

        // ---------------- 参数同步 ----------------

        private void SyncSettings()
        {
            _settings.CornerRadius = (float)CornerRadius;
            _settings.RefractionHeight = (float)RefractionHeight;
            _settings.RefractionAmount = (float)RefractionAmount;
            _settings.DepthEffect = (float)DepthEffect;
            _settings.Saturation = (float)Saturation;
            _settings.Dispersion = (float)Dispersion;
            _settings.Darkening = (float)Darkening;
            _settings.GlassTint = ColorUtil.ToSk(GlassTint);
            _settings.BlurSigma = (float)BlurSigma;
            _settings.BlurRadius = BlurRadius;
            _settings.EaseSpeed = (float)EaseSpeed;
            _settings.UseDispersion = UseDispersion;
            _settings.EnableHighlight = EnableHighlight;
            _settings.HighlightColor = ColorUtil.ToSk(HighlightColor);
            _settings.SpotRadius = (float)SpotRadius;

            var r = _renderer;
            if (r != null)
            {
                r.ShowStatus = ShowDiagnostics;
                r.SettingsChanged();
            }

            InvalidateGlass();
        }

        private void SyncTimer()
        {
            int fps = FrameRate;
            bool wantRun = IsGlassActive && _started && fps > 0
                           && (!PauseWhenHidden || IsEffectivelyShown);

            if (!wantRun)
            {
                _timer?.Stop();
                return;
            }

            double intervalMs = 1000.0 / fps;

            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(intervalMs)
                };
                _timer.Tick += OnTick;
            }
            else if (Math.Abs(_timer.Interval.TotalMilliseconds - intervalMs) > 0.5)
            {
                _timer.Interval = TimeSpan.FromMilliseconds(intervalMs);
            }

            if (!_timer.IsEnabled) _timer.Start();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            var r = _renderer;
            if (r == null || !IsGlassActive) return;

            // 隐藏时不要抓屏：Magnification 的 UpdateWindow 是同步阻塞调用，
            // 窗口不可见时还去抓，既得不到有用画面又拖慢整个 UI 线程。
            // 注意：这里只跳过、不停表 —— 宿主窗口隐藏时面板自身 IsVisible 可能仍是 true，
            // 停表后就再没人负责重启。让表继续跑，开销可忽略。
            if (PauseWhenHidden && !IsEffectivelyShown) return;

            r.Tick();
            InvalidateGlass();
        }

        /// <summary>手动刷新一帧（FrameRate=0 时用）。</summary>
        public void Refresh()
        {
            _renderer?.Tick();
            InvalidateGlass();
        }

        private void InvalidateGlass() => _layer.InvalidateVisual();

        // ---------------- 布局 ----------------

        /// <summary>玻璃层不参与测量（尺寸恒为 0），由面板在 Arrange 时铺满。</summary>
        protected override Size ArrangeOverride(Size finalSize)
        {
            var result = base.ArrangeOverride(finalSize);

            // Panel 的排列策略各版本略有差异，这里强制把玻璃层铺满，保证不漏边
            _layer.Arrange(new Rect(finalSize));

            return result;
        }

        private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_fixingOrder) return;

            // 玻璃层必须始终在最底层，否则会盖住用户内容
            if (Children.Count == 0 || !Children.Contains(_layer))
            {
                _fixingOrder = true;
                try
                {
                    Children.Insert(0, _layer);
                }
                finally
                {
                    _fixingOrder = false;
                }
            }
        }

        // ---------------- 几何 ----------------

        /// <summary>面板在屏幕上的物理像素矩形 + DPI 缩放比。</summary>
        private WindowGeometry GetGeometry()
        {
            double dipW = Bounds.Width;
            double dipH = Bounds.Height;

            var root = VisualRoot;
            double scale = 1.0;
            if (root != null)
            {
                double rs = root.RenderScaling;
                scale = rs > 0.1 ? rs : 1.0;
            }

            // 相对窗口内容区左上角的偏移（DIP）：沿视觉树累加父级 Bounds.X/Y。
            // 不用 TransformToVisual —— 该 API 在不同 Avalonia 版本间签名不稳定。
            double offX = 0, offY = 0;
            try
            {
                // Avalonia 11 起 IVisual 被移除，视觉父级要靠扩展方法：using Avalonia.VisualTree;
                Visual? cur = this;
                while (cur != null && !ReferenceEquals(cur, root))
                {
                    offX += cur.Bounds.X;
                    offY += cur.Bounds.Y;
                    cur = cur.GetVisualParent() as Visual;
                }
            }
            catch (Exception ex)
            {
                Log.Once("transform-ex", "[panel] 坐标变换失败: " + ex.Message);
            }

            // 内容区左上角在屏幕上的物理坐标
            int originX = 0, originY = 0;
            bool gotOrigin = false;

            if (_hwnd != IntPtr.Zero && OperatingSystem.IsWindows())
            {
                try
                {
                    var p = new NativeMethods.POINT { x = 0, y = 0 };
                    if (NativeMethods.ClientToScreen(_hwnd, ref p))
                    {
                        originX = p.x;
                        originY = p.y;
                        gotOrigin = true;
                    }
                }
                catch
                {
                    // ignored
                }
            }

            if (!gotOrigin && root is Window w)
            {
                originX = w.Position.X;
                originY = w.Position.Y;
            }

            int left = originX + (int)Math.Round(offX * scale);
            int top = originY + (int)Math.Round(offY * scale);
            int right = left + (int)Math.Round(dipW * scale);
            int bottom = top + (int)Math.Round(dipH * scale);

            return new WindowGeometry(
                new SKRectI(left, top, Math.Max(right, left + 1), Math.Max(bottom, top + 1)),
                scale);
        }

        // ---------------- 绘制（由 GlassLayer 调用） ----------------

        private void DrawGlass(DrawingContext context)
        {
            var w = Bounds.Width;
            var h = Bounds.Height;
            if (w <= 0.5 || h <= 0.5) return;

            var r = _renderer;
            if (r == null || !_captureReady)
            {
                // 抓屏链路还没起来：也要画可见内容，否则透明窗口 = 完全隐形
                Log.Once("no-capture-yet", "[panel] 抓屏未就绪（renderer=" +
                                           (r == null ? "null" : "ok") + "，captureReady=" + _captureReady +
                                           "），ShowPlaceholder=" + ShowPlaceholder);

                if (ShowPlaceholder) DrawPlaceholder(context, w, h);
                return;
            }

            context.Custom(new GlassDrawOperation(new Rect(0, 0, w, h), r));
        }

        private void DrawPlaceholder(DrawingContext context, double w, double h)
        {
            float radius = (float)Math.Min(CornerRadius, Math.Min(w, h) * 0.5);
            var rect = new RoundedRect(new Rect(0, 0, w, h), radius, radius);

            // SolidColorBrush / Pen 都不实现 IDisposable，做成静态只读实例复用即可
            context.DrawRectangle(PlaceholderFill, PlaceholderPen, rect);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            var r = _renderer;
            if (r == null || !EnableHighlight) return;

            var p = e.GetPosition(this);
            r.SetMousePosition(new SKPoint((float)p.X, (float)p.Y));
        }

        /// <summary>
        /// 真正画玻璃的那一层。Avalonia 11 里 Panel.Render 是 sealed，
        /// 所以用一个 Control 子类来承载自定义绘制，并靠 Children[0] 保证在最底层。
        /// </summary>
        private sealed class GlassLayer : Control
        {
            private readonly LiquidGlassPanel _owner;

            public GlassLayer(LiquidGlassPanel owner)
            {
                _owner = owner;
                IsHitTestVisible = false;   // 不吃鼠标事件，永远让内容在上层
            }

            /// <summary>
            /// 玻璃层必须拿到【非零 Bounds】，否则 Avalonia 的渲染管线会直接跳过它的 Render
            /// —— 表现就是"窗口全透明、进程不崩、也没有任何异常"。
            /// 所以这里不能返回 0，尽量返回可用尺寸；Infinity 时退回宿主面板当前尺寸。
            /// </summary>
            protected override Size MeasureOverride(Size availableSize)
            {
                double w = double.IsInfinity(availableSize.Width) ? _owner.Bounds.Width : availableSize.Width;
                double h = double.IsInfinity(availableSize.Height) ? _owner.Bounds.Height : availableSize.Height;

                if (double.IsInfinity(w) || double.IsNaN(w)) w = 0;
                if (double.IsInfinity(h) || double.IsNaN(h)) h = 0;

                return new Size(w, h);
            }

            public override void Render(DrawingContext context)
            {
                Log.Once("layer-render", "[panel] 玻璃层开始渲染，layer.Bounds=" +
                                         Bounds.Width.ToString("0") + "x" + Bounds.Height.ToString("0") +
                                         "，owner.Bounds=" + _owner.Bounds.Width.ToString("0") + "x" +
                                         _owner.Bounds.Height.ToString("0"));

                _owner.DrawGlass(context);
            }
        }

        private sealed class GlassDrawOperation : ICustomDrawOperation
        {
            private readonly LiquidGlassRenderer _renderer;

            public GlassDrawOperation(Rect bounds, LiquidGlassRenderer renderer)
            {
                Bounds = bounds;
                _renderer = renderer;
            }

            public Rect Bounds { get; }

            public void Dispose()
            {
            }

            public bool HitTest(Point p) => false;

            public bool Equals(ICustomDrawOperation? other) => false;

            public void Render(ImmediateDrawingContext context)
            {
                // 注意：ImmediateDrawingContext.TryGetFeature 是非泛型方法（接收 Type），
                // 泛型版本不是这个类型上的成员，不能用类型参数调用。
                var leaseFeature = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature))
                                   as ISkiaSharpApiLeaseFeature;

                if (leaseFeature is null)
                {
                    Log.Once("no-skia", "[panel] 拿不到 ISkiaSharpApiLeaseFeature —— 当前渲染后端不是 Skia，" +
                                        "玻璃无法绘制。请确认已引用 Avalonia.Skia 且未强制使用软件/自定义后端。");
                    return;
                }

                using var lease = leaseFeature.Lease();
                var canvas = lease.SkCanvas;

                if (canvas is null)
                {
                    Log.Once("no-canvas", "[panel] lease.SkCanvas 为 null");
                    return;
                }

                // 关键：SkiaSharp 2.88 的 CPU 光栅后端【没有启用 SKSL ByteCode 解释器】，
                // 在 CPU 表面上跑 SKRuntimeEffect 会直接原生崩溃。
                // 所以必须把 GRContext 传给渲染器：有 GPU 才用着色器，没有就走纯 Skia 路径。
                var grContext = lease.GrContext;

                Log.Once("skia-ok", "[panel] Skia 画布可用，画布尺寸=" +
                                    canvas.LocalClipBounds.Width.ToString("0.0") + "x" +
                                    canvas.LocalClipBounds.Height.ToString("0.0") +
                                    "，GRContext=" + (grContext == null ? "null(软件渲染)" : "GPU 可用"));

                _renderer.Draw(canvas, grContext);
            }
        }
    }
}
