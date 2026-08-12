using Windows.Storage;

namespace HyperViewer.Helpers
{
    /// <summary>
    /// 应用设置: LocalSettings 持久化, 强类型访问, 即时生效。
    /// </summary>
    public static class SettingsService
    {
        private const string KeySlideShowSeconds = "SlideShowSeconds";
        private const string KeyBackground = "MainBackground";
        private const string KeyTheme = "AppTheme";
        private const string KeyResetRotation = "ResetRotationOnNavigate";
        private const string KeySlideTransition = "SlideTransition";
        private const string KeyLastTab = "LastTab";

        private static readonly ApplicationDataContainer Store =
            ApplicationData.Current.LocalSettings;

        public static int SlideShowSeconds
        {
            get { return GetInt(KeySlideShowSeconds, 3); }
            set { Store.Values[KeySlideShowSeconds] = value; }
        }

        /// <summary>
        /// 主视图背景: "Black" / "DarkGray" / "White"。
        /// </summary>
        public static string MainBackground
        {
            get { return GetString(KeyBackground, "Black"); }
            set { Store.Values[KeyBackground] = value; }
        }

        /// <summary>
        /// 应用主题: "Default" / "Dark" / "Light"。
        /// </summary>
        public static string AppTheme
        {
            get { return GetString(KeyTheme, "Dark"); }
            set { Store.Values[KeyTheme] = value; }
        }

        /// <summary>
        /// 切换图片时是否重置旋转/翻转 (True=重置为原始方向)。
        /// </summary>
        public static bool ResetRotationOnNavigate
        {
            get { return GetBool(KeyResetRotation, false); }
            set { Store.Values[KeyResetRotation] = value; }
        }

        /// <summary>
        /// 幻灯片/换图过渡: "Fade" / "Zoom" / "Pan" / "Flicker"。
        /// </summary>
        public static string SlideTransition
        {
            get { return GetString(KeySlideTransition, "Fade"); }
            set { Store.Values[KeySlideTransition] = value; }
        }

        // 最近一次打开的图库 Tab（Albums / AllPhotos / Folders）
        public static string LastTab
        {
            get { return GetString(KeyLastTab, "Albums"); }
            set { Store.Values[KeyLastTab] = value; }
        }

        private static bool GetBool(string key, bool def)
        {
            if (Store.Values.TryGetValue(key, out var v) && v is bool b) return b;
            return def;
        }

        private static int GetInt(string key, int def)
        {
            if (Store.Values.TryGetValue(key, out var v) && v is int i) return i;
            return def;
        }

        private static string GetString(string key, string def)
        {
            if (Store.Values.TryGetValue(key, out var v) && v is string s && !string.IsNullOrEmpty(s)) return s;
            return def;
        }
    }
}