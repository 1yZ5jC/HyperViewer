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

        /// <summary>本地化日期文本 (列表/卡片视图展示)。</summary>
        public string DateCreatedText
        {
            get
            {
                var d = DateCreated;
                return d == DateTimeOffset.MinValue ? string.Empty
                    : d.ToString("yyyy-MM-dd HH:mm");
            }
        }

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

        private DateTimeOffset? _dateTaken;
        /// <summary>
        /// 拍摄日期 (EXIF 优先, 无 EXIF 时为文件修改时间), 由时间轴扫描填充。
        /// </summary>
        public DateTimeOffset? DateTaken
        {
            get => _dateTaken;
            set
            {
                if (_dateTaken != value)
                {
                    _dateTaken = value;
                    RaisePropertyChanged();
                }
            }
        }

        // 原始像素尺寸 (由信息服务/解码器填充, 用于状态栏显示)
        public double PixelWidth { get; set; }
        public double PixelHeight { get; set; }

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
