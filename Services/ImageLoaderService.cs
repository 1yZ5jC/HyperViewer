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
    /// 缩略图带内存 LRU 缓存 (跨列表重建复用, 避免重复解码)。
    /// </summary>
    public static class ImageLoaderService
    {
        // ====== 缩略图内存缓存 (LRU) ======
        private const int ThumbCacheMax = 512;
        private static readonly Dictionary<string, BitmapImage> ThumbCache =
            new Dictionary<string, BitmapImage>();
        private static readonly List<string> ThumbOrder = new List<string>();
        private static readonly object CacheLock = new object();

        private static string ThumbKey(StorageFile file, int size)
            => file != null ? file.Path + "@" + size : null;

        private static BitmapImage CacheGet(string key)
        {
            lock (CacheLock)
            {
                if (key != null && ThumbCache.TryGetValue(key, out var hit))
                {
                    // 触达提升优先级 (移到队尾)
                    ThumbOrder.Remove(key);
                    ThumbOrder.Add(key);
                    return hit;
                }
                return null;
            }
        }

        private static void CachePut(string key, BitmapImage bmp)
        {
            if (key == null || bmp == null) return;
            lock (CacheLock)
            {
                if (ThumbCache.ContainsKey(key))
                {
                    ThumbOrder.Remove(key);
                }
                ThumbCache[key] = bmp;
                ThumbOrder.Add(key);
                while (ThumbOrder.Count > ThumbCacheMax)
                {
                    var victim = ThumbOrder[0];
                    ThumbOrder.RemoveAt(0);
                    ThumbCache.Remove(victim);
                }
            }
        }

        /// <summary>清空缩略图缓存 (设置页"清除缓存")。返回清理条目数。</summary>
        public static int ClearCache()
        {
            lock (CacheLock)
            {
                int count = ThumbCache.Count;
                ThumbCache.Clear();
                ThumbOrder.Clear();
                return count;
            }
        }

        /// <summary>当前缓存条目数。</summary>
        public static int CacheCount
        {
            get { lock (CacheLock) return ThumbCache.Count; }
        }

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

        /// <summary>加载缩略图 (内存 LRU 缓存优先)。</summary>
        public static async Task<BitmapImage> LoadThumbnailAsync(PhotoItem photo, int size = 200)
        {
            var key = ThumbKey(photo?.File, size);
            var cached = CacheGet(key);
            if (cached != null) return cached;

            var bmp = await LoadAsync(photo, size);
            if (bmp != null) CachePut(key, bmp);
            return bmp;
        }

        /// <summary>按 StorageFile 加载缩略图 (相册封面等, 走同一缓存)。</summary>
        public static async Task<BitmapImage> LoadThumbnailAsync(StorageFile file, int size = 180)
        {
            var key = ThumbKey(file, size);
            var cached = CacheGet(key);
            if (cached != null)
            {
                Helpers.DebugLog.Write("LIB", $"thumb(file) cache HIT {file.Name} size={size} -> {cached.PixelWidth}x{cached.PixelHeight}");
                return cached;
            }

            BitmapImage bmp = null;
            try
            {
                using (var stream = await file.OpenReadAsync())
                {
                    bmp = new BitmapImage();
                    bmp.DecodePixelWidth = size;
                    await bmp.SetSourceAsync(stream);
                    Helpers.DebugLog.Write("LIB", $"thumb(file) decoded {file.Name} size={size} -> {bmp.PixelWidth}x{bmp.PixelHeight}");
                }
            }
            catch
            {
                bmp = null;
            }
            if (bmp != null) CachePut(key, bmp);
            return bmp;
        }

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
                    // 必须在 UI 线程赋值: 后台线程的 PropertyChanged 通知绑定不更新
                    // (踩坑 28 封面同款问题: 时间轴缩略图由后台任务预载)
                    await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                        Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            p.Thumbnail = bmp;
                            p.ThumbnailLoaded = true;
                        });
                }
            }
            finally
            {
                sem.Release();
            }
        }
    }
}
