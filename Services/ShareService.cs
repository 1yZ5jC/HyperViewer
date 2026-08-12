using System;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace HyperViewer.Services
{
    /// <summary>
    /// 系统分享服务: 通过 DataTransferManager 分享当前图片 (文件 + 位图)。
    /// </summary>
    public static class ShareService
    {
        private static StorageFile _file;
        private static string _title;

        public static void Share(StorageFile file, string title)
        {
            if (file == null) return;
            _file = file;
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
                args.Request.Data.Properties.Description = _file.Name;
                args.Request.Data.SetStorageItems(new StorageFile[] { _file });
                args.Request.Data.SetBitmap(RandomAccessStreamReference.CreateFromFile(_file));
            }
            finally
            {
                deferral.Complete();
            }
        }
    }
}
