using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Storage;

namespace HyperViewer.Helpers
{
    /// <summary>
    /// 收藏持久化: LocalSettings 存路径集合 (保留添加/拖拽后的顺序)。
    /// </summary>
    public static class FavoritesService
    {
        private const string SettingsKey = "FavoritePaths";
        private const char Separator = '|';

        private static readonly HashSet<string> Set =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> Order = new List<string>();
        private static readonly object Sync = new object();

        static FavoritesService()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            if (values.TryGetValue(SettingsKey, out var raw) && raw is string s && !string.IsNullOrEmpty(s))
            {
                lock (Sync)
                {
                    foreach (var p in s.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (Set.Add(p)) Order.Add(p);
                    }
                }
            }
        }

        public static bool IsFavorite(string path)
        {
            lock (Sync)
            {
                return !string.IsNullOrEmpty(path) && Set.Contains(path);
            }
        }

        public static void Add(string path)
        {
            lock (Sync)
            {
                if (string.IsNullOrEmpty(path) || Set.Contains(path)) return;
                Set.Add(path);
                Order.Add(path);
                Persist();
            }
        }

        public static void Remove(string path)
        {
            lock (Sync)
            {
                if (!string.IsNullOrEmpty(path) && Set.Remove(path))
                {
                    Order.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                    Persist();
                }
            }
        }

        /// <summary>按顺序返回全部收藏路径 (只读快照)。</summary>
        public static IReadOnlyList<string> GetAll()
        {
            lock (Sync)
            {
                return Order.ToList();
            }
        }

        /// <summary>用新顺序整体替换收藏 (拖拽排序后调用)。</summary>
        public static void SaveOrder(IEnumerable<string> paths)
        {
            lock (Sync)
            {
                Order.Clear();
                Set.Clear();
                if (paths != null)
                {
                    foreach (var p in paths)
                    {
                        if (!string.IsNullOrEmpty(p) && Set.Add(p)) Order.Add(p);
                    }
                }
                Persist();
            }
        }

        private static void Persist()
        {
            ApplicationData.Current.LocalSettings.Values[SettingsKey] = string.Join(Separator.ToString(), Order);
        }
    }
}
