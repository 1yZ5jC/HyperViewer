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

        /// <summary>用最近照片更新主磁贴。图片生成/编码均在后台完成。</summary>
        public static async Task UpdateAsync(IEnumerable<string> photoPaths)
        {
            try
            {
                var paths = (photoPaths ?? Enumerable.Empty<string>())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Take(MaxPhotos)
                    .ToList();

                if (paths.Count == 0)
                {
                    // 图库为空: 清除动态磁贴内容, 回退到静态磁贴
                    TileUpdateManager.CreateTileUpdaterForApplication().Clear();
                    SaveState(null);
                    return;
                }

                var tilesDir = await ApplicationData.Current.LocalFolder
                    .CreateFolderAsync("tiles", CreationCollisionOption.OpenIfExists);

                if (SameAsLast(paths)) return;

                var uris = new List<string>(paths.Count);
                for (int i = 0; i < paths.Count; i++)
                {
                    var uri = await MakeTileImageAsync(tilesDir, paths[i], i + 1);
                    if (uri != null) uris.Add(uri);
                }
                if (uris.Count == 0) return;

                var xml = new XmlDocument();
                xml.LoadXml(BuildXml(uris));

                var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                updater.EnableNotificationQueue(false);
                updater.Update(new TileNotification(xml));
                SaveState(paths);
            }
            catch
            {
                // 磁贴更新失败不影响主流程
            }
        }

        /// <summary>中心裁剪为 150x150 并编码 PNG, 返回可被磁贴引用的 URI。</summary>
        private static async Task<string> MakeTileImageAsync(StorageFolder dir, string path, int index)
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

                    double scale = (double)Cell / Math.Min(sw, sh);
                    uint scaledW = (uint)Math.Max(1, sw * scale);
                    uint scaledH = (uint)Math.Max(1, sh * scale);

var transform = new BitmapTransform
                        {
                            ScaledWidth = scaledW,
                            ScaledHeight = scaledH,
                            InterpolationMode = BitmapInterpolationMode.Fant,
                            Bounds = new BitmapBounds
                            {
                                X = (uint)((scaledW - Cell) / 2.0),
                                Y = (uint)((scaledH - Cell) / 2.0),
                                Width = Cell,
                                Height = Cell
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
                            "tile_" + index + ".png",
                            CreationCollisionOption.ReplaceExisting);
                        using (var fs = await outFile.OpenAsync(FileAccessMode.ReadWrite))
                        {
                            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, fs);
                            encoder.SetSoftwareBitmap(sb);
                            await encoder.FlushAsync();
                        }
                        return TileUriPrefix + index + ".png";
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
        /// 小=1 图, 中=1 图, 宽=2 宫格, 大=2x2 四宫格。
        /// </summary>
        private static string BuildXml(List<string> uris)
        {
            string Img(string src) =>
                $"<image src=\"{src}\" placement=\"background\" hint-crop=\"none\"/>";
            string Sub(string src) =>
                $"<subgroup hint-weight=\"50\">{Img(src)}</subgroup>";

            string one = uris[0];
            string two = uris.Count > 1 ? uris[1] : one;
            string three = uris.Count > 2 ? uris[2] : one;
            string four = uris.Count > 3 ? uris[3] : one;

            return "<tile version=\"3\">" +
                "<visual branding=\"name\">" +
                "<binding template=\"TileSmall\">" + Img(one) + "</binding>" +
                "<binding template=\"TileMedium\">" + Img(one) + "</binding>" +
                "<binding template=\"TileWide\">" +
                "<group>" + Sub(one) + Sub(two) + "</group>" +
                "</binding>" +
                "<binding template=\"TileLarge\">" +
                "<group>" + Sub(one) + Sub(two) + "</group>" +
                "<group>" + Sub(three) + Sub(four) + "</group>" +
                "</binding>" +
                "</visual></tile>";
        }

        private static bool SameAsLast(List<string> paths)
        {
            var store = ApplicationData.Current.LocalSettings.Values;
            var key = string.Join("|", paths);
            return store.TryGetValue(StateKey, out var raw) && raw is string s && s == key;
        }

        private static void SaveState(List<string> paths)
        {
            ApplicationData.Current.LocalSettings.Values[StateKey] =
                paths == null ? null : string.Join("|", paths);
        }
    }
}