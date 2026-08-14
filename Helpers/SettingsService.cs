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
        private const string KeySlideRandomOrder = "SlideRandomOrder";
        private const string KeySlideRandomTransition = "SlideRandomTransition";
        private const string KeySlideBlurBackground = "SlideBlurBackground";
        private const string KeyLastTab = "LastTab";
        private const string KeyRestoreLastFolder = "RestoreLastFolder";
        private const string KeyLastFolderPath = "LastFolderPath";
        private const string KeyLiveTileEnabled = "LiveTileEnabled";
        private const string KeyTileSingleImage = "TileSingleImage";
        private const string KeyTileRotationEnabled = "TileRotationEnabled";
        private const string KeyTileRotationSeconds = "TileRotationSeconds";

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

        /// <summary>幻灯片随机播放顺序。</summary>
        public static bool SlideRandomOrder
        {
            get { return GetBool(KeySlideRandomOrder, false); }
            set { Store.Values[KeySlideRandomOrder] = value; }
        }

        /// <summary>幻灯片每张随机转场。</summary>
        public static bool SlideRandomTransition
        {
            get { return GetBool(KeySlideRandomTransition, false); }
            set { Store.Values[KeySlideRandomTransition] = value; }
        }

        /// <summary>幻灯片模糊背景 (每张跟随当前图片色彩)。</summary>
        public static bool SlideBlurBackground
        {
            get { return GetBool(KeySlideBlurBackground, false); }
            set { Store.Values[KeySlideBlurBackground] = value; }
        }

        // 最近一次打开的图库 Tab（Albums / AllPhotos / Folders）
        public static string LastTab
        {
            get { return GetString(KeyLastTab, "Albums"); }
            set { Store.Values[KeyLastTab] = value; }
        }

        /// <summary>启动时是否恢复上次浏览的文件夹。</summary>
        public static bool RestoreLastFolder
        {
            get { return GetBool(KeyRestoreLastFolder, false); }
            set { Store.Values[KeyRestoreLastFolder] = value; }
        }

        /// <summary>最近一次浏览的文件夹路径 (供启动恢复)。</summary>
        public static string LastFolderPath
        {
            get { return GetString(KeyLastFolderPath, null); }
            set { Store.Values[KeyLastFolderPath] = value; }
        }

        /// <summary>是否在开始菜单动态磁贴上显示最近照片 (自动刷新)。</summary>
        public static bool LiveTileEnabled
        {
            get { return GetBool(KeyLiveTileEnabled, true); }
            set { Store.Values[KeyLiveTileEnabled] = value; }
        }

        /// <summary>磁贴仅显示一张图片 (所有尺寸共用同一张最近照片)。</summary>
        public static bool TileSingleImage
        {
            get { return GetBool(KeyTileSingleImage, false); }
            set { Store.Values[KeyTileSingleImage] = value; }
        }

        /// <summary>磁贴轮换: 在当前文件夹图片中定时轮换显示。</summary>
        public static bool TileRotationEnabled
        {
            get { return GetBool(KeyTileRotationEnabled, false); }
            set { Store.Values[KeyTileRotationEnabled] = value; }
        }

        /// <summary>磁贴轮换间隔 (秒): 30 / 60 / 300 / 600。</summary>
        public static int TileRotationSeconds
        {
            get { return GetInt(KeyTileRotationSeconds, 60); }
            set { Store.Values[KeyTileRotationSeconds] = value; }
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