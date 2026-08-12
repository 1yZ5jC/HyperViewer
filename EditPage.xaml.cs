using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using HyperViewer.Helpers;
using HyperViewer.Models;
using HyperViewer.Services;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace HyperViewer
{
    /// <summary>
    /// 编辑页: 裁剪 + 像素级处理 (调色后续加入), 保存副本不覆盖原图。
    /// </summary>
    public sealed partial class EditPage : Page
    {
        private PhotoItem _photo;
        private EditableImage _image;
        private EditableImage _previewBase; // 调色预览用的降采样基图

        private bool _cropMode;
        private Rect _cropRect;
        private Point _dragStart;
        private bool _dragging;
        private bool _dragMove; // true=移动选区, false=新画选区

        public EditPage()
        {
            this.InitializeComponent();
            HostSizer.SizeChanged += (_, __) => UpdateHostSize();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _photo = e.Parameter as PhotoItem;
            TitleText.Text = _photo?.Name ?? Loc.Get("EditTitle");
            if (_photo != null)
            {
                _ = LoadOriginalAsync();
            }
        }

        private async Task LoadOriginalAsync()
        {
            BusyRing.IsActive = true;
            try
            {
                _image = await ImageEditService.LoadAsync(_photo.File);
                ResetAdjust();
                RenderPreview();
                StatusText.Text = Loc.Format("EditLoaded", _image.Width, _image.Height);
            }
            catch
            {
                StatusText.Text = Loc.Get("EditLoadFailed");
            }
            BusyRing.IsActive = false;
        }

        private void RenderPreview()
        {
            if (_image == null) return;
            _previewBase = ImageEditService.Downsample(_image.Pixels, _image.Width, _image.Height, 1024);
            UpdatePreview();
        }

        private void RenderPixels(byte[] px, int w, int h)
        {
            var wb = new WriteableBitmap(w, h);
            using (var stream = wb.PixelBuffer.AsStream())
            {
                stream.Write(px, 0, px.Length);
            }
            wb.Invalidate();
            PreviewImage.Source = wb;
            UpdateHostSize();
        }

        private void UpdatePreview()
        {
            if (_previewBase == null) return;
            var tmp = new byte[_previewBase.Pixels.Length];
            Array.Copy(_previewBase.Pixels, tmp, tmp.Length);
            ImageEditService.ApplyAdjustments(
                tmp,
                (int)BrightnessSlider.Value,
                (int)ContrastSlider.Value,
                (int)SaturationSlider.Value,
                (AdjustFilter)FilterBox.SelectedIndex);
            RenderPixels(tmp, _previewBase.Width, _previewBase.Height);
        }

        private void Adjust_Changed(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
            => UpdatePreview();

        private void Filter_Changed(object sender, SelectionChangedEventArgs e)
            => UpdatePreview();

        private void ResetAdjust()
        {
            if (BrightnessSlider != null) BrightnessSlider.Value = 0;
            if (ContrastSlider != null) ContrastSlider.Value = 0;
            if (SaturationSlider != null) SaturationSlider.Value = 0;
            if (FilterBox != null) FilterBox.SelectedIndex = 0;
        }

        private async void ApplyAdjust_Click(object sender, RoutedEventArgs e)
        {
            if (_image == null) return;
            BusyRing.IsActive = true;
            StatusText.Text = Loc.Get("EditProcessing");
            try
            {
                var b = (int)BrightnessSlider.Value;
                var c = (int)ContrastSlider.Value;
                var s = (int)SaturationSlider.Value;
                var f = (AdjustFilter)FilterBox.SelectedIndex;
                await Task.Run(() => ImageEditService.ApplyAdjustments(_image.Pixels, b, c, s, f));
                ResetAdjust();
                RenderPreview();
                StatusText.Text = Loc.Format("EditApplied", _image.Width, _image.Height);
            }
            catch
            {
                StatusText.Text = Loc.Get("EditProcessFailed");
            }
            BusyRing.IsActive = false;
        }

        private void UpdateHostSize()
        {
            if (_image == null || HostSizer.ActualWidth < 1 || HostSizer.ActualHeight < 1) return;
            double scale = Math.Min(HostSizer.ActualWidth / _image.Width, HostSizer.ActualHeight / _image.Height);
            ImageHost.Width = Math.Max(1, _image.Width * scale);
            ImageHost.Height = Math.Max(1, _image.Height * scale);
        }

        // ===== 裁剪交互 =====

        private void CropToggle_Click(object sender, RoutedEventArgs e)
        {
            _cropMode = !_cropMode;
            CropToggle.IsChecked = _cropMode;
            HintText.Visibility = _cropMode ? Visibility.Visible : Visibility.Collapsed;
            if (!_cropMode) ClearCropUi();
        }

        private void ClearCropUi()
        {
            _cropRect = default(Rect);
            CropRectVisual.Visibility = Visibility.Collapsed;
            DimPath.Visibility = Visibility.Collapsed;
            _dragging = false;
        }

        private void CropCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!_cropMode) return;
            var pos = e.GetCurrentPoint(CropCanvas).Position;
            _dragging = true;
            if (!_cropRect.IsEmpty && _cropRect.Contains(pos))
            {
                _dragMove = true;
                _dragStart = pos;
                CropCanvas.CapturePointer(e.Pointer);
            }
            else
            {
                _dragMove = false;
                _dragStart = pos;
                _cropRect = new Rect(pos, new Size(1, 1));
                UpdateCropVisuals();
                CropCanvas.CapturePointer(e.Pointer);
            }
        }

        private void CropCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_dragging) return;
            var pos = e.GetCurrentPoint(CropCanvas).Position;
            pos.X = Math.Max(0, Math.Min(pos.X, CropCanvas.ActualWidth));
            pos.Y = Math.Max(0, Math.Min(pos.Y, CropCanvas.ActualHeight));

            if (_dragMove)
            {
                double dx = pos.X - _dragStart.X;
                double dy = pos.Y - _dragStart.Y;
                _cropRect.X += dx;
                _cropRect.Y += dy;
                if (_cropRect.X < 0) _cropRect.X = 0;
                if (_cropRect.Y < 0) _cropRect.Y = 0;
                if (_cropRect.Right > CropCanvas.ActualWidth)
                    _cropRect.X = Math.Max(0, CropCanvas.ActualWidth - _cropRect.Width);
                if (_cropRect.Bottom > CropCanvas.ActualHeight)
                    _cropRect.Y = Math.Max(0, CropCanvas.ActualHeight - _cropRect.Height);
                _dragStart = pos;
            }
            else
            {
                double x = Math.Min(_dragStart.X, pos.X);
                double y = Math.Min(_dragStart.Y, pos.Y);
                _cropRect = new Rect(x, y, Math.Abs(pos.X - _dragStart.X), Math.Abs(pos.Y - _dragStart.Y));
            }
            UpdateCropVisuals();
        }

        private void CropCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _dragging = false;
            if (_cropRect.Width < 8 || _cropRect.Height < 8) ClearCropUi();
        }

        private void UpdateCropVisuals()
        {
            if (_cropRect.IsEmpty || _cropRect.Width < 1 || _cropRect.Height < 1)
            {
                CropRectVisual.Visibility = Visibility.Collapsed;
                DimPath.Visibility = Visibility.Collapsed;
                return;
            }

            CropRectVisual.Visibility = Visibility.Visible;
            CropRectVisual.Width = _cropRect.Width;
            CropRectVisual.Height = _cropRect.Height;
            Canvas.SetLeft(CropRectVisual, _cropRect.X);
            Canvas.SetTop(CropRectVisual, _cropRect.Y);

            // 遮罩: 偶数填充法则挖洞 (外矩形 - 内选区)
            DimPath.Visibility = Visibility.Visible;
            double w = CropCanvas.ActualWidth;
            double h = CropCanvas.ActualHeight;
            var geom = new PathGeometry { FillRule = FillRule.EvenOdd };
            var outer = new PathFigure { IsClosed = true, IsFilled = true, StartPoint = new Point(0, 0) };
            outer.Segments.Add(new LineSegment { Point = new Point(w, 0) });
            outer.Segments.Add(new LineSegment { Point = new Point(w, h) });
            outer.Segments.Add(new LineSegment { Point = new Point(0, h) });
            geom.Figures.Add(outer);
            var inner = new PathFigure { IsClosed = true, IsFilled = true, StartPoint = new Point(_cropRect.X, _cropRect.Y) };
            inner.Segments.Add(new LineSegment { Point = new Point(_cropRect.Right, _cropRect.Y) });
            inner.Segments.Add(new LineSegment { Point = new Point(_cropRect.Right, _cropRect.Bottom) });
            inner.Segments.Add(new LineSegment { Point = new Point(_cropRect.X, _cropRect.Bottom) });
            geom.Figures.Add(inner);
            DimPath.Data = geom;
        }

        private void ApplyCrop_Click(object sender, RoutedEventArgs e)
        {
            if (!_cropMode || _cropRect.IsEmpty || CropCanvas.ActualWidth < 1) return;
            double fx = _image.Width / CropCanvas.ActualWidth;
            double fy = _image.Height / CropCanvas.ActualHeight;
            int px = (int)Math.Round(_cropRect.X * fx);
            int py = (int)Math.Round(_cropRect.Y * fy);
            int pw = (int)Math.Round(_cropRect.Width * fx);
            int ph = (int)Math.Round(_cropRect.Height * fy);
            _image = _image.Crop(px, py, pw, ph);
            ResetAdjust();
            RenderPreview();
            ClearCropUi();
            StatusText.Text = Loc.Format("EditCropped", _image.Width, _image.Height);
        }

        private async void Reset_Click(object sender, RoutedEventArgs e)
        {
            ClearCropUi();
            await LoadOriginalAsync();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_image == null || _photo == null) return;
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(_photo.Name) + "_edited"
            };
            picker.FileTypeChoices.Add(Loc.Get("EditTypePNG"), new List<string> { ".png" });
            picker.FileTypeChoices.Add(Loc.Get("EditTypeJPEG"), new List<string> { ".jpg" });
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            Guid encoderId = string.Equals(file.FileType, ".jpg", StringComparison.OrdinalIgnoreCase)
                ? BitmapEncoder.JpegEncoderId
                : BitmapEncoder.PngEncoderId;
            try
            {
                await ImageEditService.SaveAsync(file, _image.Pixels, _image.Width, _image.Height, encoderId);
                StatusText.Text = Loc.Format("EditSaved", file.Name);
            }
            catch
            {
                StatusText.Text = Loc.Get("EditSaveFailed");
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}