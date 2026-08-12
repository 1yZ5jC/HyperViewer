using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Media.Animation;
using HyperViewer.Helpers;
using HyperViewer.Models;
using HyperViewer.ViewModels;
using HyperViewer.Services;
using Windows.Storage;

namespace HyperViewer
{
    /// <summary>
    /// 主页：图片浏览 + 工具栏 + 缩略图栏 + 状态栏。
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainViewModel Vm { get; } = new MainViewModel();

        public MenuFlyout AlbumContextFlyout => Resources["AlbumContextFlyout"] as MenuFlyout;
        public MenuFlyout PhotoContextFlyout => Resources["PhotoContextFlyout"] as MenuFlyout;
        public MenuFlyout FolderContextFlyout => Resources["FolderContextFlyout"] as MenuFlyout;

        // UI 显隐 (顶栏/胶片条/状态栏): 全屏自动隐藏, 单击图片区切换
        private bool _chromeVisible = true;
        private bool _thumbStripEnabled = true;
        private DispatcherTimer _tapTimer;
        private bool _doubleTapPending;

        // 中央导航箭头: 鼠标移动显示, 空闲 2s 隐藏
        private DispatcherTimer _navTimer;
        private bool _navArrowsVisible;

        // 缩放滑块同步
        private bool _syncingSlider;

        public MainPage()
        {
            this.InitializeComponent();
            this.DataContext = Vm;
            Vm.PropertyChanged += OnVmPropertyChanged;
            Vm.LibraryTabChanged += OnLibraryTabChanged;
            // 启动初始态: 主页 (无图) 时隐藏悬浮顶栏, 图片模式才显示
            SetChrome(!Vm.HomeVisible);
            Viewer.ZoomFactorChanged += (_, __) =>
            {
                Vm.UpdateZoomFactor(Viewer.CurrentZoomFactor);
                UpdateZoomSlider();
            };
            Viewer.DoubleTappedOccurred += (_, __) =>
            {
                _doubleTapPending = true;
                _tapTimer?.Stop();
                var view = ApplicationView.GetForCurrentView();
                if (view.IsFullScreenMode)
                {
                    view.ExitFullScreenMode();
                    SetChrome(true);
                }
            };
            _tapTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
            _tapTimer.Tick += (_, __) =>
            {
                _tapTimer.Stop();
                if (!_doubleTapPending)
                {
                    ToggleChrome();
                }
                _doubleTapPending = false;
            };
            _navTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _navTimer.Tick += (_, __) => SetNavArrows(false);
            ZoomSlider.ValueChanged += ZoomSlider_ValueChanged;
            // 键盘快捷键注册
            CoreWindow.GetForCurrentThread().Dispatcher.AcceleratorKeyActivated += CoreWindow_AcceleratorKeyActivated;
            UpdateZoomSlider();
        }

        private void UpdateZoomSlider()
        {
            if (_syncingSlider) return;
            _syncingSlider = true;
            ZoomSlider.Value = Math.Max(ZoomSlider.Minimum,
                                        Math.Min(ZoomSlider.Maximum,
                                                 Viewer.CurrentZoomFactor * 100));
            _syncingSlider = false;
        }

