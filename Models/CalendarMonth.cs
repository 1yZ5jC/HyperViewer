using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using HyperViewer.Helpers;

namespace HyperViewer.Models
{
    /// <summary>
    /// 日历热力图: 某一天的照片数量与色阶 (0-4 级)。
    /// </summary>
    public sealed class CalendarDay
    {
        public DateTimeOffset Date { get; }

        public int DayNumber { get; }

        public string DayNumberText => IsPlaceholder || DayNumber <= 0 ? string.Empty : DayNumber.ToString();

        public int Count { get; }

        public bool Empty => Count == 0;

        public Brush Fill { get; }

        public Brush TextBrush { get; }

        public CalendarDay(DateTimeOffset date, int count, int maxCount, bool placeholder = false)
        {
            Date = date;
            DayNumber = date.Day;
            Count = count;
            IsPlaceholder = placeholder;
            if (count <= 0)
            {
                Level = 0;
            }
            else
            {
                Level = Math.Min(4, 1 + (int)Math.Round(3.0 * count / Math.Max(1, maxCount)));
            }
            Fill = LevelBrush(Level);
            TextBrush = Level >= 3 ? WhiteBrush : TextBrushDefault;
        }

        public int Level { get; }

        public bool IsPlaceholder { get; }

        private static readonly Brush WhiteBrush = new SolidColorBrush(Colors.White);

        private static Brush _textDefault;
        private static Brush TextBrushDefault
        {
            get
            {
                if (_textDefault == null)
                {
                    try { _textDefault = (Brush)Application.Current.Resources["TextPrimaryBrush"]; }
                    catch { _textDefault = new SolidColorBrush(Colors.Gray); }
                }
                return _textDefault;
            }
        }

        private static Color AccentColor()
        {
            try
            {
                if (Application.Current.Resources["AccentBrush"] is SolidColorBrush b) return b.Color;
            }
            catch { }
            return Colors.DeepSkyBlue;
        }

        private static Brush LevelBrush(int level)
        {
            var c = AccentColor();
            byte alpha = level == 0 ? (byte)28 : (byte)(50 + level * 50);
            return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }
    }

    /// <summary>
    /// 日历热力图的一个月块: 星期头 + 天数格子 (7 列)。
    /// </summary>
    public sealed class CalendarMonth
    {
        public string Title { get; }

        public int FirstWeekday { get; }

        public int DayCount { get; }

        public IReadOnlyList<string> Weekdays { get; }

        public ObservableCollection<CalendarDay> Days { get; } = new ObservableCollection<CalendarDay>();

        public CalendarMonth(int year, int month, IReadOnlyDictionary<DateTime, int> counts)
        {
            Title = Loc.Format("TimelineMonthTitle", year, month);
            var first = new DateTime(year, month, 1);
            FirstWeekday = (int)first.DayOfWeek; // 0=星期日
            DayCount = DateTime.DaysInMonth(year, month);
            int max = 0;
            for (int d = 1; d <= DayCount; d++)
            {
                var date = new DateTime(year, month, d);
                if (counts.TryGetValue(date, out var c)) max = Math.Max(max, c);
            }
            for (int d = 1; d <= DayCount; d++)
            {
                var date = new DateTime(year, month, d);
                counts.TryGetValue(date, out var c);
                Days.Add(new CalendarDay(date, c, max));
            }
            Weekdays = Loc.Get("CalendarWeekdays").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            // 月初之前的空位占位 (不可点击): 使用上月合法日期, 避免 day=0 非法 DateTime
            for (int i = 0; i < FirstWeekday; i++)
            {
                Days.Insert(0, new CalendarDay(first.AddDays(-i - 1), 0, 1, placeholder: true));
            }
        }
    }
}
