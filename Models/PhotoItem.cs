using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.Storage;
using Windows.UI.Xaml.Media.Imaging;

namespace HyperViewer.Models
{
    /// <summary>
    /// 单张图片的轻量数据载体。延迟加载缩略图与原图，绑定友好。
    /// </summary>
    public sealed class PhotoItem : INotifyPropertyChanged
    {
        public StorageFile File { get; }

        public string Name => File?.Name ?? string.Empty;
        public string Path => File?.Path ?? string.Empty;
        public string ContentType => File?.ContentType ?? string.Empty;
        public DateTimeOffset DateCreated => File?.DateCreated ?? DateTimeOffset.MinValue;

        private BitmapImage _thumbnail;
        public BitmapImage Thumbnail
        {
            get => _thumbnail;
            set
            {
                if (!Equals(_thumbnail, value))
                {
                    _thumbnail = value;
                    RaisePropertyChanged();
                }
            }
        }

        // 标记是否已被请求过缩略图，避免重复加载
        internal bool ThumbnailLoaded { get; set; }

        public bool IsImage { get; }

        public PhotoItem(StorageFile file)
        {
            File = file ?? throw new ArgumentNullException(nameof(file));
            IsImage = ContentType != null
                      && (ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                          || IsKnownImageExtension(Name));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void RaisePropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public static bool IsKnownImageExtension(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var ext = System.IO.Path.GetExtension(name).ToLowerInvariant();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp"
                   || ext == ".gif" || ext == ".tiff" || ext == ".tif" || ext == ".ico"
                   || ext == ".webp" || ext == ".svg";
        }
    }
}
