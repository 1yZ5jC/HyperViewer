using System;
using System.Linq;
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
    }
}
