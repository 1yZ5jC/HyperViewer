using System.Diagnostics;
using System.IO;
using Windows.Storage;

namespace HyperViewer.Helpers
{
    /// <summary>
    /// 调试日志: 输出到调试器, 并始终 (不依赖开关) 追加写入本地应用数据目录的
    /// debug.log (每次启动清空), 便于把日志文件发给开发者排查显示/缩放时序问题。
    /// 文件: %LOCALAPPDATA%\Packages\YoungZhouCorp.HyperViewer_*\LocalState\debug.log
    /// 格式: [毫秒][tag] 消息 (调用点统一走本类的 Write, 与调试器输出同一时间轴)。
    /// </summary>
    public static class DebugLog
    {
        private static readonly Stopwatch _clock = Stopwatch.StartNew();
        private static readonly StreamWriter _file;

        // 静态构造: 打开文件 (覆盖写入, 每次启动全新日志)。UWP 的 Debug 类没有
        // Listeners/TraceListener, 故直接持有 StreamWriter 自己落盘。
        static DebugLog()
        {
            StreamWriter file = null;
            try
            {
                var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "debug.log");
                file = new StreamWriter(File.Create(path)) { AutoFlush = true };
            }
            catch
            {
                // 写盘失败不阻断应用
            }
            _file = file;
        }

        public static bool Verbose => SettingsService.DebugVerboseLog;

        /// <summary>输出日志 (调试器可见, 文件始终写入)。</summary>
        public static void Write(string tag, string message)
        {
            var line = $"[{_clock.ElapsedMilliseconds,6}ms][{tag}] {message}";
            Debug.WriteLine(line);
            try
            {
                _file?.WriteLine(line);
            }
            catch
            {
            }
        }
    }
}
