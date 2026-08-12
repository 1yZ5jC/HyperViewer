using System;
using System.Threading.Tasks;
using Windows.Graphics.Printing;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Printing;

namespace HyperViewer.Services
{
    /// <summary>
    /// 图片打印服务: 通过 PrintManager 整页打印当前图片。
    /// </summary>
    public static class PrintService
    {
        private static PrintDocument _doc;
        private static UIElement _page;
        private static BitmapImage _bitmap;

        public static async Task PrintAsync(StorageFile file)
        {
            if (file == null) return;
            try
            {
                _bitmap = new BitmapImage();
                using (var stream = await file.OpenReadAsync())
                {
                    await _bitmap.SetSourceAsync(stream);
                }
                var pm = PrintManager.GetForCurrentView();
                pm.PrintTaskRequested += OnPrintTaskRequested;
                try
                {
                    await PrintManager.ShowPrintUIAsync();
                }
                finally
                {
                    pm.PrintTaskRequested -= OnPrintTaskRequested;
                }
            }
            catch
            {
                // 无打印机/打印不可用时静默失败
            }
        }

        private static void OnPrintTaskRequested(PrintManager sender, PrintTaskRequestedEventArgs args)
        {
            args.Request.CreatePrintTask("HyperViewer", OnPrintTaskSourceRequested);
        }

        private static void OnPrintTaskSourceRequested(PrintTaskSourceRequestedArgs args)
        {
            _doc = new PrintDocument();
            _doc.Paginate += Doc_Paginate;
            _doc.GetPreviewPage += Doc_GetPreviewPage;
            _doc.AddPages += Doc_AddPages;
            args.SetSource(_doc.DocumentSource);
        }

        private static void Doc_Paginate(object sender, PaginateEventArgs e)
        {
            var description = e.PrintTaskOptions.GetPageDescription(0);
            var dpiX = description.DpiX > 0 ? description.DpiX : 96;
            var dpiY = description.DpiY > 0 ? description.DpiY : 96;
            var width = description.ImageableRect.Width * 96.0 / dpiX;
            var height = description.ImageableRect.Height * 96.0 / dpiY;
            var img = new Image
            {
                Source = _bitmap,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(48)
            };
            _page = new Grid
            {
                Width = width,
                Height = height
            };
            ((Grid)_page).Children.Add(img);
            _doc.SetPreviewPageCount(1, PreviewPageCountType.Final);
        }

        private static void Doc_GetPreviewPage(object sender, GetPreviewPageEventArgs e)
        {
            _doc.SetPreviewPage(e.PageNumber, _page);
        }

        private static void Doc_AddPages(object sender, AddPagesEventArgs e)
        {
            _doc.AddPage(_page);
            _doc.AddPagesComplete();
        }
    }
}
