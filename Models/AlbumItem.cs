using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.Storage;
using Windows.UI.Xaml.Media.Imaging;
using HyperViewer.Helpers;

namespace HyperViewer.Models
{
    /// <summary>
    /// 相册 (文件夹) 卡片数据载体: 封面 + 名称 + 张数 + 日期范围。
    /// 缩略图懒加载, 绑定友好。
    /// </summary>
    public sealed class AlbumItem : INotifyPropertyChanged
    {
        public StorageFolder Folder { get; }

        public string Name => Folder?.Name ?? string.Empty;
        public string Path => Folder?.Path ?? string.Empty;

        public int Count { get; }

        /// <summary>最新照片日期 (用于排序与封面选择)。</summary>
        public DateTimeOffset? LatestDate { get; }

        /// <summary>最早照片日期。</summary>
        public DateTimeOffset? EarliestDate { get; }

        private BitmapImage _cover;
        public BitmapImage Cover
        {
            get => _cover;
            set
            {
                if (!Equals(_cover, value))
                {
                    _cover = value;
                    Helpers.DebugLog.Write("LIB", $"cover set {Name}: {(value?.PixelWidth ?? 0)}x{(value?.PixelHeight ?? 0)}");
                    RaisePropertyChanged();
                }
            }
        }

        public AlbumItem(StorageFolder folder, int count, DateTimeOffset? latest, DateTimeOffset? earliest)
        {
            Folder = folder;
            Count = count;
            LatestDate = latest;
            EarliestDate = earliest;
        }

        public string DateRangeText
        {
            get
            {
                if (LatestDate == null) return string.Empty;
                if (EarliestDate == null || EarliestDate.Value.Year == LatestDate.Value.Year)
                {
                    return LatestDate.Value.ToString("yyyy");
                }
                return $"{EarliestDate.Value:yyyy} – {LatestDate.Value:yyyy}";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void RaisePropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
