using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using HyperViewer.Helpers;
using HyperViewer.Models;
using HyperViewer.Services;
using Windows.Storage;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace HyperViewer.ViewModels
{
    /// <summary>
    /// 主页 ViewModel：相册列表、当前图片、翻页/旋转/翻转/幻灯片/最近打开。
    /// </summary>
    public sealed class MainViewModel : ObservableObject
    {
        private readonly ObservableCollection<PhotoItem> _photos = new ObservableCollection<PhotoItem>();
        public ReadOnlyObservableCollection<PhotoItem> Photos { get; }

        private PhotoItem _current;
        public PhotoItem Current
        {
            get => _current;
            private set
            {
                if (SetProperty(ref _current, value))
                {
                    RaisePropertyChanged(nameof(CanGoPrev));
                    RaisePropertyChanged(nameof(CanGoNext));
                    RaisePropertyChanged(nameof(HasImage));
                    RaisePropertyChanged(nameof(StatusText));
                    RaisePropertyChanged(nameof(ThumbnailVisible));
                    RaiseImageChangedAsync();
                }
            }
        }

        private BitmapImage _displayImage;
        public BitmapImage DisplayImage
        {
            get => _displayImage;
            private set => SetProperty(ref _displayImage, value);
        }

        private int _currentIndex = -1;
        public int CurrentIndex
        {
            get => _currentIndex;
            private set
            {
                if (SetProperty(ref _currentIndex, value))
                {
                    RaisePropertyChanged(nameof(CanGoPrev));
                    RaisePropertyChanged(nameof(CanGoNext));
                    RaisePropertyChanged(nameof(StatusText));
                }
            }
        }

        // 旋转角度 (0/90/180/270)
        private int _rotation;
        public int Rotation
        {
            get => _rotation;
            private set { SetProperty(ref _rotation, ((value % 360) + 360) % 360); }
        }

        // 水平/垂直翻转 (1 表示翻转, 0 表示不翻转, 用作 scale 系数)
        private int _flipH = 1;
        public int FlipH
        {
            get => _flipH;
            private set => SetProperty(ref _flipH, value == 0 ? -1 : 1);
        }
        private int _flipV = 1;
        public int FlipV
        {
            get => _flipV;
            private set => SetProperty(ref _flipV, value == 0 ? -1 : 1);
        }

        // 加载状态
        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        private bool _loadFailed;
        public bool LoadFailed
        {
            get => _loadFailed;
            private set => SetProperty(ref _loadFailed, value);
        }

        // 幻灯片
        private bool _slideShowRunning;
        public bool SlideShowRunning
        {
            get => _slideShowRunning;
            private set
            {
                if (SetProperty(ref _slideShowRunning, value))
                    RaisePropertyChanged(nameof(SlideShowLabel));
            }
        }
        public string SlideShowLabel => SlideShowRunning ? "停止幻灯片" : "幻灯片";
        private int _slideShowSeconds = 3;
        public int SlideShowSeconds
        {
            get => _slideShowSeconds;
            set => SetProperty(ref _slideShowSeconds, Math.Max(1, Math.Min(30, value)));
        }

        public bool ThumbnailVisible => Photos.Count > 0;

        public bool HasImage => Current != null;
        public bool CanGoPrev => CurrentIndex > 0;
        public bool CanGoNext => CurrentIndex >= 0 && CurrentIndex < _photos.Count - 1;
        public string StatusText => Current == null
            ? "未打开图片"
            : $"{Current.Name}  ·  {CurrentIndex + 1}/{_photos.Count}  ·  {Rotation}°{(FlipH < 0 ? " 水平翻转" : "")}{(FlipV < 0 ? " 垂直翻转" : "")}";

        public ICommand OpenImageCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public RelayCommand NextCommand { get; }
        public RelayCommand PrevCommand { get; }
        public RelayCommand FirstCommand { get; }
        public RelayCommand LastCommand { get; }
        public RelayCommand RotateCommand { get; }
        public RelayCommand RotateBackCommand { get; }
        public RelayCommand FlipHCommand { get; }
        public RelayCommand FlipVCommand { get; }
        public RelayCommand ResetTransformCommand { get; }
        public RelayCommand<object> SelectPhotoCommand { get; }
        public RelayCommand ToggleSlideShowCommand { get; }

        public MainViewModel()
        {
            Photos = new ReadOnlyObservableCollection<PhotoItem>(_photos);
            OpenImageCommand = new AsyncRelayCommand(OpenImageAsync);
            OpenFolderCommand = new AsyncRelayCommand(OpenFolderAsync);
            NextCommand = new RelayCommand(Next, () => CanGoNext);
            PrevCommand = new RelayCommand(Prev, () => CanGoPrev);
            FirstCommand = new RelayCommand(First, () => _photos.Count > 0);
            LastCommand = new RelayCommand(Last, () => _photos.Count > 0);

            RotateCommand = new RelayCommand(() => Rotation += 90);
            RotateBackCommand = new RelayCommand(() => Rotation -= 90);
            FlipHCommand = new RelayCommand(() => FlipH = -FlipH);
            FlipVCommand = new RelayCommand(() => FlipV = -FlipV);
            ResetTransformCommand = new RelayCommand(ResetTransform);
            SelectPhotoCommand = new RelayCommand<object>(SelectByIndex);
            ToggleSlideShowCommand = new RelayCommand(ToggleSlideShow, () => _photos.Count > 0);

            _recent = new RecentFoldersService();
            RaisePropertyChanged(nameof(RecentFolders));
        }

        private async Task OpenImageAsync()
        {
            var file = await FilePickerService.PickSingleImageAsync();
            if (file == null) return;
            _photos.Clear();
            var photo = new PhotoItem(file);
            _photos.Add(photo);
            CurrentIndex = 0;
            Current = photo;
            RaisePropertyChanged(nameof(ThumbnailVisible));
            _ = PreloadThumbnailsAsync();
        }

        private async Task OpenFolderAsync()
        {
            var folder = await FilePickerService.PickFolderAsync();
            if (folder == null) return;
            var items = await FilePickerService.EnumerateImagesAsync(folder);
            _photos.Clear();
            foreach (var p in items) _photos.Add(p);
            if (_photos.Count > 0)
            {
                CurrentIndex = 0;
                Current = _photos[0];
                _ = _recent.AddAsync(folder);
            }
            else
            {
                CurrentIndex = -1;
                Current = null;
            }
            RaisePropertyChanged(nameof(ThumbnailVisible));
            _ = PreloadThumbnailsAsync();
        }

        private async Task PreloadThumbnailsAsync()
        {
            if (_photos.Count == 0) return;
            await ImageLoaderService.PreloadThumbnailsAsync(_photos);
        }

        public void Next()
        {
            if (!CanGoNext) return;
            CurrentIndex++;
            Current = _photos[CurrentIndex];
        }
        public void Prev()
        {
            if (!CanGoPrev) return;
            CurrentIndex--;
            Current = _photos[CurrentIndex];
        }
        public void First()
        {
            if (_photos.Count == 0) return;
            CurrentIndex = 0;
            Current = _photos[0];
        }
        public void Last()
        {
            if (_photos.Count == 0) return;
            CurrentIndex = _photos.Count - 1;
            Current = _photos[CurrentIndex];
        }

        private void ResetTransform()
        {
            Rotation = 0;
            FlipH = 1;
            FlipV = 1;
            RaisePropertyChanged(nameof(StatusText));
        }

        /// <summary>
        /// 缩略图栏点击切换当前图片。
        /// </summary>
        public void SelectByIndex(object parameter)
        {
            if (parameter is int idx && idx >= 0 && idx < _photos.Count)
            {
                CurrentIndex = idx;
                Current = _photos[idx];
            }
        }

        public void ActivateFromFile(StorageFile file)
        {
            _photos.Clear();
            var photo = new PhotoItem(file);
            _photos.Add(photo);
            CurrentIndex = 0;
            Current = photo;
            RaisePropertyChanged(nameof(ThumbnailVisible));
            _ = PreloadThumbnailsAsync();
        }

        private async void RaiseImageChangedAsync()
        {
            if (Current == null) { DisplayImage = null; LoadFailed = false; return; }
            IsLoading = true;
            LoadFailed = false;
            var bmp = await ImageLoaderService.LoadAsync(Current);
            DisplayImage = bmp;
            LoadFailed = bmp == null;
            IsLoading = false;
            ResetTransform();
            NextCommand?.NotifyCanExecuteChanged();
            PrevCommand?.NotifyCanExecuteChanged();
            RaisePropertyChanged(nameof(StatusText));
        }

        // ====== 幻灯片 ======
        private CancellationTokenSource _slideShowCts;

        public void ToggleSlideShow()
        {
            if (SlideShowRunning) StopSlideShow();
            else StartSlideShow();
        }

        public void StartSlideShow()
        {
            if (_photos.Count == 0) return;
            SlideShowRunning = true;
            _slideShowCts = new CancellationTokenSource();
            _ = RunSlideShowAsync(_slideShowCts.Token);
        }

        public void StopSlideShow()
        {
            SlideShowRunning = false;
            _slideShowCts?.Cancel();
            _slideShowCts = null;
        }

        private async Task RunSlideShowAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(SlideShowSeconds), token); }
                catch (TaskCanceledException) { break; }
                if (token.IsCancellationRequested) break;
                if (CanGoNext) Next();
                else { First(); }
            }
        }

        // ====== 最近打开 ======
        private readonly RecentFoldersService _recent;
        public System.Collections.Generic.IReadOnlyList<StorageFolder> RecentFolders => _recent.Folders;

        public async Task OpenRecentAsync(StorageFolder folder)
        {
            if (folder == null) return;
            try
            {
                var items = await FilePickerService.EnumerateImagesAsync(folder);
                _photos.Clear();
                foreach (var p in items) _photos.Add(p);
                if (_photos.Count > 0) { CurrentIndex = 0; Current = _photos[0]; }
                else { CurrentIndex = -1; Current = null; }
                RaisePropertyChanged(nameof(ThumbnailVisible));
                _ = PreloadThumbnailsAsync();
            }
            catch { /* 可能已被删除 */ }
        }
    }
}
