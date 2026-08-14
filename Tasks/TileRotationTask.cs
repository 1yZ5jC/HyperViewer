using System;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel.Background;
using Windows.Storage;
using HyperViewer.Services;

namespace HyperViewer.Tasks
{
    /// <summary>
    /// 后台磁贴轮换: 应用不在前台时按系统允许的最短周期 (15 分钟)
    /// 从共享图片列表 (LocalSettings, 由前台 TileRotationService 维护)
    /// 轮换推送到动态磁贴。
    /// </summary>
    public sealed class TileRotationTask : IBackgroundTask
    {
        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            var deferral = taskInstance.GetDeferral();
            try
            {
                if (!Helpers.SettingsService.TileRotationEnabled) return;
                if (!Helpers.SettingsService.LiveTileEnabled) return;

                var store = ApplicationData.Current.LocalSettings;
                if (store.Values["TileRotatePaths"] is string joined)
                {
                    var paths = joined.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    if (paths.Count == 0) return;

                    var offset = store.Values["TileRotateOffset"] is int o ? o : 0;
                    var single = Helpers.SettingsService.TileSingleImage;

                    IEnumerable<string> pick;
                    if (single)
                    {
                        pick = new[] { paths[offset % paths.Count] };
                    }
                    else
                    {
                        var count = Math.Min(4, paths.Count);
                        pick = Enumerable.Range(0, count).Select(i => paths[(offset + i) % paths.Count]);
                    }

                    store.Values["TileRotateOffset"] = (offset + 1) % paths.Count;
                    await TileService.UpdateAsync(pick, offset);
                }
            }
            catch
            {
                // 后台失败静默, 不影响系统
            }
            finally
            {
                deferral.Complete();
            }
        }
    }
}