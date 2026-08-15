using Windows.Foundation.Metadata;

namespace HyperViewer.Helpers
{
    /// <summary>
    /// 10240 兼容守卫: 部分 API 是 14393+ (UniversalApiContract v2) 才引入,
    /// 编译期不报错 (winmd 按 TargetPlatformVersion 解析), 但运行时在 10240 上调用会崩溃。
    /// </summary>
    public static class UwpCompat
    {
        // 注意: 用属性而非 static readonly —— 模拟开关随时可切换, 即时生效。

        /// <summary>
        /// 契约 v2 (Anniversary 14393): BringIntoViewOptions / StartBringIntoView(BringIntoViewOptions)、
        /// ScrollViewer.ChangeView 四参重载等。
        /// 开发者开关 DebugSimulate10240 开启时强制按 10240 处理 (无 v2)。
        /// </summary>
        public static bool HasContractV2 =>
            !SettingsService.DebugSimulate10240
            && ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 2);

        /// <summary>
        /// 契约 v4 (Creators 1703): ContentDialog.CloseButtonText / Clipboard.SetContentWithOptions 等。
        /// 开发者开关 DebugSimulate10240 开启时强制按 10240 处理 (无 v4)。
        /// </summary>
        public static bool HasContractV4 =>
            !SettingsService.DebugSimulate10240
            && ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 4);

        /// <summary>
        /// 契约 v5 (Fall Creators 1709): CoreApplication.RequestRestartAsync 等。
        /// </summary>
        public static bool HasContractV5 =>
            !SettingsService.DebugSimulate10240
            && ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 5);

        /// <summary>
        /// InkToolbar 是否可用 (10240 上可能缺失, 无则不加墨迹工具栏)。
        /// </summary>
        public static bool HasInkToolbar =>
            !SettingsService.DebugSimulate10240
            && ApiInformation.IsTypePresent("Windows.UI.Xaml.Controls.InkToolbar");

        /// <summary>
        /// XamlRoot (1703+, 契约 v4): 设备像素缩放; 低版本回退 1.0。
        /// </summary>
        public static bool HasXamlRoot =>
            !SettingsService.DebugSimulate10240
            && ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 4);
    }
}