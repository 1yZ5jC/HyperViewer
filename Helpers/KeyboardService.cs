using System;
using System.Collections.Generic;
using Windows.Storage;
using Windows.System;

namespace HyperViewer.Helpers
{
    /// <summary>
    /// 快捷键绑定服务: 单键动作可自定义 (LocalSettings 持久化)。
    /// Ctrl 组合键 (Ctrl+C/F/O/R) 保持固定, 不参与自定义。
    /// </summary>
    public static class KeyboardService
    {
        private const string KeyStore = "ShortcutBindings";

        /// <summary>可自定义的动作名 (顺序即设置页展示顺序)。</summary>
        public static readonly string[] Actions =
        {
            "Prev", "Next", "First", "Last", "Rotate", "FlipH", "FlipV",
            "ResetZoom", "ZoomIn", "ZoomOut", "ToggleChrome", "SlideShow", "FullScreen"
        };

        private static readonly Dictionary<string, VirtualKey> Defaults =
            new Dictionary<string, VirtualKey>
            {
                ["Prev"] = VirtualKey.Left,
                ["Next"] = VirtualKey.Right,
                ["First"] = VirtualKey.Home,
                ["Last"] = VirtualKey.End,
                ["Rotate"] = VirtualKey.R,
                ["FlipH"] = VirtualKey.H,
                ["FlipV"] = VirtualKey.V,
                ["ResetZoom"] = VirtualKey.Number0,
                ["ZoomIn"] = VirtualKey.Add,
                ["ZoomOut"] = VirtualKey.Subtract,
                ["ToggleChrome"] = VirtualKey.F,
                ["SlideShow"] = VirtualKey.F5,
                ["FullScreen"] = VirtualKey.F11,
            };

        /// <summary>可用于自定义的候选按键 (组合框选项)。</summary>
        public static readonly VirtualKey[] CandidateKeys =
        {
            VirtualKey.Left, VirtualKey.Right, VirtualKey.Up, VirtualKey.Down,
            VirtualKey.Space, VirtualKey.Home, VirtualKey.End,
            VirtualKey.R, VirtualKey.H, VirtualKey.V, VirtualKey.F,
            VirtualKey.Number0, VirtualKey.Number1, VirtualKey.Number2,
            VirtualKey.Add, VirtualKey.Subtract,
            VirtualKey.F5, VirtualKey.F8, VirtualKey.F11,
        };

        private static readonly Dictionary<string, VirtualKey> Bindings = new Dictionary<string, VirtualKey>();

        static KeyboardService()
        {
            if (ApplicationData.Current.LocalSettings.Values[KeyStore] is string raw)
            {
                foreach (var pair in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = pair.Split('=');
                    if (parts.Length == 2
                        && Defaults.ContainsKey(parts[0])
                        && Enum.TryParse<VirtualKey>(parts[1], out var key))
                    {
                        Bindings[parts[0]] = key;
                    }
                }
            }
        }

        public static VirtualKey GetKey(string action)
            => Bindings.TryGetValue(action, out var k) ? k
               : Defaults.TryGetValue(action, out var d) ? d : VirtualKey.None;

        public static void SetKey(string action, VirtualKey key)
        {
            Bindings[action] = key;
            Save();
        }

        public static void ResetAll()
        {
            Bindings.Clear();
            ApplicationData.Current.LocalSettings.Values.Remove(KeyStore);
        }

        /// <summary>按键 → 已绑定动作 (首个匹配), 未绑定返回 null。</summary>
        public static string ActionForKey(VirtualKey key)
        {
            foreach (var pair in Bindings)
            {
                if (pair.Value == key) return pair.Key;
            }
            foreach (var pair in Defaults)
            {
                if (pair.Value == key && !Bindings.ContainsKey(pair.Key)) return pair.Key;
            }
            return null;
        }

        private static void Save()
        {
            var parts = new List<string>();
            foreach (var pair in Bindings)
            {
                parts.Add(pair.Key + "=" + pair.Value);
            }
            ApplicationData.Current.LocalSettings.Values[KeyStore] = string.Join(";", parts);
        }
    }
}
