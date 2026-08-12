using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HyperViewer.Models;
using Windows.Storage;
using Windows.UI.Xaml.Media.Imaging;

namespace HyperViewer.Services
{
    /// <summary>
    /// 图片加载服务，统一封装异步解码与降采样。
    /// </summary>
    public static class ImageLoaderService
    {
        public static async Task<BitmapImage> LoadAsync(PhotoItem photo, int decodePixelWidth = 0)
        {
            if (photo?.File == null) return null;
            try
            {
                using (var stream = await photo.File.OpenReadAsync())
                {
                    var image = new BitmapImage();
                    bool isAnimated = string.Equals(photo.ContentType, "image/gif", StringComparison.OrdinalIgnoreCase)
                                   || photo.Name.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

                    if (decodePixelWidth > 0)
                    {
                        // 缩略图：降采样
                        image.DecodePixelWidth = decodePixelWidth;
                        image.DecodePixelType = DecodePixelType.Logical;
                    }
                    else if (!isAnimated)
                    {
                        // 主图静态图：超大图降采样到 4K 短边，避免 OOM
                        // GIF 则保持原尺寸, 让动画帧自动播放
                        image.DecodePixelWidth = 4096;
                        image.DecodePixelType = DecodePixelType.Logical;
                    }

                    await image.SetSourceAsync(stream);
                    return image;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static Task<BitmapImage> LoadThumbnailAsync(PhotoItem photo, int size = 200)
            => LoadAsync(photo, size);

        /// <summary>
        /// 并发受限地为一批 PhotoItem 预生成缩略图，逐项通过 binding 反映到 UI。
        /// </summary>
        /// <param name="photos">待生成的列表</param>
        /// <param="size">缩略图短边像素</param>
        /// <param name="maxConcurrency">最大并发解码数</param>
        public static async Task PreloadThumbnailsAsync(
            IReadOnlyList<PhotoItem> photos,
            int size = 200,
            int maxConcurrency = 4)
        {
            if (photos == null || photos.Count == 0) return;
            using (var sem = new SemaphoreSlim(Math.Max(1, maxConcurrency)))
            {
                var tasks = new List<Task>();
                foreach (var p in photos)
                {
                    if (p.ThumbnailLoaded) continue;
                    tasks.Add(LoadOneAsync(sem, p, size));
                }
                await Task.WhenAll(tasks);
            }
        }

        private static async Task LoadOneAsync(SemaphoreSlim sem, PhotoItem p, int size)
        {
            await sem.WaitAsync();
            try
            {
                var bmp = await LoadThumbnailAsync(p, size);
                if (bmp != null)
                {
                    p.Thumbnail = bmp;
                    p.ThumbnailLoaded = true;
                }
            }
            finally
            {
                sem.Release();
            }
        }
    }
}
