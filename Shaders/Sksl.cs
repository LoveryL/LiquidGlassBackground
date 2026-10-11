using System.Text;
using System.Text.RegularExpressions;

namespace LiquidGlass
{
    /// <summary>
    /// HLSL → SKSL 的移植。
    /// 约定：
    ///   1) 采样写成 SMPL(纹理名, 坐标)，运行时展开成 sample()（SkiaSharp 2.x）
    ///      或 纹理名.eval()（SkiaSharp 3.x），两种语法都会尝试编译，哪个成功用哪个；
    ///   2) 内部运算统一用 float，只在 main 返回处构造成 half4，避免 half/float 混用报错；
    ///   3) saturate/discard 等 HLSL 专有写法换成 clamp / 提前 return；
    ///   4) Skia m88（SkiaSharp 2.88）的 SKSL 没有 step()/smoothstep() 内建函数，
    ///      这里手写 stepAt()/smoothStepAt() 替代；
    ///   5) 所有子着色器采样都【内联在 main 里】，不走辅助函数 —— 旧版 SKSL 对
    ///      "在辅助函数里 sample()" 支持不稳，内联最保险。
    /// </summary>
    internal static class Sksl
    {
        // 支持一层括号嵌套：SMPL(tex, rp + doff * (2.0/3.0))
        private static readonly Regex SampleToken =
            new Regex(@"SMPL\(\s*([^,\s()]+)\s*,\s*([^()]*(?:\([^()]*\)[^()]*)*)\s*\)", RegexOptions.Compiled);

        public static string Expand(string source, bool useEval)
        {
            return SampleToken.Replace(source, m =>
            {
                string tex = m.Groups[1].Value;
                string coord = m.Groups[2].Value;
                return useEval ? tex + ".eval(" + coord + ")" : "sample(" + tex + ", " + coord + ")";
            });
        }

        // ---------- 公共 SDF 片段（对应 SDF_COMMON_HLSL） ----------
        private const string Sdf = @"
// 手写 step()：m88 SKSL 没有内建 step
float stepAt(float edge, float x) {
    return x >= edge ? 1.0 : 0.0;
}

// 手写 smoothstep()：m88 SKSL 没有内建 smoothstep
float smoothStepAt(float e0, float e1, float x) {
    float d = e1 - e0;
    if (abs(d) < 0.000001) { return x < e0 ? 0.0 : 1.0; }
    float t = clamp((x - e0) / d, 0.0, 1.0);
    return t * t * (3.0 - 2.0 * t);
}

float radiusAt(float2 coord, float4 radii) {
    if (coord.x >= 0.0) {
        if (coord.y <= 0.0) { return radii.y; }
        return radii.z;
    }
    if (coord.y <= 0.0) { return radii.x; }
    return radii.w;
}

float sdRoundedRect(float2 coord, float2 halfSize, float radius) {
    float2 cornerCoord = abs(coord) - (halfSize - float2(radius, radius));
    float outside = length(max(cornerCoord, float2(0.0, 0.0))) - radius;
    float inside = min(max(cornerCoord.x, cornerCoord.y), 0.0);
    return outside + inside;
}

float2 gradSdRoundedRect(float2 coord, float2 halfSize, float radius) {
    float2 cornerCoord = abs(coord) - (halfSize - float2(radius, radius));
    float sx = coord.x >= 0.0 ? 1.0 : -1.0;
    float sy = coord.y >= 0.0 ? 1.0 : -1.0;
    if (cornerCoord.x > 0.0 || cornerCoord.y > 0.0) {
        float2 c = max(cornerCoord, float2(0.0, 0.0));
        float len = length(c);
        if (len > 0.0) {
            return float2(sx, sy) * (c / len);
        }
    }
    float gradX = stepAt(cornerCoord.y, cornerCoord.x);
    return float2(sx, sy) * float2(gradX, 1.0 - gradX);
}

// 窗口贴屏幕边缘时采样点会越界，这里 clamp 到纹理有效范围，
// 避免出现拉伸条纹（SKSL 对 vec2 版 clamp 支持不稳，手写分量版本）
float2 clampToTex(float2 v, float2 texSize) {
    return float2(clamp(v.x, 0.0, texSize.x - 1.0), clamp(v.y, 0.0, texSize.y - 1.0));
}

float circleMap(float x) {
    return 1.0 - sqrt(max(0.0, 1.0 - x * x));
}
";

