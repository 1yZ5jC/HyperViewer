using System;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Printing;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Printing;
using HyperViewer.Helpers;

namespace HyperViewer.Services
{
    /// <summary>
    /// 图片打印服务: 通过 PrintManager 整页打印当前图片。
    /// 每次调用创建独立的 PrintSession, 由事件闭包持有, 避免连续打印时
    /// 静态状态 (_doc/_page/_bitmap) 互相覆盖。
    /// </summary>
    public static class PrintService
    {
        private sealed class PrintSession
        {
            public ImageSource Source;
            public PrintDocument Document;
            public UIElement Page;
            public PrintTask Task;
        }

        public static async Task PrintAsync(StorageFile file)
        {
            if (file == null) return;

            var session = new PrintSession();
            try
            {
                // 解码在调用线程完成, 打印回调只负责排版, 不做文件 IO。
                var bitmap = new BitmapImage();
                using (var stream = await file.OpenReadAsync())
                {
                    await bitmap.SetSourceAsync(stream);
                }
                session.Source = bitmap;

                var pm = PrintManager.GetForCurrentView();
                TypedEventHandler<PrintManager, PrintTaskRequestedEventArgs> onRequested = (sender, args) =>
                {
                    // 保存 PrintTask 引用, 并订阅完成结果便于排查 (回调不在 UI 线程, 只记日志)。
                    session.Task = args.Request.CreatePrintTask("HyperViewer", a => BuildDocument(session, a));
                    session.Task.Completed += (t, e) =>
                        DebugLog.Write("PRINT", $"task completed: {e.Completion}");
                };

                pm.PrintTaskRequested += onRequested;
                try
                {
                    await PrintManager.ShowPrintUIAsync();
                }
                finally
                {
                    pm.PrintTaskRequested -= onRequested;
                }
            }
            catch (Exception ex)
            {
                // 无打印机 / 打印服务不可用 / ShowPrintUIAsync 抛错时, 不再静默失败。
                DebugLog.Write("PRINT", $"print failed: {ex}");
                await ShowUnavailableAsync();
            }
        }

        private static async Task ShowUnavailableAsync()
        {
            try
            {
                await new CompatContentDialog
                {
                    Title = Loc.Get("PrintTitle"),
                    Content = Loc.Get("PrintUnavailable"),
                    CompatCloseButtonText = Loc.Get("DialogOK")
                }.ShowAsync();
            }
            catch (Exception ex)
            {
                DebugLog.Write("PRINT", $"unavailable dialog failed: {ex}");
            }
        }

        private static void BuildDocument(PrintSession session, PrintTaskSourceRequestedArgs args)
        {
            var doc = new PrintDocument();
            session.Document = doc;
            doc.Paginate += (s, e) => Paginate(session, e);
            doc.GetPreviewPage += (s, e) => doc.SetPreviewPage(e.PageNumber, session.Page);
            doc.AddPages += (s, e) =>
            {
                doc.AddPage(session.Page);
                doc.AddPagesComplete();
            };
            args.SetSource(doc.DocumentSource);
        }

        private static void Paginate(PrintSession session, PaginateEventArgs e)
        {
            var d = e.PrintTaskOptions.GetPageDescription(0);
            // PageSize 与 ImageableRect 均以 DIP 为单位, 不能再乘 96/DpiX,
            // 否则高 DPI 打印机上整页会缩到 96/DPI, 图片变小甚至被裁切。
            double left = Math.Max(0, d.ImageableRect.Left);
            double top = Math.Max(0, d.ImageableRect.Top);
            double right = Math.Max(0, d.PageSize.Width - d.ImageableRect.Right);
            double bottom = Math.Max(0, d.PageSize.Height - d.ImageableRect.Bottom);

            session.Page = new Border
            {
                Width = d.PageSize.Width,
                Height = d.PageSize.Height,
                Padding = new Thickness(left, top, right, bottom),
                Child = new Image
                {
                    Source = session.Source,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            session.Document.SetPreviewPageCount(1, PreviewPageCountType.Final);
        }
    }
}
