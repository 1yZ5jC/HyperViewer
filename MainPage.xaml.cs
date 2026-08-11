using System;
using System.Linq;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using HyperViewer.ViewModels;

namespace HyperViewer
{
    /// <summary>
    /// 主页：图片浏览 + 工具栏 + 缩略图栏 + 状态栏。
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainViewModel Vm { get; } = new MainViewModel();

        public MainPage()
        {
            this.InitializeComponent();
            this.DataContext = Vm;
            UpdateEmptyHint();
            Vm.PropertyChanged += (_, __) => UpdateEmptyHint();
            Vm.PropertyChanged += OnVmPropertyChanged;
        }

        private void OnVmPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.CurrentIndex):
                    SyncThumbnailSelection();
                    break;
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            this.Focus(FocusState.Programmatic);
            if (e.Parameter is StorageFile file)
            {
                Vm.ActivateFromFile(file);
            }
            // 全屏时按 Esc 退出
            Window.Current.CoreWindow.Dispatcher.AcceleratorKeyActivated += CoreWindow_AcceleratorKeyActivated;
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

        private void UpdateEmptyHint()
        {
            EmptyHint.Visibility = Vm.HasImage ? Visibility.Collapsed : Visibility.Visible;
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
            }
            else
            {
                view.TryEnterFullScreenMode();
            }
        }

        private async void RecentButton_Click(object sender, RoutedEventArgs e)
        {
            var recent = Vm.RecentFolders;
            if (recent == null || recent.Count == 0)
            {
                var dlg = new ContentDialog
                {
                    Title = "最近打开",
                    Content = "暂无历史记录。",
                    CloseButtonText = "确定"
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
                Title = "最近打开的文件夹",
                Content = panel,
                CloseButtonText = "取消"
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
    }
}
