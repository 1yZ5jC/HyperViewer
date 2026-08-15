using HyperViewer.Helpers;
using HyperViewer.Models;
using HyperViewer.ViewModels;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Navigation;

namespace HyperViewer
{
    /// <summary>
    /// 时间轴页: 按拍摄日期分组浏览图片, 日历快速定位, 点击图片跳回主页。
    /// </summary>
    public sealed partial class TimelinePage : Page
    {
        public TimelineViewModel Vm { get; } = new TimelineViewModel();

        private StorageFolder _currentFolder;

        public TimelinePage()
        {
            this.InitializeComponent();
            this.DataContext = Vm;
            Vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TimelineViewModel.IsScanning))
                    UpdateEmptyHint();
            };
            // 窗口最小宽度下日历列可能过窄, 这里做简单保护
            UpdateEmptyHint();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _currentFolder = e.Parameter as StorageFolder;
            TitleText.Text = _currentFolder?.Name ?? Loc.Get("TimelineTitle");
            if (_currentFolder != null)
            {
                _ = Vm.LoadAsync(_currentFolder);
            }
        }

        private void UpdateEmptyHint()
        {
            EmptyHint.Visibility = (!Vm.IsScanning && Vm.Groups.Count == 0)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void Level_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is ToggleButton tb)
                || !(tb.Tag is string s)
                || !int.TryParse(s, out int level))
            {
                return;
            }
            DayBtn.IsChecked = level == 0;
            MonthBtn.IsChecked = level == 1;
            YearBtn.IsChecked = level == 2;
            Vm.SetLevel(level);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }

        private void Cal_SelectedDatesChanged(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
        {
            if (args.AddedDates.Count == 0) return;
            var group = Vm.FindGroupForDate(args.AddedDates[0]);
            if (group == null || !TimelineList.Items.Contains(group)) return;
            TimelineList.ScrollIntoView(group);
            TimelineList.UpdateLayout();
            if (TimelineList.ContainerFromItem(group) is FrameworkElement fe
                && Helpers.UwpCompat.HasContractV2)
            {
                // BringIntoViewOptions 是 14393+ 才有; 10240 上方的 ScrollIntoView 已生效
                fe.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true });
            }
        }

        private void Photo_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn)
                || !(btn.Tag is PhotoItem photo)
                || photo.File == null
                || _currentFolder == null)
            {
                return;
            }
            Frame.Navigate(typeof(MainPage), new TimelineRequest(_currentFolder, photo.File));
        }
    }
}