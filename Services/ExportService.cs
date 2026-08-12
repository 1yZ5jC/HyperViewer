using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace HyperViewer.Services
{
    /// <summary>
    /// 图片导出服务: 另存为副本到用户选择的位置。
    /// </summary>
    public static class ExportService
    {
        public static async Task<StorageFile> SaveAsAsync(StorageFile source)
        {
            if (source == null) return null;
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = source.Name
            };
            var ext = string.IsNullOrEmpty(source.FileType) ? ".jpg" : source.FileType;
            picker.FileTypeChoices.Add(
                Loc("ExportTypeImage"),
                new List<string> { ext });
            var target = await picker.PickSaveFileAsync();
            if (target == null) return null;
            await source.CopyAndReplaceAsync(target);
            return target;
        }

        private static string Loc(string key) => Helpers.Loc.Get(key);
    }
}
