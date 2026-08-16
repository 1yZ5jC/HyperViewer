using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using HyperViewer.Helpers;
using HyperViewer.Models;
using HyperViewer.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
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
                    RaisePropertyChanged(nameof(StatusLeftText));
                    RaisePropertyChanged(nameof(StatusRightText));
                    RaisePropertyChanged(nameof(ThumbnailVisible));
                    RaisePropertyChanged(nameof(HomeVisible));
                    RefreshFavorite();
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
                    RaisePropertyChanged(nameof(StatusLeftText));
                    RaisePropertyChanged(nameof(StatusRightText));
                }
            }
        }

        // 旋转角度 (累加值, 可超 360/负值: 取模会在 270->360 时变成 270->0,
        // 让动画逆时针倒转, 视觉上"回到原始方向"而非再转 90°)
        private int _rotation;
        public int Rotation
        {
            get => _rotation;
            private set => SetProperty(ref _rotation, value);
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
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    RaisePropertyChanged(nameof(HomeVisible));
                }
            }
        }

        // 主页显示: 无图片且未加载中 (空态欢迎面板)
        public bool HomeVisible => !IsLoading && Current == null;

        // 主页"最近打开"区块是否可见
        private bool _recentVisible;
        public bool RecentVisible
        {
            get => _recentVisible;
            private set => SetProperty(ref _recentVisible, value);
        }

        /// <summary>图库视图当前 Tab。</summary>
        public enum LibraryTabKind { Albums, AllPhotos, Folders, Favorites }
        private LibraryTabKind _libraryTab = LibraryTabKind.Albums;
        public LibraryTabKind LibraryTab
        {
            get => _libraryTab;
            set
            {
                if (SetProperty(ref _libraryTab, value))
                {
                    RaiseTabVisibilities();
                    LibraryTabChanged?.Invoke(this, value);
                }
            }
        }

        public event EventHandler<LibraryTabKind> LibraryTabChanged;
        public bool AlbumsVisible => LibraryTab == LibraryTabKind.Albums;
        public bool AllPhotosVisible => LibraryTab == LibraryTabKind.AllPhotos;
        public bool FoldersVisible => LibraryTab == LibraryTabKind.Folders;
        public bool FavoritesVisible => LibraryTab == LibraryTabKind.Favorites;

        // Tab 内容视图可见性 (空态显示时全部隐藏, 避免与空态面板重叠)
        public bool AlbumsContentVisible => AlbumsVisible && !LibraryEmptyVisible;
        public bool AllPhotosContentVisible => AllPhotosVisible && !LibraryEmptyVisible;
        public bool FoldersContentVisible => FoldersVisible && !LibraryEmptyVisible;
        public bool FavoritesContentVisible => FavoritesVisible && !LibraryEmptyVisible;

        public ObservableCollection<AlbumItem> Albums { get; } = new ObservableCollection<AlbumItem>();
        public ObservableCollection<PhotoItem> AllPhotos { get; } = new ObservableCollection<PhotoItem>();
        public ObservableCollection<PhotoItem> Favorites { get; } = new ObservableCollection<PhotoItem>();

        private bool _favoritesLoading;
        private bool _suppressFavoritesPersist;

        // ---- Search support ----
        private List<AlbumItem> _masterAlbums = new List<AlbumItem>();
        private List<PhotoItem> _masterAllPhotos = new List<PhotoItem>();
        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplySearch();
                }
            }
        }

        private void ApplySearch()
        {
            try
            {
                var term = (_searchText ?? string.Empty).Trim().ToLowerInvariant();
                bool filtering = !string.IsNullOrEmpty(term);

                Albums.Clear();
                AllPhotos.Clear();

                if (filtering)
                {
                    foreach (var a in _masterAlbums.Where(a => a.Name?.ToLowerInvariant().Contains(term) == true))
                        Albums.Add(a);
                    foreach (var p in _masterAllPhotos.Where(p => p.Name?.ToLowerInvariant().Contains(term) == true))
                        AllPhotos.Add(p);
                }
                else
                {
                    foreach (var a in _masterAlbums) Albums.Add(a);
                    foreach (var p in _masterAllPhotos) AllPhotos.Add(p);
                }
                // Update empty-state visibility based on filtered results
                LibraryEmptyVisible = Albums.Count == 0 && AllPhotos.Count == 0 && !RecentVisible;
            }
            catch (Exception ex)
            {
                // 搜索异常时静默忽略，保持原列表
                System.Diagnostics.Debug.WriteLine($"ApplySearch error: {ex.Message}");
            }
        }

        /// <summary>图库空态 (没有任何相册): 显示欢迎/添加提示。</summary>
        private bool _libraryEmptyVisible = true;
        public bool LibraryEmptyVisible
        {
            get => _libraryEmptyVisible;
            private set
            {
                if (SetProperty(ref _libraryEmptyVisible, value))
                {
                    RaiseTabVisibilities();
                }
            }
        }

        private void RaiseTabVisibilities()
        {
            RaisePropertyChanged(nameof(AlbumsVisible));
            RaisePropertyChanged(nameof(AllPhotosVisible));
            RaisePropertyChanged(nameof(FoldersVisible));
            RaisePropertyChanged(nameof(FavoritesVisible));
            RaisePropertyChanged(nameof(AlbumsContentVisible));
            RaisePropertyChanged(nameof(AllPhotosContentVisible));
            RaisePropertyChanged(nameof(FoldersContentVisible));
            RaisePropertyChanged(nameof(FavoritesContentVisible));
        }

        private bool _loadFailed;
        public bool LoadFailed
        {
            get => _loadFailed;
            private set => SetProperty(ref _loadFailed, value);
        }

        // 信息面板
        private bool _infoPanelOpen;
        public bool InfoPanelOpen
        {
            get => _infoPanelOpen;
            set => SetProperty(ref _infoPanelOpen, value);
        }

        private System.Collections.ObjectModel.ObservableCollection<InfoRow> _infoRows;
        public System.Collections.ObjectModel.ObservableCollection<InfoRow> InfoRows
        {
            get => _infoRows;
            private set => SetProperty(ref _infoRows, value);
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
        public string SlideShowLabel => SlideShowRunning ? Loc.Get("StopSlideshow") : Loc.Get("StartSlideshow");
        private int _slideShowSeconds = 3;
        public int SlideShowSeconds
        {
            get => _slideShowSeconds;
            set => SetProperty(ref _slideShowSeconds, Math.Max(1, Math.Min(30, value)));
        }

        // 主视图背景 (来自设置)
        private Windows.UI.Xaml.Media.Brush _mainBackground;
        public Windows.UI.Xaml.Media.Brush MainBackground
        {
            get => _mainBackground;
            private set => SetProperty(ref _mainBackground, value);
        }

        /// <summary>
        /// 从设置刷新 (页面回到前台时调用, 同实例也能生效)。
        /// </summary>
        public void RefreshSettings()
        {
            SlideShowSeconds = SettingsService.SlideShowSeconds;
            string bg = SettingsService.MainBackground;
            switch (bg)
            {
                case "White":
                    MainBackground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White);
                    break;
                case "DarkGray":
                    MainBackground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.DimGray);
                    break;
                default:
                    MainBackground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Black);
                    break;
            }
        }

        public bool ThumbnailVisible => Photos.Count > 0;

        // 当前图片是否已收藏
        private bool _isCurrentFavorite;
        public bool IsCurrentFavorite
        {
            get => _isCurrentFavorite;
            private set
            {
                if (SetProperty(ref _isCurrentFavorite, value))
                    RaisePropertyChanged(nameof(FavoriteGlyph));
            }
        }
        public string FavoriteGlyph => IsCurrentFavorite ? "\uE735" : "\uE734";

        // 当前打开的文件夹 (时间轴入口可用性依据)
        private StorageFolder _currentFolder;
        public StorageFolder CurrentFolder
        {
            get => _currentFolder;
            private set
            {
                if (SetProperty(ref _currentFolder, value))
                    RaisePropertyChanged(nameof(CanOpenTimeline));
            }
        }
        public bool CanOpenTimeline => CurrentFolder != null;
        public bool CanRenameCurrent => CurrentFolder != null && Current != null;

        public bool HasImage => Current != null;
        public bool CanGoPrev => CurrentIndex > 0;
        public bool CanGoNext => CurrentIndex >= 0 && CurrentIndex < _photos.Count - 1;

        // 当前缩放级别 (由 ImageViewer 上报, 仅用于状态栏显示)
        private double _zoomFactor = 1.0;
        public double ZoomFactor
        {
            get => _zoomFactor;
            private set => SetProperty(ref _zoomFactor, value);
        }
        public void UpdateZoomFactor(double value)
        {
            ZoomFactor = value;
            RaisePropertyChanged(nameof(StatusLeftText));
            RaisePropertyChanged(nameof(StatusRightText));
        }

        public string StatusLeftText
        {
            get
            {
                if (Current == null) return Loc.Get("StatusNoImage");
                var size = (Current.PixelWidth > 0 && Current.PixelHeight > 0)
                    ? $"  ·  {Current.PixelWidth:0}x{Current.PixelHeight:0}"
                    : string.Empty;
                return $"{Current.Name}  ·  {CurrentIndex + 1}/{_photos.Count}{size}";
            }
        }

        // 状态栏右侧: 缩放 / 旋转 / 翻转 状态
        public string StatusRightText
        {
            get
            {
                if (Current == null) return string.Empty;
                return $"{ZoomFactor:0%}{(((Rotation % 360) + 360) % 360 != 0 ? "  " + ((Rotation % 360) + 360) % 360 + "°" : "")}{(FlipH < 0 ? "  " + Loc.Get("FlipHState") : "")}{(FlipV < 0 ? "  " + Loc.Get("FlipVState") : "")}";
            }
        }

        public ICommand OpenImageCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public RelayCommand NextCommand { get; }
        public RelayCommand PrevCommand { get; }
        public RelayCommand FirstCommand { get; }
        public RelayCommand LastCommand { get; }
        public RelayCommand RotateCommand { get; }
        public RelayCommand RotateBackCommand { get; }
        public RelayCommand Rotate180Command { get; }
        public RelayCommand FlipHCommand { get; }
        public RelayCommand FlipVCommand { get; }
        public RelayCommand ResetTransformCommand { get; }
        public RelayCommand<object> SelectPhotoCommand { get; }
        public RelayCommand ToggleSlideShowCommand { get; }
        public RelayCommand ToggleInfoPanelCommand { get; }
        public RelayCommand ToggleFavoriteCommand { get; }

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
            Rotate180Command = new RelayCommand(() => Rotation += 180);
            FlipHCommand = new RelayCommand(() => FlipH = -FlipH);
            FlipVCommand = new RelayCommand(() => FlipV = -FlipV);
            ResetTransformCommand = new RelayCommand(ResetTransform);
            SelectPhotoCommand = new RelayCommand<object>(SelectByIndex);
            ToggleSlideShowCommand = new RelayCommand(ToggleSlideShow, () => _photos.Count > 0);
            ToggleInfoPanelCommand = new RelayCommand(() => InfoPanelOpen = !InfoPanelOpen);
            ToggleFavoriteCommand = new RelayCommand(ToggleFavorite);
            SelectAlbumsTabCommand = new RelayCommand(() => SelectLibraryTab(LibraryTabKind.Albums));
            SelectAllPhotosTabCommand = new RelayCommand(() => SelectLibraryTab(LibraryTabKind.AllPhotos));
            SelectFoldersTabCommand = new RelayCommand(() => SelectLibraryTab(LibraryTabKind.Folders));
            SelectFavoritesTabCommand = new RelayCommand(() => SelectLibraryTab(LibraryTabKind.Favorites));
            Favorites.CollectionChanged += OnFavoritesChanged;

            _recent = RecentFoldersService.Instance;
            _recent.Changed += (_, __) =>
            {
                RaisePropertyChanged(nameof(RecentFolders));
                RecentVisible = _recent.Folders.Count > 0;
                _ = RefreshLibraryAsync();
            };
            RaisePropertyChanged(nameof(RecentFolders));
            RecentVisible = _recent.Folders.Count > 0;
            _ = RefreshLibraryAsync();
            RefreshSettings();
            // 恢复上一次的 Tab 选择
            var savedTab = SettingsService.LastTab;
            if (Enum.TryParse<LibraryTabKind>(savedTab, out var tab))
                LibraryTab = tab;
        }

        private CancellationTokenSource _libraryCts;

        /// <summary>
        /// 扫描最近文件夹, 重建 Albums/AllPhotos, 后台懒加载封面缩略图。
        /// </summary>
        public async Task RefreshLibraryAsync()
        {
            _libraryCts?.Cancel();
            _libraryCts = new CancellationTokenSource();
            var token = _libraryCts.Token;
            try
            {
                var folders = _recent.Folders;
                if (folders == null || folders.Count == 0)
                {
                    Albums.Clear();
                    AllPhotos.Clear();
                    _masterAlbums.Clear();
                    _masterAllPhotos.Clear();
                    LibraryEmptyVisible = true;
                    _ = TileService.UpdateAsync(null);
                    return;
                }
                var result = await LibraryScanService.ScanAsync(folders, token);
                if (token.IsCancellationRequested) return;

                // 复用旧 PhotoItem 实例: 保留已加载的缩略图/拍摄日期, 避免重复解码
                var oldByPath = _masterAllPhotos.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
                var reused = new List<PhotoItem>(result.AllPhotos.Count);
                foreach (var p in result.AllPhotos)
                {
                    reused.Add(oldByPath.TryGetValue(p.Path, out var old) ? old : p);
                }

                Albums.Clear();
                    AllPhotos.Clear();
                    // Keep master copies for search/filter
                    _masterAlbums = new List<AlbumItem>(result.Albums);
                    _masterAllPhotos = reused;
                    // Populate filtered collections based on current search text
                    ApplySearch();
                    LibraryEmptyVisible = _masterAlbums.Count == 0 && _masterAllPhotos.Count == 0;

                // 动态磁贴: 最近照片 (按日期降序的列表头部)
                _ = TileService.UpdateAsync(_masterAllPhotos.Take(4).Select(p => p.Path));
                // 磁贴轮换: 图库全部照片
                TileRotationService.Start(_masterAllPhotos.Select(p => p.Path));

                // 后台懒加载相册封面 (遍历快照: 遍历期间集合可能被 ApplySearch 修改,
                // 直接 foreach ObservableCollection 会抛"集合已修改"异常中断全部封面)
                var albumsSnapshot = _masterAlbums.ToList();
                _ = Task.Run(async () =>
                {
                    foreach (var album in albumsSnapshot)
                    {
                        if (token.IsCancellationRequested) break;
                        await LibraryScanService.EnsureAlbumCoverAsync(album);
                    }
                    Helpers.DebugLog.Write("LIB", $"cover loop done ({albumsSnapshot.Count} albums, cancelled={token.IsCancellationRequested})");
                }, token);
            }
            catch
            {
                // 扫描失败留空
            }
        }

        /// <summary>点击相册卡片: 加载文件夹, 进入单图浏览模式。</summary>
        public async Task OpenAlbumAsync(AlbumItem album)
        {
            if (album?.Folder == null) return;
            await LoadFolderAsync(album.Folder);
        }

        /// <summary>点击全部照片中的缩略图: 加载所在文件夹并定位到该图。</summary>
        public async Task OpenPhotoFromLibraryAsync(PhotoItem photo)
        {
            if (photo?.File == null) return;
            try
            {
                var folder = await photo.File.GetParentAsync();
                if (folder == null) return;
                await LoadFolderAsync(folder);
                for (int i = 0; i < _photos.Count; i++)
                {
                    if (string.Equals(_photos[i].Path, photo.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        CurrentIndex = i;
                        Current = _photos[i];
                        ApplyNavigationReset();
                        break;
                    }
                }
            }
            catch
            {
                // 文件可能已删除, 静默忽略
            }
        }

        /// <summary>切换图库 Tab (供 XAML 命令绑定)。</summary>
        public void SelectLibraryTab(LibraryTabKind kind)
        {
            LibraryTab = kind;
            if (kind == LibraryTabKind.Favorites) _ = RefreshFavoritesAsync();
            // 持久化到本地设置
            SettingsService.LastTab = kind.ToString();
        }

        public RelayCommand SelectAlbumsTabCommand { get; }
        public RelayCommand SelectAllPhotosTabCommand { get; }
        public RelayCommand SelectFoldersTabCommand { get; }
        public RelayCommand SelectFavoritesTabCommand { get; }

        /// <summary>
        /// 重建收藏列表 (保持用户拖拽后的顺序)。文件已移动/删除的静默跳过。
        /// </summary>
        public async Task RefreshFavoritesAsync()
        {
            if (_favoritesLoading) return;
            _favoritesLoading = true;
            try
            {
                _suppressFavoritesPersist = true;
                Favorites.Clear();
                var byPath = new Dictionary<string, PhotoItem>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in _masterAllPhotos) byPath[p.Path] = p;
                foreach (var path in FavoritesService.GetAll())
                {
                    if (byPath.TryGetValue(path, out var item))
                    {
                        Favorites.Add(item);
                        continue;
                    }
                    try
                    {
                        var file = await StorageFile.GetFileFromPathAsync(path);
                        Favorites.Add(new PhotoItem(file));
                    }
                    catch { /* 文件已被移动或删除 */ }
                }
            }
            finally
            {
                _suppressFavoritesPersist = false;
                _favoritesLoading = false;
            }
        }

        private void OnFavoritesChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_suppressFavoritesPersist) return;
            FavoritesService.SaveOrder(Favorites.Select(f => f.Path));
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
            CurrentFolder = null;
            SettingsService.LastFolderPath = null;
            RaisePropertyChanged(nameof(ThumbnailVisible));
            // PreloadThumbnailsAsync removed for lazy loading
        }

        private async Task OpenFolderAsync()
        {
            var folder = await FilePickerService.PickFolderAsync();
            if (folder == null) return;
            await LoadFolderAsync(folder);
            _ = _recent.AddAsync(folder);
        }

        /// <summary>
        /// 图库文件夹管理器: 选择文件夹加入图库 (不进入查看器), 并刷新图库。
        /// </summary>
        public async Task AddLibraryFolderAsync()
        {
            var folder = await FilePickerService.PickFolderAsync();
            if (folder == null) return;
            await _recent.AddAsync(folder);
            await RefreshLibraryAsync();
        }

        /// <summary>
        /// 装载文件夹图片列表 (打开文件夹 / 最近打开 / 时间轴跳转共用)。
        /// </summary>
        public async Task LoadFolderAsync(StorageFolder folder)
        {
            if (folder == null) return;
            try
            {
                var items = await FilePickerService.EnumerateImagesAsync(folder);
                _photos.Clear();
                // 新列表会话: 邻居预取缓存按旧列表的 index 存储, 直接使用会
                // 先显示错误的邻居低清图 (如"全部照片"打开时显示错图)
                _neighborCache.Clear();
                foreach (var p in items) _photos.Add(p);
                CurrentFolder = folder;
                SettingsService.LastFolderPath = folder.Path;
                if (_photos.Count > 0)
                {
                    CurrentIndex = 0;
                    Current = _photos[0];
                }
                else
                {
                    CurrentIndex = -1;
                    Current = null;
                }
                RaisePropertyChanged(nameof(ThumbnailVisible));
                // 磁贴轮换: 当前文件夹内全部图片
                TileRotationService.Start(_photos.Select(p => p.Path));
                // PreloadThumbnailsAsync removed for lazy loading
            }
            catch { /* 文件夹可能已被删除或失去访问权 */ }
        }

        /// <summary>
        /// 按路径定位到已加载列表中的某张图片 (时间轴跳转)。
        /// </summary>
        public void SelectFile(StorageFile file)
        {
            if (file == null) return;
            for (int i = 0; i < _photos.Count; i++)
            {
                if (string.Equals(_photos[i].Path, file.Path, StringComparison.OrdinalIgnoreCase))
                {
                    CurrentIndex = i;
                    Current = _photos[i];
                    return;
                }
            }
        }

        private async Task PreloadThumbnailsAsync()
        {
            if (_photos.Count == 0) return;
            await ImageLoaderService.PreloadThumbnailsAsync(_photos);
        }

        /// <summary>切换图片后按设置重置旋转/翻转 (设置开启时恢复原始方向)。</summary>
        private void ApplyNavigationReset()
        {
            if (SettingsService.ResetRotationOnNavigate)
            {
                Rotation = 0;
                FlipH = 1;
                FlipV = 1;
            }
        }

        public void Next()
        {
            Helpers.DebugLog.Write("VM", $"Next ({CurrentIndex}->{CurrentIndex + 1})");
            if (!CanGoNext) return;
            CurrentIndex++;
            Current = _photos[CurrentIndex];
            ApplyNavigationReset();
        }
        public void Prev()
        {
            Helpers.DebugLog.Write("VM", $"Prev ({CurrentIndex}->{CurrentIndex - 1})");
            if (!CanGoPrev) return;
            CurrentIndex--;
            Current = _photos[CurrentIndex];
            ApplyNavigationReset();
        }
        public void First()
        {
            if (_photos.Count == 0) return;
            CurrentIndex = 0;
            Current = _photos[0];
            ApplyNavigationReset();
        }
        public void Last()
        {
            if (_photos.Count == 0) return;
            CurrentIndex = _photos.Count - 1;
            Current = _photos[CurrentIndex];
            ApplyNavigationReset();
        }

        private void ResetTransform()
        {
            Rotation = 0;
            FlipH = 1;
            FlipV = 1;
            RaisePropertyChanged(nameof(StatusLeftText));
            RaisePropertyChanged(nameof(StatusRightText));
        }

        /// <summary>
        /// 缩略图栏点击切换当前图片。
        /// </summary>
        public void SelectByIndex(object parameter)
        {
            if (parameter is int idx && idx >= 0 && idx < _photos.Count)
            {
                Helpers.DebugLog.Write("VM", $"SelectByIndex {idx} (from thumbstrip)");
                CurrentIndex = idx;
                Current = _photos[idx];
                ApplyNavigationReset();
            }
        }

        /// <summary>
        /// 加载失败后重试当前图片。
        /// </summary>
        public void RetryLoad()
        {
            if (Current != null)
            {
                RaiseImageChangedAsync();
            }
        }

        public void ActivateFromFile(StorageFile file)
        {
            _photos.Clear();
            var photo = new PhotoItem(file);
            _photos.Add(photo);
            CurrentIndex = 0;
            Current = photo;
            CurrentFolder = null;
            SettingsService.LastFolderPath = null;
            RaisePropertyChanged(nameof(ThumbnailVisible));
            // PreloadThumbnailsAsync removed for lazy loading
        }

        // ====== 收藏 ======

        private void ToggleFavorite()
        {
            if (Current == null) return;
            if (FavoritesService.IsFavorite(Current.Path))
            {
                FavoritesService.Remove(Current.Path);
            }
            else
            {
                FavoritesService.Add(Current.Path);
            }
            RefreshFavorite();
        }

        private void RefreshFavorite()
        {
            IsCurrentFavorite = Current != null && FavoritesService.IsFavorite(Current.Path);
        }