        private void ZoomSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_syncingSlider) return;
            _syncingSlider = true;
            Viewer.SetZoomFactor((float)(e.NewValue / 100.0));
            _syncingSlider = false;
        }

        private void OnVmPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.CurrentIndex):
                    SyncThumbnailSelection();
                    break;
                case nameof(MainViewModel.InfoPanelOpen):
                    InfoColumn.Width = Vm.InfoPanelOpen ? new GridLength(320) : new GridLength(0);
                    break;
                case nameof(MainViewModel.DisplayImage):
                    Viewer.FadeIn();
                    break;
                case nameof(MainViewModel.HomeVisible):
                    SetChrome(Vm.HomeVisible ? false : true);
                    break;
            }
        }

        private async void OnLibraryTabChanged(object sender, MainViewModel.LibraryTabKind newTab)
        {
            // 简单的淡入淡出切换动画
            var grids = new FrameworkElement[] { AlbumsGrid, AllPhotosGrid, FoldersGrid };
            FrameworkElement targetGrid = null;
            if (newTab == MainViewModel.LibraryTabKind.Albums)
                targetGrid = AlbumsGrid;
            else if (newTab == MainViewModel.LibraryTabKind.AllPhotos)
                targetGrid = AllPhotosGrid;
            else if (newTab == MainViewModel.LibraryTabKind.Folders)
                targetGrid = FoldersGrid;

            if (targetGrid == null) return;

            // 淡出当前显示的网格
            foreach (var grid in grids)
            {
                if (grid != targetGrid && grid.Visibility == Visibility.Visible)
                {
                    var fadeOut = new DoubleAnimation { From = 1, To = 0, Duration = TimeSpan.FromMilliseconds(150) };
                    var sb = new Storyboard();
                    sb.Children.Add(fadeOut);
                    Storyboard.SetTarget(fadeOut, grid);
                    Storyboard.SetTargetProperty(fadeOut, "Opacity");
                    sb.Completed += (s, e) => grid.Visibility = Visibility.Collapsed;
                    sb.Begin();
                }
            }

            // 显示目标网格并淡入
            if (targetGrid.Visibility != Visibility.Visible)
            {
                targetGrid.Opacity = 0;
                targetGrid.Visibility = Visibility.Visible;
            }
            var fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(200) };
            var sbIn = new Storyboard();
            sbIn.Children.Add(fadeIn);
            Storyboard.SetTarget(fadeIn, targetGrid);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            sbIn.Begin();
        }

        // ====== UI 显隐 ======

        private void SetChrome(bool visible)
        {
            _chromeVisible = visible;
            ApplyChrome();
        }

        private void ApplyChrome()
        {
            TopBar.Visibility = _chromeVisible ? Visibility.Visible : Visibility.Collapsed;
            StatusBar.Visibility = _chromeVisible ? Visibility.Visible : Visibility.Collapsed;
            ThumbBar.Visibility = (_chromeVisible && _thumbStripEnabled) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ToggleChrome()
        {
            if (!Vm.HasImage) return;
            SetChrome(!_chromeVisible);
            SetNavArrows(false);
        }

        private void ToggleThumbStrip()
        {
            _thumbStripEnabled = !_thumbStripEnabled;
            ApplyChrome();
        }

        // ====== 中央导航箭头 ======

        private void ImageArea_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!Vm.HasImage) return;
            if (!_navArrowsVisible)
            {
                SetNavArrows(true);
            }
            _navTimer.Stop();
            _navTimer.Start();
        }

        private void SetNavArrows(bool visible)
        {
            _navArrowsVisible = visible;
            var vis = visible && Vm.HasImage ? Visibility.Visible : Visibility.Collapsed;
            PrevArrow.Visibility = vis;
            NextArrow.Visibility = vis;
            PrevArrow.IsHitTestVisible = visible;
            NextArrow.IsHitTestVisible = visible;
        }

        private void ImageArea_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (!Vm.HasImage) return;
            // 未放大时: 点击左/右 1/4 区域翻页, 中间区域切换 UI 显隐
            if (Vm.ZoomFactor <= 1.05)
            {
                var pos = e.GetPosition(ImageArea);
                var w = ImageArea.ActualWidth;
                if (pos.X < w * 0.25)
                {
                    Vm.Prev();
                    return;
                }
                if (pos.X > w * 0.75)
                {
                    Vm.Next();
                    return;
                }
            }
            _doubleTapPending = false;
            _tapTimer.Stop();
            _tapTimer.Start();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            this.Focus(FocusState.Programmatic);
            Vm.RefreshSettings();
            if (e.Parameter is TimelineRequest req)
            {
                _ = HandleTimelineRequestAsync(req);
            }
            else if (e.Parameter is StorageFile file)
            {
                Vm.ActivateFromFile(file);
            }
            // 全屏时按 Esc 退出
            Window.Current.CoreWindow.Dispatcher.AcceleratorKeyActivated += CoreWindow_AcceleratorKeyActivated;
        }

        private async System.Threading.Tasks.Task HandleTimelineRequestAsync(TimelineRequest req)
        {
            await Vm.LoadFolderAsync(req.Folder);
            Vm.SelectFile(req.File);
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (Vm.Current == null) return;
            Vm.StopSlideShow();
            Frame.Navigate(typeof(EditPage), Vm.Current);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Vm.StopSlideShow();
            Frame.Navigate(typeof(SettingsPage));
        }

        private void TimelineButton_Click(object sender, RoutedEventArgs e)
        {
            if (Vm.CurrentFolder == null) return;
            Vm.StopSlideShow();
            Frame.Navigate(typeof(TimelinePage), Vm.CurrentFolder);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            Window.Current.CoreWindow.Dispatcher.AcceleratorKeyActivated -= CoreWindow_AcceleratorKeyActivated;
            Vm.StopSlideShow();
        }

        private void CoreWindow_AcceleratorKeyActivated(CoreDispatcher sender, AcceleratorKeyEventArgs args)
        {
            // args.EventType 是 CoreAcceleratorKeyEventType.KeyDown (==1)
            if (args.EventType == CoreAcceleratorKeyEventType.KeyDown)
            {
                HandleKey(args.VirtualKey);
            }
        }

        private void SyncThumbnailSelection()
        {
            if (Vm.CurrentIndex >= 0 && Vm.CurrentIndex < ThumbList.Items.Count)
            {
                var prev = ThumbList.SelectedIndex;
                if (prev != Vm.CurrentIndex)
                {
                    ThumbList.SelectedIndex = Vm.CurrentIndex;
                }
                // 滚到当前项
                if (ThumbList.ContainerFromIndex(Vm.CurrentIndex) is FrameworkElement fe)
                {
                    fe.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true });
                }
            }
        }

        private void ThumbList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThumbList.SelectedIndex >= 0 && ThumbList.SelectedIndex != Vm.CurrentIndex)
            {
                Vm.SelectByIndex(ThumbList.SelectedIndex);
            }
        }

        private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (HandleKey(e.Key)) e.Handled = true;
        }

        private bool HandleKey(VirtualKey key)
        {
            switch (key)
            {
                case VirtualKey.C:
                    var ctrlDown = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control)
                                   .HasFlag(CoreVirtualKeyStates.Down);
                    if (ctrlDown)
                    {
                        Vm.CopyCurrentToClipboard();
                        return true;
                    }
                    return false;
                case VirtualKey.Left:
                case VirtualKey.Up:
                    Vm.Prev();
                    return true;
                case VirtualKey.Right:
                case VirtualKey.Down:
                    Vm.Next();
                    return true;
                case VirtualKey.Space:
                    Vm.Next();
                    return true;
                case VirtualKey.Home:
                    Vm.First();
                    return true;
                case VirtualKey.End:
                    Vm.Last();
                    return true;
                case VirtualKey.R:
                    var ctrl = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control);
                    if ((ctrl & CoreVirtualKeyStates.Down) != 0)
                    {
                        Vm.RotateBackCommand.Execute(null);
                    }
                    else
                    {
                        Vm.RotateCommand.Execute(null);
                    }
                    return true;
                case VirtualKey.H:
                    Vm.FlipHCommand.Execute(null);
                    return true;
                case VirtualKey.V:
                    Vm.FlipVCommand.Execute(null);
                    return true;
                case VirtualKey.Number0:
                    Vm.ResetTransformCommand.Execute(null);
                    Viewer.ResetView();
                    return true;
                case VirtualKey.F:
                    // Ctrl+F: 聚焦搜索框, 否则切换缩略图栏
                    var ctrlF = (Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
                    if (ctrlF && SearchBox != null)
                    {
                        SearchBox.Focus(FocusState.Programmatic);
                        return true;
                    }
                    else
                    {
                        ToggleThumbStrip();
                        return true;
                    }
                case VirtualKey.O:
                    // Ctrl+O: 打开图片
                    var ctrlO = (Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
                    if (ctrlO)
                    {
                        Vm.OpenImageCommand.Execute(null);
                        return true;
                    }
                    return false;
                case VirtualKey.F11:
                    ToggleFullScreen();
                    return true;
                case VirtualKey.F5:
                    Vm.ToggleSlideShow();
                    return true;
                case VirtualKey.Escape:
                    if (Vm.SlideShowRunning)
                    {
                        Vm.StopSlideShow();
                        return true;
                    }
                    var view = ApplicationView.GetForCurrentView();
                    if (view.IsFullScreenMode)
                    {
                        view.ExitFullScreenMode();
                        SetChrome(true);
                        return true;
                    }
                    return false;
                case VirtualKey.Add:
                    ZoomByKeys(1.2f);
                    return true;
                case VirtualKey.Subtract:
                    ZoomByKeys(1f / 1.2f);
                    return true;
                default:
                    return false;
            }
        }

        private void ZoomByKeys(float factor)
        {
            var center = new Windows.Foundation.Point(Viewer.ActualWidth / 2, Viewer.ActualHeight / 2);
            Viewer.ZoomAt(center, factor);
        }

        // --------- 懒加载照片缩略图 ---------
        private async void PhotoThumbnail_Loaded(object sender, RoutedEventArgs e)
        {
            var img = sender as Image;
            if (img?.DataContext is PhotoItem photo && !photo.ThumbnailLoaded)
            {
                var bmp = await ImageLoaderService.LoadThumbnailAsync(photo);
                if (bmp != null)
                {
                    photo.Thumbnail = bmp;
                    photo.ThumbnailLoaded = true;
                }
            }
        }

        private void ToggleFullScreen_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullScreen();
        }

        private void ToggleFullScreen()
        {
            var view = ApplicationView.GetForCurrentView();
            if (view.IsFullScreenMode)
            {
                view.ExitFullScreenMode();
                SetChrome(true);
            }
            else
            {
                view.TryEnterFullScreenMode();
                SetChrome(false);
            }
        }

        private async void RecentButton_Click(object sender, RoutedEventArgs e)
        {
            var recent = Vm.RecentFolders;
            if (recent == null || recent.Count == 0)
            {
                var dlg = new ContentDialog
                {
                    Title = Loc.Get("RecentNone"),
                    Content = Loc.Get("RecentEmpty"),
                    CloseButtonText = Loc.Get("DialogOK")
                };
                await dlg.ShowAsync();
                return;
            }

            var panel = new StackPanel();
            foreach (var f in recent)
            {
                var btn = new Button
                {
                    Content = f.Name,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 2, 0, 2),
                    Tag = f
                };
                btn.Click += RecentPick_Click;
                btn.SetValue(ToolTipService.ToolTipProperty, f.Path);
                panel.Children.Add(btn);
            }

            var dlg2 = new ContentDialog
            {
                Title = Loc.Get("RecentTitle"),
                Content = panel,
                CloseButtonText = Loc.Get("DialogCancel")
            };
            await dlg2.ShowAsync();
        }

        private void RecentPick_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is StorageFolder folder)
            {
                _ = Vm.OpenRecentAsync(folder);
            }
        }

        private void HomeRecent_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is StorageFolder folder)
            {
                _ = Vm.OpenRecentAsync(folder);
            }
        }

        // ====== 图库视图点击 ======

        private void AlbumGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is AlbumItem album)
            {
                _ = Vm.OpenAlbumAsync(album);
            }
        }

        private void PhotoGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is PhotoItem photo)
            {
                _ = Vm.OpenPhotoFromLibraryAsync(photo);
            }
        }

        private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            // 用户提交搜索（按 Enter 或点击搜索图标），搜索逻辑在 SearchText 属性绑定中已触发
        }

        private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is string suggestion)
            {
                sender.Text = suggestion;
            }
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                var term = sender.Text?.ToLowerInvariant() ?? string.Empty;
                var suggestions = new List<string>();

                if (!string.IsNullOrWhiteSpace(term))
                {
                    // 从相册名称中匹配
                    suggestions.AddRange(Vm.Albums
                        .Where(a => a.Name?.ToLowerInvariant().Contains(term) == true)
                        .Select(a => a.Name)
                        .Distinct()
                        .Take(5));

                    // 从照片名称中匹配
                    suggestions.AddRange(Vm.AllPhotos
                        .Where(p => p.Name?.ToLowerInvariant().Contains(term) == true)
                        .Select(p => p.Name)
                        .Distinct()
                        .Take(5));
                }

                sender.ItemsSource = suggestions.Distinct().ToList();
            }
        }

        private void CoverImage_ImageOpened(object sender, RoutedEventArgs e)
        {
            if (sender is Image img)
            {
                // 隐藏加载指示器
                if (img.Parent is Grid grid)
                {
                    var loadingRing = grid.FindName("CoverLoadingRing") as ProgressRing;
                    if (loadingRing != null)
                    {
                        loadingRing.Visibility = Visibility.Collapsed;
                        loadingRing.IsActive = false;
                    }
                }
                // 淡入动画
                var fadeIn = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var storyboard = new Storyboard();
                storyboard.Children.Add(fadeIn);
                Storyboard.SetTarget(fadeIn, img);
                Storyboard.SetTargetProperty(fadeIn, "Opacity");
                storyboard.Begin();
            }
        }

        private void AlbumsGrid_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is GridView gv && e.OriginalSource is FrameworkElement fe)
            {
                var item = gv.ContainerFromItem(fe.DataContext) as GridViewItem;
                if (item?.DataContext is AlbumItem album)
                {
                    var flyout = Resources["AlbumContextFlyout"] as MenuFlyout;
                    flyout?.ShowAt(item, e.GetPosition(item));
                    e.Handled = true;
                }
            }
        }

        private void AllPhotosGrid_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is GridView gv && e.OriginalSource is FrameworkElement fe)
            {
                var item = gv.ContainerFromItem(fe.DataContext) as GridViewItem;
                if (item?.DataContext is PhotoItem photo)
                {
                    var flyout = Resources["PhotoContextFlyout"] as MenuFlyout;
                    flyout?.ShowAt(item, e.GetPosition(item));
                    e.Handled = true;
                }
            }
        }

        private void RecentFolder_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is StorageFolder folder)
            {
                var flyout = Resources["FolderContextFlyout"] as MenuFlyout;
                flyout?.ShowAt(btn, e.GetPosition(btn));
                e.Handled = true;
            }
        }

        private async Task LoadFolderMetadataAsync()
        {
            if (Vm.RecentFolders == null) return;
            
            foreach (var folder in Vm.RecentFolders)
            {
                try
                {
                    var files = await folder.GetFilesAsync();
                    var imageFiles = files.Where(f => f.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)).ToList();
                    var count = imageFiles.Count;
                    var lastModified = files.Any() ? files.Max(f => f.DateCreated) : folder.DateCreated;
                    
                    // 在UI线程上更新UI
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        // 这里需要找到对应的UI元素并更新，简化处理
                    });
                }
                catch { }
            }
        }

        // ====== 文件操作 ======

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (Vm.Current == null) return;
            var dlg = new ContentDialog
            {
                Title = Loc.Get("DeleteTitle"),
                Content = Loc.Format("DeleteMessage", Vm.Current.Name),
                PrimaryButtonText = Loc.Get("DeletePrimary"),
                CloseButtonText = Loc.Get("DialogCancel")
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            {
                bool ok = await Vm.DeleteCurrentAsync();
                if (!ok)
                {
                    var err = new ContentDialog
                    {
                        Title = Loc.Get("DeleteFailTitle"),
                        Content = Loc.Get("DeleteFailMessage"),
                        CloseButtonText = Loc.Get("DialogOK")
                    };
                    await err.ShowAsync();
                }
                else if (!Vm.LastDeleteToRecycleBin)
                {
                    var info = new ContentDialog
                    {
                        Title = Loc.Get("DeleteFailTitle"),
                        Content = Loc.Get("DeleteRecycleFallbackMessage"),
                        CloseButtonText = Loc.Get("DialogOK")
                    };
                    await info.ShowAsync();
                }
            }
        }

        private async void Rename_Click(object sender, RoutedEventArgs e)
        {
            if (Vm.Current == null || Vm.CurrentFolder == null) return;
            var input = new TextBox
            {
                Text = Vm.Current.Name,
                SelectionStart = 0,
                SelectionLength = Vm.Current.Name.Length - (System.IO.Path.GetExtension(Vm.Current.Name).Length),
                Margin = new Thickness(0, 12, 0, 0)
            };
            var dlg = new ContentDialog
            {
                Title = Loc.Get("RenameTitle"),
                Content = input,
                PrimaryButtonText = Loc.Get("RenamePrimary"),
                CloseButtonText = Loc.Get("DialogCancel")
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            {
bool ok = await Vm.RenameCurrentAsync(input.Text);
            if (!ok)
            {
                var err = new ContentDialog
                {
                    Title = Loc.Get("RenameFailTitle"),
                    Content = Loc.Get("RenameFailMessage"),
                    CloseButtonText = Loc.Get("DialogOK")
                };
                await err.ShowAsync();
            }
        }
    }

    // ====== 右键/长按上下文菜单处理 ======

    // 相册上下文菜单
    private async void AlbumContext_Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is AlbumItem album)
            await Vm.OpenAlbumAsync(album);
    }

    private async void AlbumContext_OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is AlbumItem album && album.Folder != null)
            await Vm.LoadFolderAsync(album.Folder);
    }

    private async void AlbumContext_Rename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is AlbumItem album && album.Folder != null)
        {
            var input = new TextBox { Text = album.Name, Margin = new Thickness(0, 12, 0, 0) };
            var dlg = new ContentDialog
            {
                Title = Loc.Get("RenameTitle"),
                Content = input,
                PrimaryButtonText = Loc.Get("RenamePrimary"),
                CloseButtonText = Loc.Get("DialogCancel")
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            {
                try
                {
                    await album.Folder.RenameAsync(input.Text.Trim(), NameCollisionOption.GenerateUniqueName);
                    await Vm.RefreshLibraryAsync();
                }
                catch { }
            }
        }
    }

    private async void AlbumContext_Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is AlbumItem album && album.Folder != null)
        {
            var dlg = new ContentDialog
            {
                Title = Loc.Get("DeleteTitle"),
                Content = Loc.Format("DeleteMessage", album.Name),
                PrimaryButtonText = Loc.Get("DeletePrimary"),
                CloseButtonText = Loc.Get("DialogCancel")
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            {
                try
                {
                    await album.Folder.DeleteAsync();
                    await Vm.RefreshLibraryAsync();
                }
                catch { }
            }
        }
    }

    private void AlbumContext_Properties_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is AlbumItem album)
        {
            var info = new ContentDialog
            {
                Title = Loc.Get("PropertiesTitle"),
                Content = new TextBlock { Text = $"名称: {album.Name}\n路径: {album.Path}\n图片数: {album.Count}\n日期范围: {album.DateRangeText}", TextWrapping = TextWrapping.Wrap },
                CloseButtonText = Loc.Get("DialogOK")
            };
            _ = info.ShowAsync();
        }
    }

    // 照片上下文菜单
    private async void PhotoContext_Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is PhotoItem photo)
            await Vm.OpenPhotoFromLibraryAsync(photo);
    }

    private async void PhotoContext_OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is PhotoItem photo && photo.File != null)
        {
            var folder = await photo.File.GetParentAsync();
            if (folder != null) await Vm.LoadFolderAsync(folder);
        }
    }

    private void PhotoContext_CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is PhotoItem photo)
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(photo.Path);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
    }

    private async void PhotoContext_Rename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is PhotoItem photo && photo.File != null)
        {
            var input = new TextBox { Text = photo.Name, Margin = new Thickness(0, 12, 0, 0) };
            var dlg = new ContentDialog
            {
                Title = Loc.Get("RenameTitle"),
                Content = input,
                PrimaryButtonText = Loc.Get("RenamePrimary"),
                CloseButtonText = Loc.Get("DialogCancel")
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            {
                try
                {
                    await photo.File.RenameAsync(input.Text.Trim(), NameCollisionOption.GenerateUniqueName);
                    await Vm.RefreshLibraryAsync();
                }
                catch { }
            }
        }
    }

    private async void PhotoContext_Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is PhotoItem photo && photo.File != null)
        {
            var dlg = new ContentDialog
            {
                Title = Loc.Get("DeleteTitle"),
                Content = Loc.Format("DeleteMessage", photo.Name),
                PrimaryButtonText = Loc.Get("DeletePrimary"),
                CloseButtonText = Loc.Get("DialogCancel")
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            {
                try
                {
                    await photo.File.DeleteAsync();
                    await Vm.RefreshLibraryAsync();
                }
                catch { }
            }
        }
    }

    private void PhotoContext_Properties_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is PhotoItem photo)
        {
            var info = new ContentDialog
            {
                Title = Loc.Get("PropertiesTitle"),
                Content = new TextBlock { Text = $"名称: {photo.Name}\n路径: {photo.Path}\n创建时间: {photo.DateCreated}", TextWrapping = TextWrapping.Wrap },
                CloseButtonText = Loc.Get("DialogOK")
            };
            _ = info.ShowAsync();
        }
    }

    // 文件夹上下文菜单
    private async void FolderContext_Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is StorageFolder folder)
            await Vm.LoadFolderAsync(folder);
    }

    private async void FolderContext_Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is StorageFolder folder)
        {
            var dlg = new ContentDialog
            {
                Title = Loc.Get("RemoveTitle"),
                Content = Loc.Format("RemoveMessage", folder.Name),
                PrimaryButtonText = Loc.Get("RemovePrimary"),
                CloseButtonText = Loc.Get("DialogCancel")
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            {
                await Vm.RecentFoldersService.RemoveAsync(folder);
                await Vm.RefreshLibraryAsync();
            }
        }
    }

    private void FolderContext_Properties_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is StorageFolder folder)
        {
            var info = new ContentDialog
            {
                Title = Loc.Get("PropertiesTitle"),
                Content = new TextBlock { Text = $"名称: {folder.Name}\n路径: {folder.Path}", TextWrapping = TextWrapping.Wrap },
                CloseButtonText = Loc.Get("DialogOK")
            };
            _ = info.ShowAsync();
}
}
}
}
