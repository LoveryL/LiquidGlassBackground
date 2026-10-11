using System;

namespace LiquidGlass
{
    /// <summary>
    /// 进程级初始化。建议在 Program.Main 的第一行调用，
    /// 保证 GetWindowRect / 抓屏拿到的是真实物理像素（高 DPI 下不会错位）。
    /// </summary>
    public static class LiquidGlassPlatform
    {
        private static bool _dpiDone;

        /// <summary>开启进程 DPI 感知（Per-Monitor v2 → System Aware → 老接口，逐级降级）。</summary>
        public static void EnableDpiAwareness()
        {
            if (_dpiDone) return;
            _dpiDone = true;

            NativeMethods.TryEnableDpiAwareness();
        }

        /// <summary>打开诊断日志（默认关闭）。会在程序目录生成 liquidglass.log。</summary>
        public static void EnableDiagnostics(string? logDirectory = null)
        {
            if (logDirectory != null) LiquidGlassDiagnostics.DirectoryOverride = logDirectory;

            LiquidGlassDiagnostics.IsEnabled = true;
            Log.Write("[platform] 诊断日志已开启: " + (LiquidGlassDiagnostics.FilePath ?? "(不可用)"));
        }
    }
}
