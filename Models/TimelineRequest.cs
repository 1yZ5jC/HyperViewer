using Windows.Storage;

namespace HyperViewer.Models
{
    /// <summary>
    /// 时间轴页 → 主页的跳转请求: 打开所在文件夹并定位到指定图片。
    /// </summary>
    public sealed class TimelineRequest
    {
        public StorageFolder Folder { get; }

        public StorageFile File { get; }

        public TimelineRequest(StorageFolder folder, StorageFile file)
        {
            Folder = folder;
            File = file;
        }
    }
}