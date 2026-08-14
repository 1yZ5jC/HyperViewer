using System;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace HyperViewer.Services
{
    /// <summary>
    /// 系统分享服务: 通过 DataTransferManager 分享当前图片 (文件 + 位图) 或批量文件。
    /// </summary>
    public static class ShareService
    {
        private static List<StorageFile> _files;
        private static string _title;

        public static void Share(StorageFile file, string title)
            => ShareFiles(new[] { file }, title);

        /// <summary>批量分享多个文件。</summary>
        public static void ShareFiles(IEnumerable<StorageFile> files, string title)
        {
            var list = (files ?? Enumerable.Empty<StorageFile>()).Where(f => f != null).ToList();
            if (list.Count == 0) return;
            _files = list;
            _title = title;
            var dtm = DataTransferManager.GetForCurrentView();
            dtm.DataRequested -= OnDataRequested;
            dtm.DataRequested += OnDataRequested;
            try
            {
                DataTransferManager.ShowShareUI();
            }
            catch
            {
                // 无可用分享目标时静默失败
            }
        }

        private static void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            var deferral = args.Request.GetDeferral();
            try
            {
                args.Request.Data.Properties.Title = string.IsNullOrEmpty(_title) ? "HyperViewer" : _title;
                args.Request.Data.Properties.Description = _files.Count == 1 ? _files[0].Name : _files.Count + " files";
                args.Request.Data.SetStorageItems(_files);
                if (_files.Count == 1)
                {
                    args.Request.Data.SetBitmap(RandomAccessStreamReference.CreateFromFile(_files[0]));
                }
            }
            finally
            {
                deferral.Complete();
            }
        }
    }
}