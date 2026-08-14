using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Xml.Dom;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI.Notifications;

namespace HyperViewer.Services
{
    /// <summary>
    /// 动态磁贴: 取最近照片中心裁剪成 150x150 PNG 存到本地, 用自适应磁贴模板
    /// (小/中/宽 2 宫格/大 4 宫格) 组装后推送。内容未变化时跳过重绘。
    /// </summary>
    public static class TileService
    {
        private const int Cell = 150;
        private const int MaxPhotos = 4;
        private const string StateKey = "TileLastPaths";
        private const string TileUriPrefix = "ms-appdata:///local/tiles/tile_";

        /// <summary>
        /// 用最近照片更新主磁贴。图片生成/编码均在后台完成。
        /// rotToken: 轮换序号, 非 0 时强制重推并给图片 URI 加查询参数
        /// (Windows 按 URI 缓存磁贴图片, 同 URI 新内容可能不刷新)。
        /// </summary>
        public static async Task UpdateAsync(IEnumerable<string> photoPaths, int rotToken = 0)
        {
            try
            {
                if (!Helpers.SettingsService.LiveTileEnabled)
                {
                    // 设置中关闭动态磁贴: 清除内容, 回退到静态磁贴
                    TileUpdateManager.CreateTileUpdaterForApplication().Clear();
                    SaveState(null, false, 0);
                    return;
                }

                var single = Helpers.SettingsService.TileSingleImage;
                var paths = (photoPaths ?? Enumerable.Empty<string>())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Take(single ? 1 : MaxPhotos)
                    .ToList();

                if (paths.Count == 0)
                {
                    // 图库为空: 清除动态磁贴内容, 回退到静态磁贴
                    TileUpdateManager.CreateTileUpdaterForApplication().Clear();
                    SaveState(null, single, 0);
                    return;
                }

                var tilesDir = await ApplicationData.Current.LocalFolder
                    .CreateFolderAsync("tiles", CreationCollisionOption.OpenIfExists);

                if (SameAsLast(paths, single, rotToken)) return;

                var uris = new List<string>();
                if (single)
                {
                    // 单图模式按尺寸各生成一张 (避免 150px 图放大到宽/大尺寸后模糊)
                    uris.Add(await MakeCropAsync(tilesDir, paths[0], Cell, Cell, "s"));
                    uris.Add(await MakeCropAsync(tilesDir, paths[0], 310, 150, "w"));
                    uris.Add(await MakeCropAsync(tilesDir, paths[0], 310, 310, "l"));
                    if (uris[0] == null || uris[1] == null || uris[2] == null) return;
                }
                else
                {
                    for (int i = 0; i < paths.Count; i++)
                    {
                        var uri = await MakeCropAsync(tilesDir, paths[i], Cell, Cell, (i + 1).ToString());
                        if (uri != null) uris.Add(uri);
                    }
                    if (uris.Count == 0) return;
                }

                // 轮换时给图片 URI 加查询参数, 绕过磁贴图片缓存
                if (rotToken != 0)
                {
                    for (int i = 0; i < uris.Count; i++) uris[i] += "?r=" + rotToken;
                }

                var xml = new XmlDocument();
                xml.LoadXml(single ? BuildXmlSingle(uris[0], uris[1], uris[2]) : BuildXml(uris));

                var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                updater.EnableNotificationQueue(false);
                updater.Update(new TileNotification(xml));
                SaveState(paths, single, rotToken);
            }
            catch
            {
                // 磁贴更新失败不影响主流程
            }
        }

