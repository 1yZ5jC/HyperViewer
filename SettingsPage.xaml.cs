using HyperViewer.Helpers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyperViewer
{
    /// <summary>
    /// 设置页: 幻灯片间隔 + 主视图背景 (LocalSettings 即时生效)。
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();
            this.Loaded += (_, __) => LoadCurrentValues();
        }

        private void LoadCurrentValues()
        {
            string theme = SettingsService.AppTheme;
            for (int i = 0; i < ThemeBox.Items.Count; i++)
            {
                if (ThemeBox.Items[i] is ComboBoxItem item
                    && (string)item.Tag == theme)
                {
                    ThemeBox.SelectedIndex = i;
                    break;
                }
            }

            int seconds = SettingsService.SlideShowSeconds;
            for (int i = 0; i < SlideSecondsBox.Items.Count; i++)
            {
                if (SlideSecondsBox.Items[i] is ComboBoxItem item
                    && item.Tag?.ToString() == seconds.ToString())
                {
                    SlideSecondsBox.SelectedIndex = i;
                    break;
                }
            }

            string transition = SettingsService.SlideTransition;
            for (int i = 0; i < TransitionBox.Items.Count; i++)
            {
                if (TransitionBox.Items[i] is ComboBoxItem item
                    && (string)item.Tag == transition)
                {
                    TransitionBox.SelectedIndex = i;
                    break;
                }
            }

            string bg = SettingsService.MainBackground;
            for (int i = 0; i < BackgroundBox.Items.Count; i++)
            {
                if (BackgroundBox.Items[i] is ComboBoxItem item
                    && (string)item.Tag == bg)
                {
                    BackgroundBox.SelectedIndex = i;
                    break;
                }
            }

            ResetRotationSwitch.IsOn = SettingsService.ResetRotationOnNavigate;
        }

        private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeBox.SelectedItem is ComboBoxItem item && item.Tag is string theme)
            {
                SettingsService.AppTheme = theme;
                App.ApplyThemeNow();
            }
        }

        private void SlideSecondsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SlideSecondsBox.SelectedItem is ComboBoxItem item
                && item.Tag != null
                && int.TryParse(item.Tag.ToString(), out int seconds))
            {
                SettingsService.SlideShowSeconds = seconds;
            }
        }

        private void BackgroundBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BackgroundBox.SelectedItem is ComboBoxItem item && item.Tag is string name)
            {
                SettingsService.MainBackground = name;
            }
        }

        private void TransitionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TransitionBox.SelectedItem is ComboBoxItem item && item.Tag is string transition)
            {
                SettingsService.SlideTransition = transition;
            }
        }

        private void ResetRotationSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            SettingsService.ResetRotationOnNavigate = ResetRotationSwitch.IsOn;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}