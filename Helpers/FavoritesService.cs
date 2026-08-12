using System;
using System.Collections.Generic;
using Windows.Storage;

namespace HyperViewer.Helpers
{
    /// <summary>
    /// 收藏持久化: LocalSettings 存路径集合 (按路径匹配)。
    /// </summary>
    public static class FavoritesService
    {
        private const string SettingsKey = "FavoritePaths";
        private const char Separator = '|';

        private static readonly HashSet<string> Cache =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Sync = new object();

        static FavoritesService()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            if (values.TryGetValue(SettingsKey, out var raw) && raw is string s && !string.IsNullOrEmpty(s))
            {
                lock (Sync)
                {
                    foreach (var p in s.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries))
                        Cache.Add(p);
                }
            }
        }

        public static bool IsFavorite(string path)
        {
            lock (Sync)
            {
                return !string.IsNullOrEmpty(path) && Cache.Contains(path);
            }
        }

        public static void Add(string path)
        {
            lock (Sync)
            {
                if (!string.IsNullOrEmpty(path) && Cache.Add(path)) Persist();
            }
        }

        public static void Remove(string path)
        {
            lock (Sync)
            {
                if (Cache.Remove(path)) Persist();
            }
        }

        private static void Persist()
        {
            lock (Sync)
            {
                ApplicationData.Current.LocalSettings.Values[SettingsKey] = string.Join(Separator.ToString(), Cache);
            }
        }
    }
}