using System;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel.Background;
using Windows.Storage;
using Windows.UI.Xaml;

namespace HyperViewer.Services
{
    /// <summary>
    /// 磁贴轮换: 前台用 DispatcherTimer 快速轮换 (30 秒起);
    /// 同时把图片列表/偏移写入 LocalSettings 并注册后台任务
    /// (Tasks.TileRotationTask, TimeTrigger 最短 15 分钟), 应用退出后继续轮换。
    /// </summary>
    public static class TileRotationService
    {
        public const string TaskName = "HyperViewer.TileRotationTask";
        public const string TaskEntryPoint = "HyperViewer.Tasks.TileRotationTask";

        private const string PathsKey = "TileRotatePaths";
        private const string OffsetKey = "TileRotateOffset";

        private static DispatcherTimer _timer;
        private static List<string> _paths = new List<string>();
        private static int _offset;

        /// <summary>用新图片列表启动轮换 (每次文件夹/图库变化时调用)。</summary>
        public static void Start(IEnumerable<string> photoPaths)
        {
            _paths = (photoPaths ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();
            _offset = 0;

            SyncBackgroundTask();

            if (!Helpers.SettingsService.TileRotationEnabled || _paths.Count < 2)
            {
                Stop();
                return;
            }

            // 供后台任务使用的共享状态
            var store = ApplicationData.Current.LocalSettings;
            store.Values[PathsKey] = string.Join("|", _paths);
            store.Values[OffsetKey] = 0;

            if (_timer == null)
            {
                _timer = new DispatcherTimer();
                _timer.Tick += OnTick;
            }
            _timer.Interval = TimeSpan.FromSeconds(Math.Max(10, Helpers.SettingsService.TileRotationSeconds));
            if (!_timer.IsEnabled) _timer.Start();
        }

        /// <summary>停止前台轮换 (后台任务注册状态由 SyncBackgroundTask 管理)。</summary>
        public static void Stop()
        {
            _timer?.Stop();
        }

        /// <summary>设置 (开关/间隔) 变化后, 沿用当前图片列表重启。</summary>
        public static void Restart()
        {
            Start(_paths);
        }

        /// <summary>按设置同步后台任务注册状态 (开关切换 / 应用启动时调用)。</summary>
        public static void SyncBackgroundTask()
        {
            var enabled = Helpers.SettingsService.TileRotationEnabled;
            foreach (var task in BackgroundTaskRegistration.AllTasks)
            {
                if (task.Value.Name == TaskName)
                {
                    if (!enabled) task.Value.Unregister(true);
                    return;
                }
            }
            if (!enabled) return;

            var builder = new BackgroundTaskBuilder
            {
                Name = TaskName,
                TaskEntryPoint = TaskEntryPoint
            };
            // 系统允许的最短周期为 15 分钟
            builder.SetTrigger(new TimeTrigger(15, false));
            _ = builder.Register();
        }

        private static void OnTick(object sender, object e)
        {
            if (!Helpers.SettingsService.TileRotationEnabled)
            {
                Stop();
                return;
            }
            if (_paths.Count == 0) return;

            var single = Helpers.SettingsService.TileSingleImage;
            if (single)
            {
                var one = _paths[_offset % _paths.Count];
                _ = TileService.UpdateAsync(new[] { one }, _offset);
            }
            else
            {
                var pick = Enumerable.Range(0, Math.Min(4, _paths.Count))
                    .Select(i => _paths[(_offset + i) % _paths.Count]);
                _ = TileService.UpdateAsync(pick, _offset);
            }

            var store = ApplicationData.Current.LocalSettings;
            store.Values[OffsetKey] = (_offset + 1) % _paths.Count;
            _offset++;
        }
    }
}