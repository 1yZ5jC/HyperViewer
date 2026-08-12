using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace HyperViewer.Helpers
{
    /// <summary>
    /// bool true->Collapsed, false->Visible. Inverse of BoolToVisibilityConverter.
    ///</summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => (value is Visibility v && v == Visibility.Collapsed);
    }
}
