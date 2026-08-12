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