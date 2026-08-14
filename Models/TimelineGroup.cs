using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HyperViewer.Helpers;

namespace HyperViewer.Models
{
    /// <summary>
    /// 时间轴分组: 同一天/月/年(按层级)的多张照片 + 分组标题。
    /// </summary>
    public sealed class TimelineGroup : ObservableCollection<PhotoItem>
    {
        public DateTimeOffset Date { get; }

        /// <summary>0=按日, 1=按月, 2=按年。</summary>
        public int Level { get; }

        public string Title { get; }

        public TimelineGroup(DateTimeOffset date, IEnumerable<PhotoItem> items, int level = 0)
            : base(items)
        {
            Date = date.Date;
            Level = level;
            switch (level)
            {
                case 1:
                    Title = Loc.Format("TimelineMonthTitle", date.Year, date.Month);
                    break;
                case 2:
                    Title = Loc.Format("TimelineYearTitle", date.Year);
                    break;
                default:
                    Title = Loc.Format("TimelineDateTitle", date.Year, date.Month, date.Day);
                    break;
            }
        }
    }
}