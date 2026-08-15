using System;
using HyperViewer.Helpers;
using HyperViewer.Services;
using Windows.ApplicationModel.Core;
using Windows.Globalization;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace HyperViewer
{
    /// <summary>
    /// 设置页: 主题 / 启动行为 / 语言 / 幻灯片 / 背景 / 缓存清理 / 快捷键自定义。
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        private bool _loading;

        public SettingsPage()
        {
            this.InitializeComponent();
            this.Loaded += (_, __) => LoadCurrentValues();
            this.Loaded += (_, __) => ApplyDragRegion();
        }

        /// <summary>设置页顶栏拖拽区作为标题栏 (按钮等交互控件在拖拽区外, 见 XAML)。</summary>
        private void ApplyDragRegion()
        {
            try { Window.Current.SetTitleBar(TitleBarDragRegion); }
            catch { }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            try { Window.Current.SetTitleBar(null); }
            catch { }
            base.OnNavigatedFrom(e);
        }

        private void LoadCurrentValues()
        {
            _loading = true;

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

            RestoreFolderSwitch.IsOn = SettingsService.RestoreLastFolder;

            string lang = ApplicationLanguages.PrimaryLanguageOverride;
            bool langFound = false;
            for (int i = 0; i < LanguageBox.Items.Count; i++)
            {
                if (LanguageBox.Items[i] is ComboBoxItem item
                    && string.Equals((string)item.Tag, lang, StringComparison.OrdinalIgnoreCase))
                {
                    LanguageBox.SelectedIndex = i;
                    langFound = true;
                    break;
                }
            }
            // 跟随系统 (空覆盖) 时选中第一项
            if (!langFound && string.IsNullOrEmpty(lang) && LanguageBox.Items.Count > 0)
            {
                LanguageBox.SelectedIndex = 0;
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
            Sim10240Switch.IsOn = SettingsService.DebugSimulate10240;
            VerboseLogSwitch.IsOn = SettingsService.DebugVerboseLog;
            SlideRandomOrderSwitch.IsOn = SettingsService.SlideRandomOrder;
            SlideRandomTransitionSwitch.IsOn = SettingsService.SlideRandomTransition;
            SlideBlurSwitch.IsOn = SettingsService.SlideBlurBackground;
            LiveTileSwitch.IsOn = SettingsService.LiveTileEnabled;
            TileSingleSwitch.IsOn = SettingsService.TileSingleImage;
            TileRotationSwitch.IsOn = SettingsService.TileRotationEnabled;
            int rotSeconds = SettingsService.TileRotationSeconds;
            for (int i = 0; i < TileRotationBox.Items.Count; i++)
            {
                if (TileRotationBox.Items[i] is ComboBoxItem item
                    && item.Tag?.ToString() == rotSeconds.ToString())
                {
                    TileRotationBox.SelectedIndex = i;
                    break;
                }
            }
            CacheInfoText.Text = Loc.Format("CacheInfo", ImageLoaderService.CacheCount);
            BuildShortcutPanel();

            _loading = false;
        }

        // ====== 主题 ======

        private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (ThemeBox.SelectedItem is ComboBoxItem item && item.Tag is string theme)
            {
                SettingsService.AppTheme = theme;
                App.ApplyThemeNow();
            }
        }

        // ====== 启动行为 ======

        private void RestoreFolderSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SettingsService.RestoreLastFolder = RestoreFolderSwitch.IsOn;
        }

        // ====== 语言 (需重启生效) ======

        private async void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (LanguageBox.SelectedItem is ComboBoxItem item && item.Tag is string lang)
            {
                ApplicationLanguages.PrimaryLanguageOverride = lang == "System" ? string.Empty : lang;
                var dlg = new CompatContentDialog
                {
                    Title = Loc.Get("RestartTitle"),
                    Content = Loc.Get("RestartMessage"),
                    PrimaryButtonText = Loc.Get("RestartNow"),
                    CompatCloseButtonText = Loc.Get("RestartLater")
                };
                if (await dlg.ShowAsync() == ContentDialogResult.Primary)
                {
                    // RequestRestartAsync 需 1709 (UniversalApiContract v5) 及以上
                    if (Helpers.UwpCompat.HasContractV5)
                    {
                        try
                        {
                            await CoreApplication.RequestRestartAsync(string.Empty);
                        }
                        catch
                        {
                            // 重启失败则下次启动生效
                        }
                    }
                    else
                    {
                        await new CompatContentDialog
                        {
                            Title = Loc.Get("RestartTitle"),
                            Content = Loc.Get("RestartMessage"),
                            CompatCloseButtonText = Loc.Get("RestartLater")
                        }.ShowAsync();
                    }
                }
            }
        }

        // ====== 幻灯片 / 背景 (原有) ======

        private void SlideSecondsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (SlideSecondsBox.SelectedItem is ComboBoxItem item
                && item.Tag != null
                && int.TryParse(item.Tag.ToString(), out int seconds))
            {
                SettingsService.SlideShowSeconds = seconds;
            }
        }

        private void BackgroundBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (BackgroundBox.SelectedItem is ComboBoxItem item && item.Tag is string name)
            {
                SettingsService.MainBackground = name;
            }
        }

        private void TransitionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (TransitionBox.SelectedItem is ComboBoxItem item && item.Tag is string transition)
            {
                SettingsService.SlideTransition = transition;
            }
        }

        private void SlideRandomOrderSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SettingsService.SlideRandomOrder = SlideRandomOrderSwitch.IsOn;
        }

        private void SlideRandomTransitionSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SettingsService.SlideRandomTransition = SlideRandomTransitionSwitch.IsOn;
        }

        private void SlideBlurSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SettingsService.SlideBlurBackground = SlideBlurSwitch.IsOn;
        }

        private void LiveTileSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SettingsService.LiveTileEnabled = LiveTileSwitch.IsOn;
        }

        private void TileSingleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SettingsService.TileSingleImage = TileSingleSwitch.IsOn;
            TileRotationService.Restart();
        }

        private void TileRotationSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SettingsService.TileRotationEnabled = TileRotationSwitch.IsOn;
            TileRotationService.Restart();
        }

        private void TileRotationBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (TileRotationBox.SelectedItem is ComboBoxItem item && item.Tag is string seconds)
            {
                SettingsService.TileRotationSeconds = int.Parse(seconds);
                TileRotationService.Restart();
            }
        }

        private void ResetRotationSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SettingsService.ResetRotationOnNavigate = ResetRotationSwitch.IsOn;
        }

        // ====== 开发者选项 ======

        private void Sim10240Switch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SettingsService.DebugSimulate10240 = Sim10240Switch.IsOn;
        }

        private void VerboseLogSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SettingsService.DebugVerboseLog = VerboseLogSwitch.IsOn;
        }

        // ====== 缓存与数据 ======

        private async void ClearCache_Click(object sender, RoutedEventArgs e)
        {
            int cleared = ImageLoaderService.ClearCache();
            CacheInfoText.Text = Loc.Format("CacheInfo", ImageLoaderService.CacheCount);
            await new CompatContentDialog
            {
                Title = Loc.Get("ClearCacheTitle"),
                Content = Loc.Format("CacheCleared", cleared),
                CompatCloseButtonText = Loc.Get("DialogOK")
            }.ShowAsync();
        }

        private async void ClearRecent_Click(object sender, RoutedEventArgs e)
        {
            await RecentFoldersService.Instance.ClearAsync();
            await new CompatContentDialog
            {
                Title = Loc.Get("ClearRecentTitle"),
                Content = Loc.Get("RecentCleared"),
                CompatCloseButtonText = Loc.Get("DialogOK")
            }.ShowAsync();
        }

        // ====== 快捷键自定义 ======

        private void BuildShortcutPanel()
        {
            ShortcutPanel.Children.Clear();
            foreach (var action in KeyboardService.Actions)
            {
                var row = new Grid
                {
                    Margin = new Thickness(0, 0, 0, 8)
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });

                var name = new TextBlock
                {
                    Text = Loc.Get("Shortcut" + action),
                    Style = (Style)Resources["ShortcutRowNameStyle"]
                };
                Grid.SetColumn(name, 0);
                row.Children.Add(name);

                var combo = new ComboBox
                {
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(combo, 1);
                foreach (var key in KeyboardService.CandidateKeys)
                {
                    combo.Items.Add(new ComboBoxItem
                    {
                        Content = KeyDisplay(key),
                        Tag = key
                    });
                }
                combo.SelectionChanged += (_, __) =>
                {
                    if (combo.SelectedItem is ComboBoxItem item && item.Tag is VirtualKey k)
                    {
                        KeyboardService.SetKey(action, k);
                    }
                };
                var current = KeyboardService.GetKey(action);
                for (int i = 0; i < combo.Items.Count; i++)
                {
                    if ((VirtualKey)((ComboBoxItem)combo.Items[i]).Tag == current)
                    {
                        combo.SelectedIndex = i;
                        break;
                    }
                }
                row.Children.Add(combo);
                ShortcutPanel.Children.Add(row);
            }
        }

        private void ShortcutReset_Click(object sender, RoutedEventArgs e)
        {
            KeyboardService.ResetAll();
            BuildShortcutPanel();
        }

        /// <summary>按键显示名 (本地化, 无映射时回退枚举名)。</summary>
        private static string KeyDisplay(VirtualKey key)
        {
            string locKey;
            switch (key)
            {
                case VirtualKey.Left: locKey = "KeyLeft"; break;
                case VirtualKey.Right: locKey = "KeyRight"; break;
                case VirtualKey.Up: locKey = "KeyUp"; break;
                case VirtualKey.Down: locKey = "KeyDown"; break;
                case VirtualKey.Space: locKey = "KeySpace"; break;
                case VirtualKey.Home: locKey = "KeyHome"; break;
                case VirtualKey.End: locKey = "KeyEnd"; break;
                case VirtualKey.Number0: locKey = "KeyNumber0"; break;
                case VirtualKey.Number1: locKey = "KeyNumber1"; break;
                case VirtualKey.Number2: locKey = "KeyNumber2"; break;
                case VirtualKey.Add: locKey = "KeyAdd"; break;
                case VirtualKey.Subtract: locKey = "KeySubtract"; break;
                case VirtualKey.F5: locKey = "KeyF5"; break;
                case VirtualKey.F8: locKey = "KeyF8"; break;
                case VirtualKey.F11: locKey = "KeyF11"; break;
                default: return key.ToString();
            }
            var s = Loc.Get(locKey);
            return string.IsNullOrEmpty(s) ? key.ToString() : s;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
