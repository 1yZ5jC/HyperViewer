using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HyperViewer.Models;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace HyperViewer.Services
{
    /// <summary>
    /// 文件 / 文件夹选择与扫描服务。
    /// </summary>
    public static class FilePickerService
    {
        private static readonly string[] ImageExtensions =
            { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".ico", ".webp", ".svg" };

        public static async Task<StorageFile> PickSingleImageAsync()
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            foreach (var ext in ImageExtensions) picker.FileTypeFilter.Add(ext);
            picker.FileTypeFilter.Add("*");
            return await picker.PickSingleFileAsync();
        }

        public static async Task<StorageFolder> PickFolderAsync()
        {
            var picker = new FolderPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            picker.FileTypeFilter.Add("*");
            return await picker.PickSingleFolderAsync();
        }

        public static async Task<IReadOnlyList<PhotoItem>> EnumerateImagesAsync(StorageFolder folder)
        {
            var items = await folder.GetFilesAsync();
            return items
                .Where(f => PhotoItem.IsKnownImageExtension(f.Name))
                .OrderBy(f => f.Name)
                .Select(f => new PhotoItem(f))
                .ToList();
        }
    }
}
