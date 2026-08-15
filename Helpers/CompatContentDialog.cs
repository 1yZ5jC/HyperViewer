using Windows.UI.Xaml.Controls;

namespace HyperViewer.Helpers
{
    /// <summary>
    /// 10240 兼容的 ContentDialog: CloseButtonText 是 1703+ (UniversalApiContract v4) 才有,
    /// 旧系统回退到 SecondaryButtonText (8.1 原生)。
    /// </summary>
    public class CompatContentDialog : ContentDialog
    {
        /// <summary>
        /// 关闭按钮文本: 1703+ 用 CloseButtonText, 10240/14393/15063/16299 用 SecondaryButtonText。
        /// </summary>
        public string CompatCloseButtonText
        {
            get { return UwpCompat.HasContractV4 ? CloseButtonText : SecondaryButtonText; }
            set
            {
                if (UwpCompat.HasContractV4) CloseButtonText = value;
                else SecondaryButtonText = value;
            }
        }
    }
}