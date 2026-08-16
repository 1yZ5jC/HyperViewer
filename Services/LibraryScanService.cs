using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HyperViewer.Models;
using Windows.Storage;
using Windows.UI.Xaml.Media.Imaging;

namespace HyperViewer.Services
{
    /// <summary>
    /// 图库扫描服务: 枚举已授权文件夹 (RecentFolders) 中的图片,
    /// 构建相册 (AlbumItem) 列表 + 跨文件夹全部照片列表, 异步预取缩略图。
    /// </summary>
    public static class LibraryScanService
    {
        public sealed class ScanResult
        {
            public IReadOnlyList<AlbumItem> Albums { get; }
            public IReadOnlyList<PhotoItem> AllPhotos { get; }

            public ScanResult(IReadOnlyList<AlbumItem> albums, IReadOnlyList<PhotoItem> allPhotos)
            {
                Albums = albums;
                AllPhotos = allPhotos;
            }
        }

        /// <summary>
        /// 扫描所有最近文件夹, 返回相册列表和全部照片列表。
        /// 缩略图按需懒加载, 调用方可在后续阶段调用 EnsureAlbumCover。
        /// </summary>
        public static async Task<ScanResult> ScanAsync(IReadOnlyList<StorageFolder> folders,
                                                      CancellationToken token = default)
        {
            var albums = new List<AlbumItem>();
            var allPhotos = new List<PhotoItem>();

            foreach (var folder in folders)
            {
                if (token.IsCancellationRequested) break;
                if (folder == null) continue;
                try
                {
                    var files = await folder.GetFilesAsync();
                    var photos = files
                        .Where(f => PhotoItem.IsKnownImageExtension(f.Name))
                        .Select(f => new PhotoItem(f))
                        .ToList();
                    if (photos.Count == 0) continue;

                    var latest = photos.Max(p => p.File.DateCreated);
                    var earliest = photos.Min(p => p.File.DateCreated);
                    var album = new AlbumItem(folder, photos.Count, latest, earliest);
                    albums.Add(album);
                    allPhotos.AddRange(photos);
                }
                catch
                {
                    // 单个文件夹无权限/已删除时跳过
                }
            }

            var sortedAlbums = albums
                .OrderByDescending(a => a.LatestDate ?? DateTimeOffset.MinValue)
                .ToList();
            var sortedPhotos = allPhotos
                .OrderByDescending(p => p.DateTaken ?? p.File.DateCreated)
                .ToList();

            return new ScanResult(sortedAlbums, sortedPhotos);
        }

        /// <summary>
        /// 异步加载相册封面 (取相册内最新一张图片的缩略图)。
        /// </summary>
        public static async Task EnsureAlbumCoverAsync(AlbumItem album, int thumbSize = 320)
        {
            if (album == null || album.Cover != null) return;
            try
            {
                var files = await album.Folder.GetFilesAsync();
                var latest = files
                    .Where(f => PhotoItem.IsKnownImageExtension(f.Name))
                    .OrderByDescending(f => f.DateCreated)
                    .FirstOrDefault();
                if (latest == null)
                {
                    Helpers.DebugLog.Write("LIB", $"cover skip (no image) {album.Name}");
                    return;
                }
                album.Cover = await ImageLoaderService.LoadThumbnailAsync(latest, thumbSize);
                Helpers.DebugLog.Write("LIB", $"cover ok {album.Name} ({files.Count} files, {latest.Name})");
            }
            catch (Exception ex)
            {
                Helpers.DebugLog.Write("LIB", $"cover FAIL {album.Name}: {ex.GetType().Name} {ex.Message}");
                // 封面加载失败留空, UI 显示占位
            }
        }
    }
}
