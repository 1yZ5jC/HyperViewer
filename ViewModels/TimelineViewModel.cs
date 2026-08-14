using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HyperViewer.Helpers;
using HyperViewer.Models;
using HyperViewer.Services;
using Windows.Storage;

namespace HyperViewer.ViewModels
{
    /// <summary>
    /// 时间轴 ViewModel: 扫描文件夹拍摄日期, 按天分组, 供日历快速定位。
    /// </summary>
    public sealed class TimelineViewModel : ObservableObject
    {
        private readonly ObservableCollection<TimelineGroup> _groups = new ObservableCollection<TimelineGroup>();
        public ReadOnlyObservableCollection<TimelineGroup> Groups { get; }

        private bool _isScanning;
        public bool IsScanning
        {
            get => _isScanning;
            private set => SetProperty(ref _isScanning, value);
        }

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        private DateTimeOffset _calendarMin = new DateTimeOffset(new DateTime(2000, 1, 1));
        public DateTimeOffset CalendarMin
        {
            get => _calendarMin;
            private set => SetProperty(ref _calendarMin, value);
        }

        private DateTimeOffset _calendarMax = DateTimeOffset.Now;
        public DateTimeOffset CalendarMax
        {
            get => _calendarMax;
            private set => SetProperty(ref _calendarMax, value);
        }

        public TimelineViewModel()
        {
            Groups = new ReadOnlyObservableCollection<TimelineGroup>(_groups);
        }

        private int _level;
        /// <summary>0=按日, 1=按月, 2=按年。</summary>
        public int Level
        {
            get => _level;
            private set => SetProperty(ref _level, value);
        }

        /// <summary>切换分组层级并重建分组。</summary>
        public void SetLevel(int level)
        {
            if (level < 0 || level > 2 || level == Level || _photos == null) return;
            Level = level;
            RebuildGroups();
        }

        public async Task LoadAsync(StorageFolder folder)
        {
            _groups.Clear();
            if (folder == null)
            {
                StatusText = Loc.Get("TimelineNoFolder");
                return;
            }

            var photos = await FilePickerService.EnumerateImagesAsync(folder);
            if (photos.Count == 0)
            {
                StatusText = Loc.Get("TimelineNoImages");
                return;
            }

            IsScanning = true;
            int scanned = 0;
            var progress = new Progress<int>(n => StatusText = Loc.Format("TimelineScanning", scanned += n, photos.Count));
            using (var sem = new SemaphoreSlim(4))
            {
                var tasks = new List<Task>();
                foreach (var p in photos)
                    tasks.Add(LoadDateAsync(sem, p, progress));
                await Task.WhenAll(tasks);
            }
            IsScanning = false;
            _photos = new List<PhotoItem>(photos);
            RebuildGroups();

            if (_groups.Count > 0)
            {
                CalendarMin = _groups[_groups.Count - 1].Date;
                CalendarMax = _groups[0].Date;
            }
            else
            {
                StatusText = Loc.Get("TimelineNoDate");
            }

            // 无日期照片兜底展示: 归入最早一组之前的日子? 直接忽略, 不影响主流程
            await ImageLoaderService.PreloadThumbnailsAsync(photos);
        }

        private List<PhotoItem> _photos;

        private void RebuildGroups()
        {
            _groups.Clear();
            var dated = _photos.Where(p => p.DateTaken.HasValue).ToList();
            if (dated.Count == 0)
            {
                StatusText = Loc.Get("TimelineNoDate");
                return;
            }

            IEnumerable<IGrouping<DateTime, PhotoItem>> grouped;
            if (Level == 2)
                grouped = dated.GroupBy(p => new DateTime(p.DateTaken.Value.Year, 1, 1));
            else if (Level == 1)
                grouped = dated.GroupBy(p => new DateTime(p.DateTaken.Value.Year, p.DateTaken.Value.Month, 1));
            else
                grouped = dated.GroupBy(p => p.DateTaken.Value.Date);

            foreach (var g in grouped.OrderByDescending(g => g.Key))
            {
                _groups.Add(new TimelineGroup(g.Key, g.OrderByDescending(p => p.DateTaken.Value), Level));
            }
            StatusText = Loc.Format("TimelineDone", _photos.Count, _groups.Count);
        }

        private static async Task LoadDateAsync(SemaphoreSlim sem, PhotoItem p, IProgress<int> progress)
        {
            await sem.WaitAsync();
            try
            {
                p.DateTaken = await ImageInfoService.GetDateTakenAsync(p.File);
            }
            finally
            {
                sem.Release();
                progress.Report(1);
            }
        }

        /// <summary>
        /// 找到某个日期所在分组; 无该分组则回退到最近一个更早的分组 (按当前层级匹配)。
        /// </summary>
        public TimelineGroup FindGroupForDate(DateTimeOffset date)
        {
            var day = date.Date;
            DateTime key = Level == 2 ? new DateTime(day.Year, 1, 1)
                : Level == 1 ? new DateTime(day.Year, day.Month, 1)
                : day;
            return _groups.FirstOrDefault(g => g.Date == key)
                ?? _groups.FirstOrDefault(g => g.Date < key);
        }
    }
}