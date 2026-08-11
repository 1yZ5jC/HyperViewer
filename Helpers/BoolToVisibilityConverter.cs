using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace HyperViewer.Helpers
{
    /// <summary>
    /// bool true->Visible, false->Collapsed.
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => (value is Visibility v && v == Visibility.Visible);
    }
}