        /// <summary>
        /// 将图片中心裁剪为 w×h (保持目标宽高比) 并编码 PNG, 返回可被磁贴引用的 URI。
        /// </summary>
        private static async Task<string> MakeCropAsync(StorageFolder dir, string path, int w, int h, string name)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                using (var stream = await file.OpenReadAsync())
                {
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    uint sw = decoder.PixelWidth;
                    uint sh = decoder.PixelHeight;
                    if (sw == 0 || sh == 0) return null;

                    // 先等比缩到至少覆盖目标尺寸, 再中心裁剪
                    double scale = Math.Max((double)w / sw, (double)h / sh);
                    uint scaledW = (uint)Math.Max(1, sw * scale);
                    uint scaledH = (uint)Math.Max(1, sh * scale);

                    var transform = new BitmapTransform
                        {
                            ScaledWidth = scaledW,
                            ScaledHeight = scaledH,
                            InterpolationMode = BitmapInterpolationMode.Fant,
                            Bounds = new BitmapBounds
                            {
                                X = (uint)((scaledW - w) / 2.0),
                                Y = (uint)((scaledH - h) / 2.0),
                                Width = (uint)w,
                                Height = (uint)h
                            }
                        };

                    using (var sb = await decoder.GetSoftwareBitmapAsync(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied,
                        transform,
                        ExifOrientationMode.RespectExifOrientation,
                        ColorManagementMode.DoNotColorManage))
                    {
                        var outFile = await dir.CreateFileAsync(
                            "tile_" + name + ".png",
                            CreationCollisionOption.ReplaceExisting);
                        using (var fs = await outFile.OpenAsync(FileAccessMode.ReadWrite))
                        {
                            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, fs);
                            encoder.SetSoftwareBitmap(sb);
                            await encoder.FlushAsync();
                        }
                        return TileUriPrefix + name + ".png";
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 自适应磁贴 v3: 四个尺寸绑定共用同一批 150 图。
        /// 小/中=单背景图 (binding 根级, 已验证可用);
        /// 宽=2 宫格 / 大=2x2 四宫格, 分组内图片必须用内联 (inline),
        /// 背景 placement 只支持 binding 根级; 不带 branding 避免文字区挤压分组。
        /// </summary>
        private static string BuildXml(List<string> uris)
        {
            string Bg(string src) =>
                $"<image src=\"{src}\" placement=\"background\" hint-crop=\"none\"/>";
            string Cell(string src) =>
                $"<image src=\"{src}\" hint-removeMargin=\"true\"/>";
            string Sub(string src) =>
                $"<subgroup hint-weight=\"50\">{Cell(src)}</subgroup>";

            string one = uris[0];
            string two = uris.Count > 1 ? uris[1] : one;
            string three = uris.Count > 2 ? uris[2] : one;
            string four = uris.Count > 3 ? uris[3] : one;

            return "<tile version=\"3\">" +
                "<visual>" +
                "<binding template=\"TileSmall\">" + Bg(one) + "</binding>" +
                "<binding template=\"TileMedium\">" + Bg(one) + "</binding>" +
                "<binding template=\"TileWide\">" +
                "<group>" + Sub(one) + Sub(two) + "</group>" +
                "</binding>" +
                "<binding template=\"TileLarge\">" +
                "<group>" + Sub(one) + Sub(two) + "</group>" +
                "<group>" + Sub(three) + Sub(four) + "</group>" +
                "</binding>" +
                "</visual></tile>";
        }

        /// <summary>
        /// 单图模式: 各尺寸用专用背景图 (binding 根级, 绕开分组渲染问题,
        /// 与小/中已验证可用结构一致); 宽/大图为按尺寸裁剪的清晰原图。
        /// </summary>
        private static string BuildXmlSingle(string small, string wide, string large)
        {
            string Bg(string s) =>
                $"<image src=\"{s}\" placement=\"background\" hint-crop=\"none\"/>";

            return "<tile version=\"3\">" +
                "<visual>" +
                "<binding template=\"TileSmall\">" + Bg(small) + "</binding>" +
                "<binding template=\"TileMedium\">" + Bg(small) + "</binding>" +
                "<binding template=\"TileWide\">" + Bg(wide) + "</binding>" +
                "<binding template=\"TileLarge\">" + Bg(large) + "</binding>" +
                "</visual></tile>";
        }

        private static bool SameAsLast(List<string> paths, bool single, int rotToken)
        {
            var store = ApplicationData.Current.LocalSettings.Values;
            // v5: 轮换令牌入状态键, 每次轮换强制重推
            var key = "v5|" + rotToken + "|" + (single ? "1" : "0") + "|" + string.Join("|", paths);
            return store.TryGetValue(StateKey, out var raw) && raw is string s && s == key;
        }

        private static void SaveState(List<string> paths, bool single, int rotToken)
        {
            ApplicationData.Current.LocalSettings.Values[StateKey] =
                paths == null ? null : "v5|" + rotToken + "|" + (single ? "1" : "0") + "|" + string.Join("|", paths);
        }
    }
}