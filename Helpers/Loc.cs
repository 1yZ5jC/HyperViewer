using Windows.ApplicationModel.Resources;

namespace HyperViewer.Helpers
{
    /// <summary>
    /// 资源字符串访问 (跟随系统语言的 resw)。
    /// </summary>
    public static class Loc
    {
        private static readonly ResourceLoader _loader = new ResourceLoader();

        public static string Get(string key) => _loader.GetString(key);

        public static string Format(string key, params object[] args) =>
            string.Format(_loader.GetString(key), args);
    }
}