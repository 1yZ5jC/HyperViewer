using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
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
        private readonly Windows.UI.Xaml.Media.TranslateTransform _thumbBarTransform = new Windows.UI.Xaml.Media.TranslateTransform();

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
            UpdateZoomSlider();
            SetupTitleBar();
            // 返回时复用页面实例 (UWP 默认 Disabled 会在 GoBack 时重建页面,
            // 新 VM 的 Current=null, 导致"退出编辑/设置后回到主页"而非图片视图)
            this.NavigationCacheMode = NavigationCacheMode.Required;
        }

        // 标题栏集成 (Win11 Photos 风格): 顶栏占满标题栏区域, 可拖拽, 右上角保留系统窗口按钮
        private bool _titleBarHooked;

        private void SetupTitleBar()
        {
            var titleBar = CoreApplication.GetCurrentView().TitleBar;
            titleBar.ExtendViewIntoTitleBar = true;
            Window.Current.SetTitleBar(TitleBarDragRegion);
            if (!_titleBarHooked)
            {
                _titleBarHooked = true;
                titleBar.LayoutMetricsChanged += (_, __) => UpdateTitleBarInsets();
                Window.Current.SizeChanged += (_, __) => UpdateTitleBarInsets();
            }
            UpdateTitleBarInsets();
        }

        private void UpdateTitleBarInsets()
        {
            double inset = 0;
            try { inset = CoreApplication.GetCurrentView().TitleBar.SystemOverlayRightInset; }
            catch { }
            if (HomePad != null) HomePad.Width = new GridLength(inset);
            if (ImagePad != null) ImagePad.Width = new GridLength(inset);
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
                case nameof(MainViewModel.ThumbnailVisible):
                    ApplyChrome();
                    break;
            }
        }

        private void OnLibraryTabChanged(object sender, MainViewModel.LibraryTabKind newTab)
        {
            // 简单的淡入淡出切换动画
            var grids = new FrameworkElement[] { AlbumsGrid, AllPhotosGrid, FoldersGrid, FavoritesGrid };
            FrameworkElement targetGrid = null;
            if (newTab == MainViewModel.LibraryTabKind.Albums)
                targetGrid = AlbumsGrid;
            else if (newTab == MainViewModel.LibraryTabKind.AllPhotos)
                targetGrid = AllPhotosGrid;
            else if (newTab == MainViewModel.LibraryTabKind.Folders)
                targetGrid = FoldersGrid;
            else if (newTab == MainViewModel.LibraryTabKind.Favorites)
                targetGrid = FavoritesGrid;

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
            // 注意: 不要在此处 Stop 上一个 Storyboard —— UWP 中 Stop() 会把动画属性回退到基值,
            // 缩略图条滑入动画(基值 Y=92, 屏幕外)一旦被中断就永久卡在屏幕外。
            // 新动画会自动接管同一属性的旧动画, 保持状态一致性。

            var showThumb = _chromeVisible && _thumbStripEnabled && Vm.ThumbnailVisible;
            var sb = new Storyboard();
            var duration = TimeSpan.FromMilliseconds(showThumb ? 240 : 180);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            void Anim(DependencyObject target, string prop, double to)
            {
                var anim = new DoubleAnimation { To = to, Duration = duration, EasingFunction = ease };
                Storyboard.SetTarget(anim, target);
                Storyboard.SetTargetProperty(anim, prop);
                sb.Children.Add(anim);
            }

            // 缩略图栏: 滑入/滑出 (基于状态而非 Visibility 判断, 中断后仍能回到正确位置)
            if (showThumb)
            {
                var wasHidden = ThumbBar.Visibility != Visibility.Visible;
                if (wasHidden)
                {
                    ThumbBar.Visibility = Visibility.Visible;
                    ThumbBar.RenderTransform = _thumbBarTransform;
                    _thumbBarTransform.Y = 92;
                    ThumbBar.Opacity = 0;
                }
                if (wasHidden || _thumbBarTransform.Y != 0 || ThumbBar.Opacity != 1)
                {
                    Anim(_thumbBarTransform, "Y", 0);
                    Anim(ThumbBar, "Opacity", 1);
                }
            }
            else if (ThumbBar.Visibility == Visibility.Visible)
            {
                Anim(_thumbBarTransform, "Y", 92);
                Anim(ThumbBar, "Opacity", 0);
            }

            // 顶栏 / 状态栏: 淡入淡出 (图片顶栏只在图片模式显示)
            var home = Vm.HomeVisible;
            if (_chromeVisible)
            {
                if (!home && ImageTopBar.Visibility != Visibility.Visible)
                {
                    ImageTopBar.Visibility = Visibility.Visible;
                }
                StatusBar.Visibility = Visibility.Visible;
                if (!home && ImageTopBar.Opacity < 1.0) Anim(ImageTopBar, "Opacity", 1);
                if (StatusBar.Opacity < 1.0) Anim(StatusBar, "Opacity", 1);
            }
            else
            {
                if (!home) Anim(ImageTopBar, "Opacity", 0);
                Anim(StatusBar, "Opacity", 0);
            }

            // 全屏(chrome 隐藏)时整条标题栏区域放行指针事件, 便于图片交互与顶部悬停唤出
            TitleBarHost.IsHitTestVisible = home || _chromeVisible;

            if (sb.Children.Count > 0)
            {
                sb.Completed += (_, __) =>
                {
                    // 不能用闭包里的 showThumb/home: 进入图片视图时 Current 连续触发两次
                    // ApplyChrome, 第一个 Storyboard(showThumb=false)完成时第二个已把胶片条显示出来,
                    // 用过期闭包会把它重新折叠。这里按完成时刻的当前状态重新计算。
                    var shouldShowThumb = _chromeVisible && _thumbStripEnabled && Vm.ThumbnailVisible;
                    if (!shouldShowThumb)
                    {
                        ThumbBar.Visibility = Visibility.Collapsed;
                        ThumbBar.Opacity = 0;
                    }
                    if (!_chromeVisible)
                    {
                        if (!Vm.HomeVisible) ImageTopBar.Visibility = Visibility.Collapsed;
                        StatusBar.Visibility = Visibility.Collapsed;
                    }
                };
                sb.Begin();
            }
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

        private void ThumbToggle_Click(object sender, RoutedEventArgs e)
        {
            ToggleThumbStrip();
            UpdateThumbToggleState();
        }

        private void UpdateThumbToggleState()
        {
            var on = _thumbStripEnabled;
            ThumbToggleBtn.Background = on
                ? (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"]
                : new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Transparent);
            ThumbToggleBtn.Foreground = on
                ? new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White)
                : (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["TextMutedBrush"];
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(ThumbToggleBtn, on ? "隐藏缩略图" : "显示缩略图");            ToolTipService.SetToolTip(ThumbToggleBtn, on ? "隐藏缩略图" : "显示缩略图");
        }

        // ====== 中央导航箭头 ======

        private void ImageArea_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!Vm.HasImage) return;
            // 顶部悬停唤出标题栏 (Photos 行为)
            if (!_chromeVisible && e.GetCurrentPoint(ImageArea).Position.Y <= 3)
            {
                SetChrome(true);
                return;
            }
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

        protected override async void OnNavigatedTo(NavigationEventArgs e)
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
            else if (e.NavigationMode == NavigationMode.New
                     && SettingsService.RestoreLastFolder
                     && !string.IsNullOrEmpty(SettingsService.LastFolderPath))
            {
                // 启动恢复上次浏览的文件夹 (仅限全新导航, 返回时不触发)
                var path = SettingsService.LastFolderPath;
                var folder = RecentFoldersService.Instance.Folders
                    .FirstOrDefault(f => string.Equals(f?.Path, path, StringComparison.OrdinalIgnoreCase));
                if (folder != null)
                {
                    await Vm.LoadFolderAsync(folder);
                }
            }
            // 全屏时按 Esc 退出
            Window.Current.CoreWindow.Dispatcher.AcceleratorKeyActivated += CoreWindow_AcceleratorKeyActivated;
            // 从设置/编辑/时间线页返回时恢复标题栏扩展
            SetupTitleBar();
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

        private void BackToLibrary_Click(object sender, RoutedEventArgs e)
        {
            Vm.GoHome();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Vm.StopSlideShow();
            Frame.Navigate(typeof(SettingsPage));
        }

        private void Retry_Click(object sender, RoutedEventArgs e)
        {
            Vm.RetryLoad();
        }

        private void TimelineButton_Click(object sender, RoutedEventArgs e)
        {
            if (Vm.CurrentFolder == null) return;
            Vm.StopSlideShow();
            Frame.Navigate(typeof(TimelinePage), Vm.CurrentFolder);
        }

        // ====== 分享 / 导出 / 打印 ======

        private void Share_Click(object sender, RoutedEventArgs e)
        {
            if (Vm.Current?.File is StorageFile file)
            {
                ShareService.Share(file, Vm.Current.Name);
            }
        }

        private async void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            if (Vm.Current?.File is StorageFile file)
            {
                var target = await ExportService.SaveAsAsync(file);
                if (target != null)
                {
                    await new ContentDialog
                    {
                        Title = Loc.Get("ExportTitle"),
                        Content = Loc.Format("ExportDone", target.Name),
                        CloseButtonText = Loc.Get("DialogOK")
                    }.ShowAsync();
                }
            }
        }

        private async void Print_Click(object sender, RoutedEventArgs e)
        {
            if (Vm.Current?.File is StorageFile file)
            {
                await PrintService.PrintAsync(file);
            }
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
            // Ctrl 组合键固定不可自定义
            var ctrlDown = (Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
            if (ctrlDown)
            {
                switch (key)
                {
                    case VirtualKey.C:
                        Vm.CopyCurrentToClipboard();
                        return true;
                    case VirtualKey.F:
                        if (SearchBox != null)
                        {
                            SearchBox.Focus(FocusState.Programmatic);
                            return true;
                        }
                        return false;
                    case VirtualKey.O:
                        Vm.OpenImageCommand.Execute(null);
                        return true;
                    case VirtualKey.R:
                        Vm.RotateBackCommand.Execute(null);
                        return true;
                    default:
                        return false;
                }
            }

            // 自定义单键动作 (含默认绑定)
            var action = KeyboardService.ActionForKey(key);
            if (action != null)
            {
                return ExecuteAction(action);
            }

            switch (key)
            {
                case VirtualKey.Space:
                    Vm.Next();
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
                default:
                    return false;
            }
        }

        /// <summary>执行自定义快捷键动作 (返回是否处理)。</summary>
        private bool ExecuteAction(string action)
        {
            switch (action)
            {
                case "Prev":
                    Vm.Prev();
                    return true;
                case "Next":
                    Vm.Next();
                    return true;
                case "First":
                    Vm.First();
                    return true;
                case "Last":
                    Vm.Last();
                    return true;
                case "Rotate":
                    Vm.RotateCommand.Execute(null);
                    return true;
                case "FlipH":
                    Vm.FlipHCommand.Execute(null);
                    return true;
                case "FlipV":
                    Vm.FlipVCommand.Execute(null);
                    return true;
                case "ResetZoom":
                    Vm.ResetTransformCommand.Execute(null);
                    Viewer.ResetView();
                    return true;
                case "ZoomIn":
                    ZoomByKeys(1.2f);
                    return true;
                case "ZoomOut":
                    ZoomByKeys(1f / 1.2f);
                    return true;
                case "ToggleChrome":
                    ToggleChrome();
                    return true;
                case "SlideShow":
                    Vm.ToggleSlideShow();
                    return true;
                case "FullScreen":
                    ToggleFullScreen();
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
            if (sender == AllPhotosGrid && _selectMode)
            {
                UpdateSelectCount();
                return;
            }
            if (e.ClickedItem is PhotoItem photo)
            {
                _ = Vm.OpenPhotoFromLibraryAsync(photo);
            }
        }

        // ====== 批量选择 ======

        private bool _selectMode;

        private void SelectModeBtn_Click(object sender, RoutedEventArgs e)
        {
            _selectMode = !_selectMode;
            AllPhotosGrid.SelectionMode = _selectMode ? ListViewSelectionMode.Multiple : ListViewSelectionMode.None;
            AllPhotosGrid.SelectedItems.Clear();
            SelectBar.Visibility = _selectMode ? Visibility.Visible : Visibility.Collapsed;
            SelectModeBtnText.Text = Loc.Get(_selectMode ? "SelectModeOff" : "SelectModeOn");
            UpdateSelectCount();
        }

        private void AllPhotosGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_selectMode) UpdateSelectCount();
        }

        private void UpdateSelectCount()
        {
            int count = AllPhotosGrid.SelectedItems.Count;
            SelectCountText.Text = Loc.Format("SelectCount", count);
            SelectFavBtn.IsEnabled = count > 0;
            SelectShareBtn.IsEnabled = count > 0;
            SelectDeleteBtn.IsEnabled = count > 0;
        }

        private void SelectCancel_Click(object sender, RoutedEventArgs e)
        {
            AllPhotosGrid.SelectedItems.Clear();
            SelectModeBtn_Click(sender, e);
        }

        private void SelectFav_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in AllPhotosGrid.SelectedItems.Cast<PhotoItem>())
            {
                FavoritesService.Add(item.Path);
                if (Vm.Favorites.All(f => !string.Equals(f.Path, item.Path, StringComparison.OrdinalIgnoreCase)))
                    Vm.Favorites.Add(item);
            }
            AllPhotosGrid.SelectedItems.Clear();
            UpdateSelectCount();
        }

        private async void SelectShare_Click(object sender, RoutedEventArgs e)
        {
            var files = new List<StorageFile>();
            foreach (var item in AllPhotosGrid.SelectedItems.Cast<PhotoItem>())
            {
                try { files.Add(await StorageFile.GetFileFromPathAsync(item.Path)); } catch { }
            }
            if (files.Count > 0)
            {
                ShareService.ShareFiles(files, Loc.Format("ShareBatchTitle", files.Count));
            }
        }

        private async void SelectDelete_Click(object sender, RoutedEventArgs e)
        {
            int count = AllPhotosGrid.SelectedItems.Count;
            if (count == 0) return;
            var paths = AllPhotosGrid.SelectedItems.Cast<PhotoItem>().Select(p => p.Path).ToList();
            var dlg = new ContentDialog
            {
                Title = Loc.Get("DeleteTitle"),
                Content = Loc.Format("BatchDeleteMessage", count),
                PrimaryButtonText = Loc.Get("DeletePrimary"),
                CloseButtonText = Loc.Get("DialogCancel")
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            var failed = await Vm.BatchDeleteAsync(paths);
            AllPhotosGrid.SelectedItems.Clear();
            if (failed.Count > 0)
            {
                var err = new ContentDialog
                {
                    Title = Loc.Get("DeleteFailTitle"),
                    Content = Loc.Format("BatchDeleteFailMessage", failed.Count),
                    CloseButtonText = Loc.Get("DialogOK")
                };
                await err.ShowAsync();
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

        private void FavoritesGrid_RightTapped(object sender, RightTappedRoutedEventArgs e)
            => AllPhotosGrid_RightTapped(sender, e);

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
