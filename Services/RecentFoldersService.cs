using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.AccessCache;

namespace HyperViewer.Services
{
    /// <summary>
    /// 最近打开的文件夹列表，使用 StorageApplicationPermissions.FutureAccessList
    /// 保存令牌，配合 LocalSettings 的顺序索引，便于应用下次启动恢复。
    /// </summary>
    public sealed class RecentFoldersService
    {
        /// <summary>全局共享实例 (主页面与设置页共用)。</summary>
        public static RecentFoldersService Instance { get; } = new RecentFoldersService();

        private const string IndexKey = "RecentFoldersIndex";
        private const int MaxCount = 10;
        private readonly List<string> _tokens = new List<string>();
        private readonly List<StorageFolder> _folders = new List<StorageFolder>();

        /// <summary>列表内容变化 (含异步加载完成后)。</summary>
        public event EventHandler Changed;

        public IReadOnlyList<StorageFolder> Folders => _folders;

        public RecentFoldersService()
        {
            LoadIndex();
            _ = LoadFoldersAsync();
        }

        private void LoadIndex()
        {
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Values[IndexKey] is string joined)
            {
                _tokens.AddRange(joined.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
            }
        }

        private async Task LoadFoldersAsync()
        {
            var fal = StorageApplicationPermissions.FutureAccessList;
            for (int i = _tokens.Count - 1; i >= 0; i--)
            {
                var token = _tokens[i];
                try
                {
                    var folder = await fal.GetFolderAsync(token);
                    if (folder != null) _folders.Add(folder);
                }
                catch
                {
                    // 令牌失效或文件夹被删，移除
                    _tokens.RemoveAt(i);
                }
            }
            SaveIndex();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public Task AddAsync(StorageFolder folder)
        {
            if (folder == null) return Task.CompletedTask;

            // 去重：若已存在，先移除旧记录
            for (int i = _folders.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_folders[i]?.Path, folder.Path, StringComparison.OrdinalIgnoreCase))
                {
                    _folders.RemoveAt(i);
                    if (i < _tokens.Count) _tokens.RemoveAt(i);
                }
            }

            _folders.Insert(0, folder);
            var fal = StorageApplicationPermissions.FutureAccessList;
            var token = fal.Add(folder, folder.Name);
            _tokens.Insert(0, token);

            while (_folders.Count > MaxCount)
            {
                var removedToken = _tokens[_tokens.Count - 1];
                _tokens.RemoveAt(_tokens.Count - 1);
                _folders.RemoveAt(_folders.Count - 1);
                try { fal.Remove(removedToken); } catch { }
            }

            SaveIndex();
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        private void SaveIndex()
        {
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values[IndexKey] = string.Join("|", _tokens);
        }

        public Task RemoveAsync(StorageFolder folder)
        {
            if (folder == null) return Task.CompletedTask;

            for (int i = _folders.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_folders[i]?.Path, folder.Path, StringComparison.OrdinalIgnoreCase))
                {
                    _folders.RemoveAt(i);
                    if (i < _tokens.Count) _tokens.RemoveAt(i);
                }
            }

            SaveIndex();
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        /// <summary>清空全部记录并移除访问令牌。</summary>
        public Task ClearAsync()
        {
            var fal = StorageApplicationPermissions.FutureAccessList;
            foreach (var token in _tokens)
            {
                try { fal.Remove(token); } catch { }
            }
            _tokens.Clear();
            _folders.Clear();
            SaveIndex();
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }
}
