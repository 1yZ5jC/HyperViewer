using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace HyperViewer
{
    /// <summary>
    /// 提供特定于应用程序的行为，以补充默认的应用程序类。
    /// </summary>
    sealed partial class App : Application
    {
        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
        }

        /// <summary>
        /// 在应用程序由最终用户正常启动时调用。
        /// </summary>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            ApplyTheme();
            var rootFrame = EnsureRootFrame();
            if (rootFrame.Content == null)
            {
                rootFrame.Navigate(typeof(MainPage), e.Arguments);
            }
            Window.Current.Activate();
        }

        /// <summary>
        /// 用户通过文件关联激活本应用时调用 (双击图片 / OpenWith)。
        /// </summary>
        protected override void OnFileActivated(FileActivatedEventArgs e)
        {
            base.OnFileActivated(e);
            ApplyTheme();
            var rootFrame = EnsureRootFrame();
            StorageFile file = e.Files != null && e.Files.Count > 0 ? e.Files[0] as StorageFile : null;
            rootFrame.Navigate(typeof(MainPage), file);
            Window.Current.Activate();
        }

        private void ApplyTheme()
        {
            ApplyThemeNow();
        }

        /// <summary>
        /// 应用主题: 优先 Application 级 (打包环境), 再设置根 Frame 级 (稳定,
        /// 影响子树 ThemeResource 解析, ElementTheme.Default 可还原跟随系统)。
        /// </summary>
        public static void ApplyThemeNow()
        {
            var theme = Helpers.SettingsService.AppTheme;
            try
            {
                var appTarget = theme == "Light" ? ApplicationTheme.Light
                              : theme == "Dark" ? ApplicationTheme.Dark
                              : (ApplicationTheme)(-1);
                if (appTarget != (ApplicationTheme)(-1)
                    && Application.Current.RequestedTheme != appTarget)
                {
                    Application.Current.RequestedTheme = appTarget;
                }
            }
            catch
            {
                // 部分运行环境不支持 Application 级切换, 忽略
            }

            if (Window.Current?.Content is Frame frame)
            {
                frame.RequestedTheme = theme == "Light" ? ElementTheme.Light
                                     : theme == "Dark" ? ElementTheme.Dark
                                     : ElementTheme.Default;
            }
        }

        private Frame EnsureRootFrame()
        {
            Frame rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                Window.Current.Content = rootFrame;
            }
            return rootFrame;
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            deferral.Complete();
        }
    }
}
