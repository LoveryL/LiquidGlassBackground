using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace LiquidGlass
{
    /// <summary>
    /// 开箱即用的液态玻璃窗口：继承 Avalonia 的 <see cref="Window"/>，
    /// 默认就是「无边框 + 全透明 + 玻璃背景」，内容直接写在标签里即可：
    /// <code>
    /// &lt;lg:LiquidGlassWindow xmlns:lg="using:LiquidGlass" CornerRadius="28" Width="520" Height="320"&gt;
    ///     &lt;TextBlock Text="Hello Liquid Glass" /&gt;
    /// &lt;/lg:LiquidGlassWindow&gt;
    /// </code>
    /// 也可以给普通 Window 手动套一层 <see cref="LiquidGlassPanel"/>，效果完全一样。
    /// 注意：窗口内容需要是控件（Grid / StackPanel / TextBlock …）。
    /// </summary>
    public class LiquidGlassWindow : Window
    {
        #region 玻璃参数（转发给内部 LiquidGlassPanel）

        // 说明：TemplatedControl 已有 CornerRadius（类型 CornerRadius 结构体）。
        // 这里刻意用 double 版本隐藏它 —— AXAML 里写 CornerRadius="30" 时命中本类型上的这个属性。
        public static readonly new StyledProperty<double> CornerRadiusProperty =
            LiquidGlassPanel.CornerRadiusProperty.AddOwner<LiquidGlassWindow>();

        public static readonly StyledProperty<double> RefractionHeightProperty =
            LiquidGlassPanel.RefractionHeightProperty.AddOwner<LiquidGlassWindow>();

        public static readonly StyledProperty<double> RefractionAmountProperty =
            LiquidGlassPanel.RefractionAmountProperty.AddOwner<LiquidGlassWindow>();

        public static readonly StyledProperty<double> DepthEffectProperty =
            LiquidGlassPanel.DepthEffectProperty.AddOwner<LiquidGlassWindow>();

        public static readonly StyledProperty<double> SaturationProperty =
            LiquidGlassPanel.SaturationProperty.AddOwner<LiquidGlassWindow>();

        public static readonly StyledProperty<double> DispersionProperty =
            LiquidGlassPanel.DispersionProperty.AddOwner<LiquidGlassWindow>();

        public static readonly StyledProperty<double> DarkeningProperty =
            LiquidGlassPanel.DarkeningProperty.AddOwner<LiquidGlassWindow>();

        public static readonly StyledProperty<Color> GlassTintProperty =
            LiquidGlassPanel.GlassTintProperty.AddOwner<LiquidGlassWindow>();

        public static readonly StyledProperty<double> BlurSigmaProperty =
            LiquidGlassPanel.BlurSigmaProperty.AddOwner<LiquidGlassWindow>();

        public static readonly StyledProperty<int> BlurRadiusProperty =
            LiquidGlassPanel.BlurRadiusProperty.AddOwner<LiquidGlassWindow>();

        public static readonly StyledProperty<double> EaseSpeedProperty =
            LiquidGlassPanel.EaseSpeedProperty.AddOwner<LiquidGlassWindow>();

        public static readonly StyledProperty<bool> UseDispersionProperty =
            LiquidGlassPanel.UseDispersionProperty.AddOwner<LiquidGlassWindow>();

        public static readonly StyledProperty<bool> EnableHighlightProperty =
            LiquidGlassPanel.EnableHighlightProperty.AddOwner<LiquidGlassWindow>();

        public static readonly StyledProperty<Color> HighlightColorProperty =
            LiquidGlassPanel.HighlightColorProperty.AddOwner<LiquidGlassWindow>();

        public static readonly StyledProperty<double> SpotRadiusProperty =
            LiquidGlassPanel.SpotRadiusProperty.AddOwner<LiquidGlassWindow>();

        public static readonly StyledProperty<int> FrameRateProperty =
            LiquidGlassPanel.FrameRateProperty.AddOwner<LiquidGlassWindow>();

        public static readonly StyledProperty<GlassCaptureMode> CaptureModeProperty =
            LiquidGlassPanel.CaptureModeProperty.AddOwner<LiquidGlassWindow>();

        /// <summary>玻璃是否运行（false = 暂停抓屏）。刻意不叫 IsActive —— 那会撞 WindowBase.IsActive。</summary>
        public static readonly StyledProperty<bool> IsGlassActiveProperty =
            LiquidGlassPanel.IsGlassActiveProperty.AddOwner<LiquidGlassWindow>();

        public static readonly StyledProperty<bool> ShowDiagnosticsProperty =
            LiquidGlassPanel.ShowDiagnosticsProperty.AddOwner<LiquidGlassWindow>();

        public static readonly StyledProperty<bool> ShowPlaceholderProperty =
            LiquidGlassPanel.ShowPlaceholderProperty.AddOwner<LiquidGlassWindow>();

        /// <summary>是否由本窗口自动接管「无边框 + 透明 + 可拖动」（默认 true）。</summary>
        public static readonly StyledProperty<bool> IsGlassShellEnabledProperty =
            AvaloniaProperty.Register<LiquidGlassWindow, bool>(nameof(IsGlassShellEnabled), true);

        /// <summary>
        /// 调试快捷键（默认关闭）。打开后：F1 诊断叠加 / F2 洋红纯色（验证渲染链路）/ F3 强制降级 / Esc 关闭。
        /// 排查「窗口看不见」时强烈建议临时打开。
        /// </summary>
        public static readonly StyledProperty<bool> EnableDebugKeysProperty =
            AvaloniaProperty.Register<LiquidGlassWindow, bool>(nameof(EnableDebugKeys), false);

        public new double CornerRadius { get => GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
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
        public bool IsGlassShellEnabled { get => GetValue(IsGlassShellEnabledProperty); set => SetValue(IsGlassShellEnabledProperty, value); }
        public bool EnableDebugKeys { get => GetValue(EnableDebugKeysProperty); set => SetValue(EnableDebugKeysProperty, value); }

        #endregion

        private LiquidGlassPanel? _glass;

        /// <summary>内部的玻璃面板（窗口打开后可用）。</summary>
        public LiquidGlassPanel? Glass => _glass;

        static LiquidGlassWindow()
        {
            CornerRadiusProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ForwardToPanel());
            RefractionHeightProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ForwardToPanel());
            RefractionAmountProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ForwardToPanel());
            DepthEffectProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ForwardToPanel());
            SaturationProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ForwardToPanel());
            DispersionProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ForwardToPanel());
            DarkeningProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ForwardToPanel());
            GlassTintProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ForwardToPanel());
            BlurSigmaProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ForwardToPanel());
            BlurRadiusProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ForwardToPanel());
            EaseSpeedProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ForwardToPanel());
            UseDispersionProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ForwardToPanel());
            EnableHighlightProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ForwardToPanel());
            HighlightColorProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ForwardToPanel());
            SpotRadiusProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ForwardToPanel());
            FrameRateProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ForwardToPanel());
            CaptureModeProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ForwardToPanel());
            IsGlassActiveProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ForwardToPanel());
            ShowDiagnosticsProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ForwardToPanel());
            ShowPlaceholderProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ForwardToPanel());
            IsGlassShellEnabledProperty.Changed.AddClassHandler<LiquidGlassWindow>((w, _) => w.ApplyShell());
        }

        public LiquidGlassWindow()
        {
            ApplyShell();
        }

        private void ApplyShell()
        {
            if (!IsGlassShellEnabled) return;

            Background = Brushes.Transparent;
            // Avalonia 11 的 TransparencyLevelHint 是「按优先级排列的候选列表」，不是单个值
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            SystemDecorations = SystemDecorations.None;

            // ★ 关键：SystemDecorations=None 时【不要】再开 ExtendClientAreaToDecorationsHint。
            // 两者同时生效会让客户区尺寸计算异常 —— 表现就是窗口"缩没了"、却没有任何异常抛出。
            ExtendClientAreaToDecorationsHint = false;

            Topmost = false;

            // 无边框窗口若没显式给尺寸，某些平台会拿到 0 尺寸，同样表现为"窗口不见了"
            if (double.IsNaN(Width) || Width <= 0) Width = 640;
            if (double.IsNaN(Height) || Height <= 0) Height = 420;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            EnsureGlassHost();
        }

        /// <summary>把用户在 AXAML 里写的内容装进玻璃面板。</summary>
        private void EnsureGlassHost()
        {
            if (_glass != null) return;

            if (Content is LiquidGlassPanel existing)
            {
                _glass = existing;
                ForwardToPanel();
                return;
            }

            var inner = Content as Control;

            var panel = new LiquidGlassPanel();

            // 先把 inner 从 Window 上摘下来（避免同一控件同时挂两个父），再装进玻璃面板
            if (inner != null) Content = null;

            if (inner != null)
            {
                try
                {
                    panel.Children.Add(inner);
                }
                catch (Exception ex)
                {
                    Log.Write("[window] 内容迁移失败: " + ex.Message);
                }
            }

            Content = panel;
            _glass = panel;

            ForwardToPanel();

            Log.Write("[window] 玻璃窗口就绪，抓屏=" + panel.CaptureName +
                      "，窗口=" + Bounds.Width.ToString("0") + "x" + Bounds.Height.ToString("0") +
                      "，实际透明级别=" + ActualTransparencyLevel +
                      "，缩放=" + RenderScaling.ToString("0.00"));

            // 尺寸为 0 = 窗口在屏幕上是"隐形"的，这是最高频的"窗口没了"原因
            if (Bounds.Width <= 1 || Bounds.Height <= 1)
            {
                Log.Write("[window] ⚠ 窗口尺寸接近 0！请在 AXAML 上显式写 Width/Height，" +
                          "或确认 IsGlassShellEnabled 时没有同时开启 ExtendClientAreaToDecorationsHint。");
            }
        }

        private void ForwardToPanel()
        {
            var p = _glass;
            if (p == null) return;

            p.SetValue(LiquidGlassPanel.CornerRadiusProperty, CornerRadius);
            p.SetValue(LiquidGlassPanel.RefractionHeightProperty, RefractionHeight);
            p.SetValue(LiquidGlassPanel.RefractionAmountProperty, RefractionAmount);
            p.SetValue(LiquidGlassPanel.DepthEffectProperty, DepthEffect);
            p.SetValue(LiquidGlassPanel.SaturationProperty, Saturation);
            p.SetValue(LiquidGlassPanel.DispersionProperty, Dispersion);
            p.SetValue(LiquidGlassPanel.DarkeningProperty, Darkening);
            p.SetValue(LiquidGlassPanel.GlassTintProperty, GlassTint);
            p.SetValue(LiquidGlassPanel.BlurSigmaProperty, BlurSigma);
            p.SetValue(LiquidGlassPanel.BlurRadiusProperty, BlurRadius);
            p.SetValue(LiquidGlassPanel.EaseSpeedProperty, EaseSpeed);
            p.SetValue(LiquidGlassPanel.UseDispersionProperty, UseDispersion);
            p.SetValue(LiquidGlassPanel.EnableHighlightProperty, EnableHighlight);
            p.SetValue(LiquidGlassPanel.HighlightColorProperty, HighlightColor);
            p.SetValue(LiquidGlassPanel.SpotRadiusProperty, SpotRadius);
            p.SetValue(LiquidGlassPanel.FrameRateProperty, FrameRate);
            p.SetValue(LiquidGlassPanel.CaptureModeProperty, CaptureMode);
            p.SetValue(LiquidGlassPanel.IsGlassActiveProperty, IsGlassActive);
            p.SetValue(LiquidGlassPanel.ShowDiagnosticsProperty, ShowDiagnostics);
            p.SetValue(LiquidGlassPanel.ShowPlaceholderProperty, ShowPlaceholder);
        }

        // 调试快捷键：F1 诊断 / F2 洋红纯色 / F3 强制降级 / Esc 关闭
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!EnableDebugKeys)
            {
                base.OnKeyDown(e);
                return;
            }

            var r = _glass?.Renderer;

            switch (e.Key)
            {
                case Key.F1:
                    ShowDiagnostics = !ShowDiagnostics;
                    Log.Write("[key] F1 诊断=" + ShowDiagnostics + " | " + (r?.Status ?? "(无渲染器)"));
                    e.Handled = true;
                    return;

                case Key.F2:
                    if (r != null)
                    {
                        r.TestFill = !r.TestFill;
                        Log.Write("[key] F2 洋红纯色=" + r.TestFill + "（能看到洋红 = 渲染链路是通的）");
                    }
                    e.Handled = true;
                    return;

                case Key.F3:
                    if (r != null)
                    {
                        r.ForceFallback = !r.ForceFallback;
                        Log.Write("[key] F3 强制降级=" + r.ForceFallback);
                    }
                    e.Handled = true;
                    return;

                case Key.Escape:
                    Close();
                    e.Handled = true;
                    return;
            }

            base.OnKeyDown(e);
        }

        // 空白处按住左键拖动窗口（无边框窗口没有标题栏）
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (IsGlassShellEnabled &&
                e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }
    }
}
