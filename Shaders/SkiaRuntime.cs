using System;
using System.Reflection;
using SkiaSharp;

namespace LiquidGlass
{
    /// <summary>
    /// SKRuntimeEffect 的 2.x / 3.x 兼容层。
    /// SkiaSharp 2.88（Avalonia 11 默认）与 SkiaSharp 3.x 的 API 签名不兼容，
    /// 这里用反射把两代 API 都兜住，这样同一份代码在两种版本下都能跑。
    /// </summary>
    internal static class SkRuntime
    {
        private static readonly Type EffectType = typeof(SKRuntimeEffect);
        private static readonly Type? ChildType = Type.GetType("SkiaSharp.SKRuntimeEffectChild, SkiaSharp");

        private static readonly MethodInfo? CreateShader3 =
            EffectType.GetMethod("CreateShader", new[] { typeof(string), typeof(string).MakeByRefType() });
        private static readonly MethodInfo? Create2 =
            EffectType.GetMethod("Create", new[] { typeof(string), typeof(string).MakeByRefType() });
        private static readonly MethodInfo? Build2 =
            EffectType.GetMethod("Build", new[] { typeof(string), typeof(string).MakeByRefType() });

        private static readonly MethodInfo? ToShader3 =
            EffectType.GetMethod("ToShader", new[] { typeof(SKRuntimeEffectUniforms), typeof(SKRuntimeEffectChildren) });
        private static readonly MethodInfo? ToShader2 =
            EffectType.GetMethod("ToShader", new[] { typeof(bool), typeof(SKRuntimeEffectUniforms), typeof(SKRuntimeEffectChildren) });

        private static readonly MethodInfo? AddChildShader =
            typeof(SKRuntimeEffectChildren).GetMethod("Add", new[] { typeof(string), typeof(SKShader) });

        private static MethodInfo? _addChildTyped;
        private static MethodInfo? _childImplicit;
        private static bool _childLookupDone;

        public static bool IsVersion3 => ChildType != null;

        /// <summary>编译 SKSL；返回 null 表示失败，error 为 Skia 的编译错误信息。</summary>
        public static SKRuntimeEffect? Create(string sksl, out string error)
        {
            MethodInfo?[] factories = { CreateShader3, Create2, Build2 };
            string lastError = "未找到 SKRuntimeEffect 的工厂方法";

            foreach (var mi in factories)
            {
                if (mi == null) continue;

                try
                {
                    object?[] args = { sksl, null };
                    var effect = mi.Invoke(null, args) as SKRuntimeEffect;
                    lastError = args[1] as string ?? string.Empty;

                    if (effect != null)
                    {
                        error = string.Empty;
                        return effect;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
            }

            error = lastError;
            return null;
        }

        public static SKShader? ToShader(SKRuntimeEffect effect, SKRuntimeEffectUniforms uniforms, SKRuntimeEffectChildren children)
        {
            try
            {
                if (ToShader3 != null)
                {
                    return ToShader3.Invoke(effect, new object[] { uniforms, children }) as SKShader;
                }

                if (ToShader2 != null)
                {
                    return ToShader2.Invoke(effect, new object[] { false, uniforms, children }) as SKShader;
                }
            }
            catch (Exception)
            {
                // 落到下面统一返回 null
            }

            return null;
        }

        /// <summary>给 effect 的 child 槽位绑定一个 SKShader。</summary>
        public static bool SetChild(SKRuntimeEffectChildren children, string name, SKShader shader)
        {
            if (!_childLookupDone)
            {
                _childLookupDone = true;

                if (ChildType != null)
                {
                    _addChildTyped = typeof(SKRuntimeEffectChildren)
                        .GetMethod("Add", new[] { typeof(string), ChildType });

                    foreach (var mi in ChildType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (mi.Name != "op_Implicit") continue;

                        var ps = mi.GetParameters();
                        if (ps.Length == 1 && ps[0].ParameterType == typeof(SKShader))
                        {
                            _childImplicit = mi;
                            break;
                        }
                    }
                }
            }

            // SkiaSharp 3.x：Add(string, SKRuntimeEffectChild)
            if (_addChildTyped != null && _childImplicit != null)
            {
                try
                {
                    object? child = _childImplicit.Invoke(null, new object[] { shader });
                    _addChildTyped.Invoke(children, new[] { name, child });
                    return true;
                }
                catch (Exception)
                {
                    // 继续尝试 2.x 写法
                }
            }

            // SkiaSharp 2.x：Add(string, SKShader)
            if (AddChildShader != null)
            {
                try
                {
                    AddChildShader.Invoke(children, new object[] { name, shader });
                    return true;
                }
                catch (Exception)
                {
                    // ignored
                }
            }

            return false;
        }
    }
}
