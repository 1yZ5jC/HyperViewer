using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HyperViewer.Helpers;

namespace HyperViewer.Models
{
    /// <summary>
    /// 全部照片按日期分组 (日期流视图), 组内保持图库原始顺序。
    /// </summary>
    public sealed class PhotoDayGroup : ObservableCollection<PhotoItem>
    {
        public DateTimeOffset Day { get; }

        public string Title { get; }

        public string Subtitle { get; }

        public PhotoDayGroup(DateTimeOffset day, IEnumerable<PhotoItem> items)
            : base(items)
        {
            Day = day.Date;
            Title = Loc.Format("TimelineDateTitle", day.Year, day.Month, day.Day);
            Subtitle = Loc.Format("DayFlowCount", Count);
        }
    }
}
