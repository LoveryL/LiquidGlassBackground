using System;
using System.Collections.Generic;
using System.IO;

namespace LiquidGlass
{
    /// <summary>
    /// 极简诊断日志：同时写控制台和日志文件。
    /// 类库默认【关闭】文件输出，避免污染宿主程序；需要排查问题时在 AXAML/C# 里打开：
    /// <code>LiquidGlassDiagnostics.IsEnabled = true;</code>
    /// </summary>
    public static class LiquidGlassDiagnostics
    {
        private static readonly object Sync = new object();
        private static readonly HashSet<string> OnceKeys = new HashSet<string>();
        private static string? _logPath;
        private static bool _pathResolved;

        /// <summary>是否写日志文件（默认 false，只写控制台）。</summary>
        public static bool IsEnabled { get; set; }

        /// <summary>当前日志文件路径（未启用时为 null）。</summary>
        public static string? FilePath
        {
            get
            {
                if (!_pathResolved) ResolvePath();
                return _logPath;
            }
        }

        /// <summary>宿主程序可指定日志目录（例如 AppData）。</summary>
        public static string? DirectoryOverride { get; set; }

        internal static void Write(string message)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message;

            try
            {
                Console.WriteLine(line);
            }
            catch
            {
                // ignored
            }

            if (!IsEnabled) return;

            lock (Sync)
            {
                try
                {
                    var p = FilePath;
                    if (p != null) File.AppendAllText(p, line + Environment.NewLine);
                }
                catch
                {
                    // ignored
                }
            }
        }

        /// <summary>同一条消息只打一次，避免刷屏。</summary>
        internal static void Once(string key, string message)
        {
            lock (Sync)
            {
                if (!OnceKeys.Add(key)) return;
            }

            Write(message);
        }

        internal static void Reset()
        {
            lock (Sync)
            {
                OnceKeys.Clear();
            }
        }

        private static void ResolvePath()
        {
            _pathResolved = true;

            var candidates = new[]
            {
                DirectoryOverride,
                AppContext.BaseDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };

            foreach (var dir in candidates)
            {
                try
                {
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        _logPath = Path.Combine(dir!, "liquidglass.log");
                        break;
                    }
                }
                catch
                {
                    // ignored
                }
            }

            try
            {
                if (_logPath != null)
                {
                    File.WriteAllText(_logPath,
                        "=== LiquidGlass " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===" + Environment.NewLine);
                }
            }
            catch
            {
                _logPath = null;
            }
        }
    }

    /// <summary>
    /// 内部日志别名：保留 <c>Log.Write / Log.Once</c> 的调用习惯，全部转给 <see cref="LiquidGlassDiagnostics"/>。
    /// </summary>
    internal static class Log
    {
        public static void Write(string message) => LiquidGlassDiagnostics.Write(message);

        public static void Once(string key, string message) => LiquidGlassDiagnostics.Once(key, message);
    }
}
