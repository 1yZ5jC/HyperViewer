using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HyperViewer.Helpers;

namespace HyperViewer.Models
{
    /// <summary>
    /// 时间轴分组: 同一天的多张照片 + 分组标题。
    /// </summary>
    public sealed class TimelineGroup : ObservableCollection<PhotoItem>
    {
        public DateTimeOffset Date { get; }

        public string Title { get; }

        public TimelineGroup(DateTimeOffset date, IEnumerable<PhotoItem> items)
            : base(items)
        {
            Date = date.Date;
            Title = Loc.Format("TimelineDateTitle", date.Year, date.Month, date.Day);
        }
    }
}