// ====== 文件操作 ======

        /// <summary>上次删除是否进入回收站 (供 UI 提示)。</summary>
        public bool LastDeleteToRecycleBin { get; private set; }

        /// <summary>
        /// 删除当前文件并移除列表项 (返回是否成功)。
        /// UWP 无回收站 API, 优先 P/Invoke SHFileOperation, 失败则永久删除。
        /// </summary>
        public async Task<bool> DeleteCurrentAsync()
        {
            if (Current == null) return false;
            var file = Current.File;
            var targetIndex = CanGoNext ? CurrentIndex : (CanGoPrev ? CurrentIndex - 1 : -1);
            bool recycled = false;
            bool removed = false;
            try
            {
                recycled = TryRecycle(file.Path);
                if (recycled) removed = true;
            }
            catch
            {
                recycled = false;
            }
            if (!recycled)
            {
                try
                {
                    await file.DeleteAsync();
                    removed = true;
                }
                catch
                {
                    return false;
                }
            }

            LastDeleteToRecycleBin = recycled;
            FavoritesService.Remove(file.Path);
            _photos.RemoveAt(CurrentIndex);
            if (targetIndex >= 0 && targetIndex < _photos.Count)
            {
                CurrentIndex = targetIndex;
                Current = _photos[targetIndex];
                ApplyNavigationReset();
            }
            else
            {
                CurrentIndex = -1;
                Current = null;
            }
            RaisePropertyChanged(nameof(ThumbnailVisible));
            RaisePropertyChanged(nameof(StatusLeftText));
            RaisePropertyChanged(nameof(StatusRightText));

            return removed;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCTW
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_NOERRORUI = 0x0400;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCTW lpFileOp);

        /// <summary>尝试将文件移入回收站 (不可用时返回 false)。</summary>
        private static bool TryRecycle(string path)
        {
            var op = new SHFILEOPSTRUCTW
            {
                wFunc = FO_DELETE,
                pFrom = path + "\0\0",
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI)
            };
            return SHFileOperation(ref op) == 0;
        }

        /// <summary>
        /// 批量删除 (回收站, 失败则永久删除), 并从图库/收藏中移除, 返回失败路径。
        /// </summary>
        public async Task<List<string>> BatchDeleteAsync(IEnumerable<string> paths)
        {
            var list = (paths ?? Enumerable.Empty<string>()).ToList();
            var failed = new List<string>();
            foreach (var p in list)
            {
                try
                {
                    if (!TryRecycle(p))
                    {
                        var f = await StorageFile.GetFileFromPathAsync(p);
                        await f.DeleteAsync(StorageDeleteOption.PermanentDelete);
                    }
                }
                catch { failed.Add(p); }
            }
            if (list.Count > 0)
            {
                _masterAllPhotos.RemoveAll(x => list.Contains(x.Path, StringComparer.OrdinalIgnoreCase));
                foreach (var p in list) FavoritesService.Remove(p);
                if (Favorites.Any(f => list.Contains(f.Path, StringComparer.OrdinalIgnoreCase)))
                    _ = RefreshFavoritesAsync();
                ApplySearch();
                LibraryEmptyVisible = _masterAlbums.Count == 0 && _masterAllPhotos.Count == 0;
            }
            return failed;
        }

        /// <summary>
        /// 复制当前图片到剪贴板 (懒加载流, 复制时不阻塞 UI)。
        /// </summary>
        public void CopyCurrentToClipboard()
        {
            if (Current == null) return;
            try
            {
                var package = new DataPackage();
                package.RequestedOperation = DataPackageOperation.Copy;
                package.SetBitmap(RandomAccessStreamReference.CreateFromFile(Current.File));
                // SetContentWithOptions 是 1703+ (契约 v4) 才有, 10240 用 SetContent
                if (Helpers.UwpCompat.HasContractV4)
                {
                    Clipboard.SetContentWithOptions(package, null);
                }
                else
                {
                    Clipboard.SetContent(package);
                }
            }
            catch
            {
                // 剪贴板不可用时静默失败
            }
        }

        /// <summary>
        /// 重命名当前文件, 成功后重新扫描文件夹并定位。
        /// </summary>
        public async Task<bool> RenameCurrentAsync(string newName)
        {
            if (Current == null || CurrentFolder == null) return false;
            if (string.IsNullOrWhiteSpace(newName)) return false;
            var oldPath = Current.Path;
            try
            {
                await Current.File.RenameAsync(newName.Trim(), NameCollisionOption.GenerateUniqueName);
            }
            catch
            {
                return false;
            }

            FavoritesService.Remove(oldPath);
            await LoadFolderAsync(CurrentFolder);
            for (int i = 0; i < _photos.Count; i++)
            {
                if (string.Equals(_photos[i].Name, newName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    SelectByIndex(i);
                    break;
                }
            }
            return true;
        }

        // 加载序号: 快速切换时丢弃过期解码结果 (避免旧图晚到覆盖新图)
        private int _loadSeq;

        private bool _isQuickShowing;
        /// <summary>当前显示的是邻居低清占位图 (主图解码中)。</summary>
        public bool IsQuickShowing
        {
            get => _isQuickShowing;
            private set => SetProperty(ref _isQuickShowing, value);
        }

        private async void RaiseImageChangedAsync()
        {
            var seq = ++_loadSeq;
            if (Current == null)
            {
                IsQuickShowing = false;
                DisplayImage = null;
                LoadFailed = false;
                InfoRows = null;
                return;
            }
            IsLoading = true;
            LoadFailed = false;

            // 已有相邻预取的低清版: 立即显示, 高清完成后替换
            if (_neighborCache.TryGetValue(CurrentIndex, out var quick))
            {
                IsQuickShowing = true;
                DisplayImage = quick;
                Helpers.DebugLog.Write("VM", $"Load#{seq} idx={CurrentIndex} quick={quick.PixelWidth}x{quick.PixelHeight} (low-res neighbor)");
            }
            else
            {
                IsQuickShowing = false;
                Helpers.DebugLog.Write("VM", $"Load#{seq} idx={CurrentIndex} no quick, decode high-res...");
            }

            // 开发者模拟 10240: 注入解码延迟, 复现"解码慢 -> ImageOpened 晚 ->
            // 布局/事件时序拉长"的真实 10240 行为 (PixelWidth==0 挂起、timer 兜底等)
            if (Helpers.SettingsService.DebugSimulate10240)
            {
                await Task.Delay(700);
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var bmp = await ImageLoaderService.LoadAsync(Current);
            Helpers.DebugLog.Write("VM", $"Load#{seq} decoded {sw.ElapsedMilliseconds}ms px={(bmp?.PixelWidth ?? 0)}x{(bmp?.PixelHeight ?? 0)} seqOk={seq == _loadSeq}");
            if (seq != _loadSeq) return; // 已被更新的切换取代, 丢弃过期结果
            IsQuickShowing = false;
            DisplayImage = bmp;
            LoadFailed = bmp == null;
            IsLoading = false;
            ResetTransform();
            NextCommand?.NotifyCanExecuteChanged();
            PrevCommand?.NotifyCanExecuteChanged();
            RaisePropertyChanged(nameof(StatusLeftText));
            RaisePropertyChanged(nameof(StatusRightText));

            // 预取相邻 ±2 张低清版
            _ = PrefetchNeighborsAsync(CurrentIndex);

            // 异步加载图片信息 (不阻塞显示)
            try
            {
                var info = await ImageInfoService.LoadAsync(Current);
                if (info != null)
                {
                    Current.PixelWidth = info.Width;
                    Current.PixelHeight = info.Height;
                    InfoRows = new System.Collections.ObjectModel.ObservableCollection<InfoRow>(info.BuildRows());
                }
                else
                {
                    InfoRows = null;
                }
                RaisePropertyChanged(nameof(StatusLeftText));
                RaisePropertyChanged(nameof(StatusRightText));
            }
            catch
            {
                InfoRows = null;
            }

            _ = LoadHistogramAsync(Current);
        }

        // 直方图 (信息面板底部)
        private Windows.UI.Xaml.Media.Imaging.WriteableBitmap _histogramImage;
        public Windows.UI.Xaml.Media.Imaging.WriteableBitmap HistogramImage
        {
            get => _histogramImage;
            private set => SetProperty(ref _histogramImage, value);
        }

        private async Task LoadHistogramAsync(PhotoItem photo)
        {
            try
            {
                using (var stream = await photo.File.OpenReadAsync())
                {
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    uint dw = decoder.PixelWidth, dh = decoder.PixelHeight;
                    uint maxEdge = 512;
                    while (Math.Max(dw, dh) > maxEdge)
                    {
                        dw = (dw + 1) / 2;
                        dh = (dh + 1) / 2;
                    }
                    var data = await decoder.GetPixelDataAsync(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Ignore,
                        new BitmapTransform
                        {
                            ScaledWidth = Math.Max(1, dw),
                            ScaledHeight = Math.Max(1, dh),
                            InterpolationMode = BitmapInterpolationMode.Linear,
                        },
                        ExifOrientationMode.IgnoreExifOrientation,
                        ColorManagementMode.DoNotColorManage);
                    var px = data.DetachPixelData();
                    if (Current == photo && px != null)
                    {
                        HistogramImage = ImageEditService.BuildHistogram(px, (int)dw, (int)dh);
                    }
                }
            }
            catch
            {
                if (Current == photo) HistogramImage = null;
            }
        }

        // ====== 相邻预取 (LRU: 只保留当前 ±2 的 1024px 低清缓存) ======
        private const int PrefetchRange = 2;
        private const int PrefetchSize = 1024;
        private readonly Dictionary<int, BitmapImage> _neighborCache = new Dictionary<int, BitmapImage>();
        private readonly SemaphoreSlim _prefetchGate = new SemaphoreSlim(2);
        private CancellationTokenSource _prefetchCts;

        private async Task PrefetchNeighborsAsync(int center)
        {
            _prefetchCts?.Cancel();
            var cts = new CancellationTokenSource();
            _prefetchCts = cts;

            // 淘汰远离当前项的缓存
            foreach (var k in _neighborCache.Keys
                .Where(k => Math.Abs(k - center) > PrefetchRange)
                .ToList())
            {
                _neighborCache.Remove(k);
            }

            var order = new[] { 1, -1, 2, -2 };
            foreach (var off in order)
            {
                int idx = center + off;
                if (idx < 0 || idx >= _photos.Count) continue;
                if (cts.Token.IsCancellationRequested) return;
                await PrefetchOneAsync(idx, cts.Token);
            }
        }

        private async Task PrefetchOneAsync(int idx, CancellationToken token)
        {
            if (_neighborCache.ContainsKey(idx)) return;
            await _prefetchGate.WaitAsync();
            try
            {
                if (token.IsCancellationRequested) return;
                var bmp = await ImageLoaderService.LoadAsync(_photos[idx], PrefetchSize);
                if (bmp != null && !token.IsCancellationRequested)
                {
                    _neighborCache[idx] = bmp;
                }
            }
            finally
            {
                _prefetchGate.Release();
            }
        }

        // ====== 幻灯片 ======
        private CancellationTokenSource _slideShowCts;
        private readonly Random _rng = new Random();
        private static readonly string[] TransitionNames = { "Fade", "Zoom", "Pan", "Flicker" };

        /// <summary>
        /// 返回图库主页 (清除当前图片)。
        /// </summary>
        public void GoHome()
        {
            StopSlideShow();
            // 离开看图页: 清掉按 index 存的邻居预取缓存, 防止下次打开同列表
            // 图片时先显示上次会话的错误邻居图
            _neighborCache.Clear();
            if (Current != null)
            {
                Current = null;
            }
            InfoPanelOpen = false;
        }

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
            // 模糊背景播放结束后恢复设置里的背景
            RefreshSettings();
        }

        /// <summary>幻灯片播放进度 (0~1, 用于底部进度条)。</summary>
        public double SlideShowProgress
        {
            get
            {
                if (_photos.Count == 0) return 0;
                return (double)(CurrentIndex + 1) / _photos.Count;
            }
        }

        private async Task RunSlideShowAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(SlideShowSeconds), token); }
                catch (TaskCanceledException) { break; }
                if (token.IsCancellationRequested) break;

                if (SettingsService.SlideRandomTransition)
                {
                    SettingsService.SlideTransition = TransitionNames[_rng.Next(TransitionNames.Length)];
                }

                if (SettingsService.SlideRandomOrder && _photos.Count > 1)
                {
                    int idx;
                    do { idx = _rng.Next(_photos.Count); } while (idx == CurrentIndex);
                    CurrentIndex = idx;
                    Current = _photos[idx];
                    ApplyNavigationReset();
                }
                else if (CanGoNext) Next();
                else First();

                RaisePropertyChanged(nameof(SlideShowProgress));
                _ = UpdateSlideBackgroundAsync(Current);
            }
        }

        /// <summary>
        /// 幻灯片模糊背景: 取当前图片缩小版做盒式模糊, 铺满主区背景。
        /// </summary>
        private async Task UpdateSlideBackgroundAsync(PhotoItem photo)
        {
            if (!SlideShowRunning || !SettingsService.SlideBlurBackground || photo == null) return;
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(photo.Path);
                using (var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read))
                {
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    using (var sb = await decoder.GetSoftwareBitmapAsync(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied,
                        new BitmapTransform
                        {
                            ScaledWidth = 192,
                            ScaledHeight = 192,
                            InterpolationMode = BitmapInterpolationMode.Fant
                        },
                        ExifOrientationMode.IgnoreExifOrientation,
                        ColorManagementMode.DoNotColorManage))
                    {
                        var bytes = new byte[sb.PixelWidth * sb.PixelHeight * 4];
                        sb.CopyToBuffer(bytes.AsBuffer());
                        BoxBlur(bytes, sb.PixelWidth, sb.PixelHeight, 12);
                        var wb = new WriteableBitmap(sb.PixelWidth, sb.PixelHeight);
                        using (var wbs = wb.PixelBuffer.AsStream())
                        {
                            wbs.Write(bytes, 0, bytes.Length);
                        }
                        wb.Invalidate();
                        if (!SlideShowRunning) return;
                        MainBackground = new ImageBrush
                        {
                            ImageSource = wb,
                            Stretch = Stretch.UniformToFill
                        };
                    }
                }
            }
            catch
            {
                // 个别图片解码失败时保持原背景
            }
        }

        private static void BoxBlur(byte[] pixels, int w, int h, int radius)
        {
            var tmp = new byte[pixels.Length];
            int stride = w * 4;
            for (int c = 0; c < 4; c++)
            {
                // 水平方向
                for (int y = 0; y < h; y++)
                {
                    int rowBase = y * stride + c;
                    int sum = 0, count = 0;
                    for (int x = -radius; x <= radius; x++)
                    {
                        int sx = x < 0 ? 0 : (x >= w ? w - 1 : x);
                        sum += pixels[rowBase + sx * 4];
                        count++;
                    }
                    for (int x = 0; x < w; x++)
                    {
                        tmp[rowBase + x * 4] = (byte)(sum / count);
                        int add = x + radius + 1;
                        int sub = x - radius;
                        if (add < w) { sum += pixels[rowBase + add * 4]; count++; }
                        if (sub >= 0) { sum -= pixels[rowBase + sub * 4]; count--; }
                    }
                }
                // 垂直方向
                for (int x = 0; x < w; x++)
                {
                    int sum = 0, count = 0;
                    for (int y = -radius; y <= radius; y++)
                    {
                        int sy = y < 0 ? 0 : (y >= h ? h - 1 : y);
                        sum += tmp[sy * stride + x * 4 + c];
                        count++;
                    }
                    for (int y = 0; y < h; y++)
                    {
                        pixels[y * stride + x * 4 + c] = (byte)(sum / count);
                        int add = y + radius + 1;
                        int sub = y - radius;
                        if (add < h) { sum += tmp[add * stride + x * 4 + c]; count++; }
                        if (sub >= 0) { sum -= tmp[sub * stride + x * 4 + c]; count--; }
                    }
                }
            }
        }

        // ====== 最近打开 ======
        private readonly RecentFoldersService _recent;
        public System.Collections.Generic.IReadOnlyList<StorageFolder> RecentFolders => _recent.Folders;
        public RecentFoldersService RecentFoldersService => _recent;

        public async Task OpenRecentAsync(StorageFolder folder)
        {
            await LoadFolderAsync(folder);
        }
    }
}
