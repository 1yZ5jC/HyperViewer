using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;

namespace HyperViewer.Helpers
{
    /// <summary>
    /// 根据 bool 切换两个 Brush。parameter="Bg" 选中=AccentBrush, 未选=Transparent。
    /// parameter="Fg" 选中=White, 未选=TextMutedBrush。
    ///</summary>
    public class SelectionBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool selected = value is bool b && b;
            string param = parameter as string;
            if (param == "Bg")
                return selected
                    ? (Brush)Application.Current.Resources["AccentBrush"]
                    : new SolidColorBrush(Colors.Transparent);
            if (param == "Fg")
                return selected
                    ? new SolidColorBrush(Colors.White)
                    : (Brush)Application.Current.Resources["TextMutedBrush"];
            return new SolidColorBrush(Colors.Transparent);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}