        // ---------- 可分离高斯模糊（对应 BlurH_PS / BlurV_PS） ----------
        // 15 taps（中心 1 + 两侧各 7），权重由 CPU 按高斯公式算好传进来。
        // uStep = (缩放,0) 做横向、(0,缩放) 做纵向，一份源码两趟复用。
        public static readonly string BlurPass = BuildBlurPass();

        private static string BuildBlurPass()
        {
            var sb = new StringBuilder();
            sb.AppendLine("uniform shader inputTex;");
            sb.AppendLine("uniform float2 uStep;");
            sb.AppendLine("uniform float4 uWeights0;");
            sb.AppendLine("uniform float4 uWeights1;");
            sb.AppendLine();
            sb.AppendLine("half4 main(float2 coord) {");
            sb.AppendLine("    float4 c = float4(SMPL(inputTex, coord)) * uWeights0.x;");

            string[] w =
            {
                "uWeights0.y", "uWeights0.z", "uWeights0.w",
                "uWeights1.x", "uWeights1.y", "uWeights1.z", "uWeights1.w"
            };

            for (int i = 1; i <= 7; i++)
            {
                string mul = i == 1 ? "" : " * " + i + ".0";
                sb.AppendLine("    c += (float4(SMPL(inputTex, coord + uStep" + mul + "))" +
                              " + float4(SMPL(inputTex, coord - uStep" + mul + "))) * " + w[i - 1] + ";");
            }

            sb.AppendLine("    return half4(c);");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ---------- 玻璃主体（无折射色散版，对应 GlassRefractionPS） ----------
        public const string GlassRefraction = @"
uniform shader blurTex;
uniform float2 uElementPos;
uniform float2 uElementSize;
uniform float4 uCornerRadii;
uniform float uScale;
uniform float uRefractionHeight;
uniform float uRefractionAmount;
uniform float uDepthEffect;
uniform float uSaturation;
uniform float uDarkening;
uniform float4 uGlassTint;
uniform float2 uTexSize;
" + Sdf + @"
half4 main(float2 fragCoord) {
    float2 pc = fragCoord * uScale + uElementPos;
    float2 hs = uElementSize * 0.5;
    float2 cc = pc - uElementPos - hs;

    float r = radiusAt(cc, uCornerRadii);
    float sd = sdRoundedRect(cc, hs, r);
    float edgeAA = 1.0 - smoothStepAt(-1.0, 1.0, sd);
    if (edgeAA < 0.001) { return half4(0.0, 0.0, 0.0, 0.0); }

    float sdIn = min(sd, 0.0);
    float t = clamp(1.0 - (-sdIn / uRefractionHeight), 0.0, 1.0);
    float d = circleMap(t) * uRefractionAmount;

    float gr = min(r * 1.5, min(hs.x, hs.y));
    float2 ccN = cc / (length(cc) + 0.000001);
    float2 grad = normalize(gradSdRoundedRect(cc, hs, gr) + uDepthEffect * ccN);

    float2 rp = clampToTex(pc + d * grad, uTexSize);
    float4 color = float4(SMPL(blurTex, rp));

    float lum = dot(color.rgb, float3(0.213, 0.715, 0.072));
    color.rgb = mix(float3(lum, lum, lum), color.rgb, uSaturation);
    color.rgb = clamp(color.rgb * uDarkening, 0.0, 1.0);
    color.rgb = mix(color.rgb, uGlassTint.rgb, uGlassTint.a);
    color.a *= edgeAA;

    return half4(color.rgb * color.a, color.a);
}
";

        // ---------- 玻璃主体（色散版，对应 GlassDispersionPS，默认使用） ----------
        public const string GlassDispersion = @"
uniform shader blurTex;
uniform float2 uElementPos;
uniform float2 uElementSize;
uniform float4 uCornerRadii;
uniform float uScale;
uniform float uRefractionHeight;
uniform float uRefractionAmount;
uniform float uDepthEffect;
uniform float uSaturation;
uniform float uDispersion;
uniform float uDarkening;
uniform float4 uGlassTint;
uniform float2 uTexSize;
" + Sdf + @"
half4 main(float2 fragCoord) {
    float2 pc = fragCoord * uScale + uElementPos;
    float2 hs = uElementSize * 0.5;
    float2 cc = pc - uElementPos - hs;

    float r = radiusAt(cc, uCornerRadii);
    float sd = sdRoundedRect(cc, hs, r);
    float edgeAA = 1.0 - smoothStepAt(-1.0, 1.0, sd);
    if (edgeAA < 0.001) { return half4(0.0, 0.0, 0.0, 0.0); }

    float sdIn = min(sd, 0.0);
    float t = clamp(1.0 - (-sdIn / uRefractionHeight), 0.0, 1.0);
    float d = circleMap(t) * uRefractionAmount;

    float gr = min(r * 1.5, min(hs.x, hs.y));
    float2 ccN = cc / (length(cc) + 0.000001);
    float2 grad = normalize(gradSdRoundedRect(cc, hs, gr) + uDepthEffect * ccN);

    float2 rp = pc + d * grad;
    float disp = (cc.x * cc.y) / (hs.x * hs.y);
    float2 doff = d * grad * disp * uDispersion;

    float4 color = float4(0.0, 0.0, 0.0, 0.0);
    float4 s;
    s = float4(SMPL(blurTex, clampToTex(rp + doff, uTexSize)));             color.r += s.r / 3.5;                       color.a += s.a / 7.0;
    s = float4(SMPL(blurTex, clampToTex(rp + doff * 0.6666667, uTexSize))); color.r += s.r / 3.5; color.g += s.g / 7.0;  color.a += s.a / 7.0;
    s = float4(SMPL(blurTex, clampToTex(rp + doff * 0.3333333, uTexSize))); color.r += s.r / 3.5; color.g += s.g / 3.5;  color.a += s.a / 7.0;
    s = float4(SMPL(blurTex, clampToTex(rp, uTexSize)));                                          color.g += s.g / 3.5;  color.a += s.a / 7.0;
    s = float4(SMPL(blurTex, clampToTex(rp - doff * 0.3333333, uTexSize)));                       color.g += s.g / 3.5; color.b += s.b / 3.0; color.a += s.a / 7.0;
    s = float4(SMPL(blurTex, clampToTex(rp - doff * 0.6666667, uTexSize)));                                            color.b += s.b / 3.0; color.a += s.a / 7.0;
    s = float4(SMPL(blurTex, clampToTex(rp - doff, uTexSize)));             color.r += s.r / 7.0;                       color.b += s.b / 3.0; color.a += s.a / 7.0;

    float lum = dot(color.rgb, float3(0.213, 0.715, 0.072));
    color.rgb = mix(float3(lum, lum, lum), color.rgb, uSaturation);
    color.rgb = clamp(color.rgb * uDarkening, 0.0, 1.0);
    color.rgb = mix(color.rgb, uGlassTint.rgb, uGlassTint.a);
    color.a *= edgeAA;

    return half4(color.rgb * color.a, color.a);
}
";

        // ---------- 鼠标聚光高光（对应 HighlightPS，默认关闭） ----------
        public const string Highlight = @"
uniform float2 uElementSize;
uniform float4 uCornerRadii;
uniform float uScale;
uniform float4 uHighlightColor;
uniform float2 uMousePos;
uniform float uSpotRadius;
" + Sdf + @"
half4 main(float2 fragCoord) {
    float2 pc = fragCoord * uScale;
    float2 hs = uElementSize * 0.5;
    float2 cc = pc - hs;

    float r = radiusAt(cc, uCornerRadii);
    float sd = sdRoundedRect(cc, hs, r);
    if (sd > 2.0) { return half4(0.0, 0.0, 0.0, 0.0); }

    float dist = length(pc - uMousePos);
    float intensity = 1.0 - smoothStepAt(0.0, uSpotRadius, dist);
    float edgeFade = 1.0 - smoothStepAt(-2.0, 0.0, sd);
    float a = uHighlightColor.a * intensity * edgeFade;

    return half4(uHighlightColor.rgb * a, a);
}
";
    }
}
