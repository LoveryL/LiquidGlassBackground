using Avalonia.Media;
using SkiaSharp;

namespace LiquidGlass
{
    /// <summary>
    /// 玻璃参数。数值与 C++ 版 Render() 里填的 GlassCB 一一对应。
    /// 长度类参数单位是「CSS 像素」（会自动乘以 DPI 缩放变成物理像素）。
    /// </summary>
    public sealed class GlassSettings
    {
        // ---------- 形状 ----------
        /// <summary>圆角半径（原 cornerRadii 四角均为 20）</summary>
        public float CornerRadius = 20f;

        // ---------- 折射 ----------
        /// <summary>从边缘往内多远开始产生折射（原 refractionHeight = 20）</summary>
        public float RefractionHeight = 20f;
        /// <summary>折射位移强度（原 refractionAmount = 50）</summary>
        public float RefractionAmount = 50f;
        /// <summary>SDF 梯度与「从中心向外」方向的混合比例（原 depthEffect = 0.8）</summary>
        public float DepthEffect = 0.8f;

        // ---------- 色彩 ----------
        /// <summary>饱和度（原 saturation = 1.2）</summary>
        public float Saturation = 1.2f;
        /// <summary>色散强度，0 = 关闭（原 dispersion = 0.4）</summary>
        public float Dispersion = 0.4f;
        /// <summary>整体压暗（原 darkening = 0.9）</summary>
        public float Darkening = 0.9f;
        /// <summary>玻璃染色，alpha 为混合权重（原 glassTint = 0.8,0.8,0.8,0.1）</summary>
        public SKColor GlassTint = new SKColor(204, 204, 204, 26);

        // ---------- 背景模糊 ----------
        /// <summary>高斯 sigma（原 sigma = 2.0）</summary>
        public float BlurSigma = 1.0f;
        /// <summary>核半径，权重在 CPU 侧按高斯公式算好（原 kernelRadius = 7）</summary>
        public int BlurRadius = 7;

        // ---------- 动效 ----------
        /// <summary>位置缓动系数（原 g_easeSpeed = 0.5），0 = 不做缓动</summary>
        public float EaseSpeed = 0.5f;

        // ---------- 开关 ----------
        /// <summary>true = 7 次采样的色散玻璃（GlassDispersionPS）；false = 普通折射（GlassRefractionPS）</summary>
        public bool UseDispersion = true;
        /// <summary>鼠标聚光高光（HighlightPS）</summary>
        public bool EnableHighlight = false;
        public SKColor HighlightColor = new SKColor(255, 255, 255, 48);
        public float SpotRadius = 180f;

        public static GlassSettings CreateDefault() => new GlassSettings();

        public GlassSettings Clone() => (GlassSettings)MemberwiseClone();
    }

    /// <summary>窗口几何信息（物理像素 + DPI 缩放比）。</summary>
    public readonly record struct WindowGeometry(SKRectI Physical, double Scale);

    internal static class ColorUtil
    {
        /// <summary>Avalonia 的 Color（0-255 + 0-255 alpha）→ SkiaSharp 的 SKColor。</summary>
        public static SKColor ToSk(Color c) => new SKColor(c.R, c.G, c.B, c.A);
    }
}
