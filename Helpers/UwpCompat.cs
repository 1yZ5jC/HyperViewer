using Windows.Foundation.Metadata;

namespace HyperViewer.Helpers
{
    /// <summary>
    /// 10240 兼容守卫: 部分 API 是 14393+ (UniversalApiContract v2) 才引入,
    /// 编译期不报错 (winmd 按 TargetPlatformVersion 解析), 但运行时在 10240 上调用会崩溃。
    /// </summary>
    public static class UwpCompat
    {
        /// <summary>
        /// 契约 v2 (Anniversary 14393): BringIntoViewOptions / StartBringIntoView(BringIntoViewOptions)、
        /// ScrollViewer.ChangeView 四参重载等。
        /// </summary>
        public static readonly bool HasContractV2 =
            ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 2);

        /// <summary>
        /// 契约 v4 (Creators 1703): ContentDialog.CloseButtonText / Clipboard.SetContentWithOptions 等。
        /// </summary>
        public static readonly bool HasContractV4 =
            ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 4);
    }
}