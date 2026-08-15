using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using HyperViewer.Helpers;
using HyperViewer.Models;
using HyperViewer.Services;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.Foundation.Metadata;

namespace HyperViewer
{
    /// <summary>
    /// 编辑页: 操作栈 (旋转/翻转/裁剪/调色/白平衡/模糊锐化/边框/马赛克/文字) + 实时预览, 保存副本不覆盖原图。
    /// </summary>
    public sealed partial class EditPage : Page
    {
        private enum EditMode { None, Crop, Ink, Mosaic, Text }

        private PhotoItem _photo;
        private EditableImage _original;   // 全分辨率原图 (只读基准)
        private EditableImage _previewBase; // 调色预览用的降采样基图
        private int _previewW;
        private int _previewH;
        private WriteableBitmap _rawCompareBmp;

        private EditMode _mode;
        private readonly List<EditOp> _ops = new List<EditOp>();
        private int _opIndex;
        private int _renderGen;

        // 裁剪交互
        private Rect _cropRect;
        private Point _dragStart;
        private bool _dragging;
        private bool _dragMove;

        // 马赛克
        private readonly List<Point> _pendingMosaic = new List<Point>();

        // 文字贴纸 (图片上直接编辑)
        private Point _stickerNorm = new Point(0.5, 0.5);
        private bool _stickerDragging;
        private Point _gripStart;
        private bool _textDragging;
        private Point _textDragStart;
        private double _stickerDragWidth = 200;

        // 对比 (按住显示原图)
        private bool _comparing;

        // 直方图
        private WriteableBitmap _lastHisto;

        public EditPage()
        {
            this.InitializeComponent();
            HostSizer.SizeChanged += (_, __) => UpdateHostSize();
            TextColorCombo.SelectedIndex = 1;   // 默认黑色文字 (画图风格)
            BorderColorCombo.SelectedIndex = 0; // 默认白色边框
            try
            {
                var titleBar = CoreApplication.GetCurrentView().TitleBar;
                titleBar.LayoutMetricsChanged += OnTitleMetricsChanged;
                Window.Current.SizeChanged += OnWindowSizeChanged;
            }
            catch { }
            UpdateTitleBarInset();
            Loaded += (_, __) => ApplyDragRegion();
        }

        /// <summary>编辑页顶栏作为标题栏拖拽区 (与主页一致)。</summary>
        private void ApplyDragRegion()
        {
            try { Window.Current.SetTitleBar(TopBarGrid); }
            catch { }
        }

        private void OnTitleMetricsChanged(CoreApplicationViewTitleBar sender, object args)
            => UpdateTitleBarInset();

        private void OnWindowSizeChanged(object sender, WindowSizeChangedEventArgs e)
            => UpdateTitleBarInset();

        private void UpdateTitleBarInset()
        {
            double inset = 0;
            try { inset = CoreApplication.GetCurrentView().TitleBar.SystemOverlayRightInset; }
            catch { }
            if (TitleInsetPad != null) TitleInsetPad.Width = inset;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            // InkToolbar 需周年更新 (14393) 及以上, 低版本动态创建会被系统忽略
            EnsureInkToolbar();
            InkHost.InkPresenter.InputDeviceTypes = CoreInputDeviceTypes.Mouse
                | CoreInputDeviceTypes.Touch
                | CoreInputDeviceTypes.Pen;
            _photo = e.Parameter as PhotoItem;
            TitleText.Text = _photo?.Name ?? Loc.Get("EditTitle");
            if (_photo != null)
            {
                _ = LoadOriginalAsync();
            }
        }

        private void EnsureInkToolbar()
        {
            if (InkBarPanel.Children.Count > 0 && InkBarPanel.Children[0] is InkToolbar) return;
            if (!Helpers.UwpCompat.HasInkToolbar) return;
            InkBarPanel.Children.Insert(0, new InkToolbar { TargetInkCanvas = InkHost });
        }

        /// <summary>设备像素缩放 (XamlRoot 属 1703+ 才有, 低版本回退 1.0)。</summary>
        private static double DevScaleOf(UIElement e)
        {
            if (!Helpers.UwpCompat.HasXamlRoot) return 1.0;
            try { return e.XamlRoot?.RasterizationScale ?? 1.0; } catch { return 1.0; }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            try
            {
                CoreApplication.GetCurrentView().TitleBar.LayoutMetricsChanged -= OnTitleMetricsChanged;
                Window.Current.SizeChanged -= OnWindowSizeChanged;
                Window.Current.SetTitleBar(null);
            }
            catch { }
            base.OnNavigatedFrom(e);
        }

        private async Task LoadOriginalAsync()
        {
            BusyRing.IsActive = true;
            try
            {
                _original = await ImageEditService.LoadAsync(_photo.File);
                _ops.Clear();
                _opIndex = 0;
                _pendingMosaic.Clear();
                _stickerDragging = false;
                InkHost.InkPresenter.StrokeContainer.Clear();
                SetMode(EditMode.None);
                ResetAdjust();
                _previewBase = ImageEditService.Downsample(_original.Pixels, _original.Width, _original.Height, 1024);
                _rawCompareBmp = ToWriteableBitmap(_previewBase.Pixels, _previewBase.Width, _previewBase.Height);
                UpdateOpButtons();
                UpdatePreview();
                StatusText.Text = Loc.Format("EditLoaded", _original.Width, _original.Height);
            }
            catch
            {
                StatusText.Text = Loc.Get("EditLoadFailed");
            }
            BusyRing.IsActive = false;
        }

        // ===== 操作栈 =====

        private abstract class EditOp
        {
            public abstract EditableImage Apply(EditableImage img);
        }

        private sealed class CropOp : EditOp
        {
            public Rect Rect;
            public override EditableImage Apply(EditableImage img) => img.CropNormalized(Rect);
        }

        private sealed class RotateOp : EditOp
        {
            public int Degrees;
            public override EditableImage Apply(EditableImage img) => img.Rotate(Degrees);
        }

        private sealed class FlipOp : EditOp
        {
            public bool H, V;
            public override EditableImage Apply(EditableImage img) => img.Flip(H, V);
        }

        private sealed class BorderOp : EditOp
        {
            public double Ratio;
            public Color Color;
            public override EditableImage Apply(EditableImage img) => img.Pad(Ratio, Color.B, Color.G, Color.R);
        }

        private sealed class AdjustOp : EditOp
        {
            public int B, C, S;
            public AdjustFilter F;
            public override EditableImage Apply(EditableImage img)
            {
                var c = img.Clone();
                ImageEditService.ApplyAdjustments(c.Pixels, B, C, S, F);
                if (F == AdjustFilter.Vignette)
                    ImageEditService.ApplyVignette(c.Pixels, c.Width, c.Height, 0.55f);
                return c;
            }
        }

        private sealed class WbOp : EditOp
        {
            public int Temp, Tint;
            public override EditableImage Apply(EditableImage img)
            {
                var c = img.Clone();
                ImageEditService.ApplyWhiteBalance(c.Pixels, Temp, Tint);
                return c;
            }
        }

        private sealed class BlurSharpenOp : EditOp
        {
            public int Blur, Sharpen;
            public override EditableImage Apply(EditableImage img)
            {
                var c = img.Clone();
                if (Blur > 0)
                    ImageEditService.ApplyBoxBlur(c.Pixels, c.Width, c.Height, Math.Max(1, Blur * 4 / 10));
                if (Sharpen > 0)
                    ImageEditService.ApplySharpen(c.Pixels, c.Width, c.Height, Sharpen / 100.0 * 1.5);
                return c;
            }
        }

        private sealed class MosaicStrokeOp : EditOp
        {
            public List<Point> Pts;
            public double Size;
            public override EditableImage Apply(EditableImage img)
            {
                var c = img.Clone();
                ImageEditService.ApplyMosaicStroke(c, Pts, Size);
                return c;
            }
        }

        private sealed class TextOp : EditOp
        {
            public string Text;
            public double X, Y;          // 归一化位置 (中心点)
            public double FontSizeNorm;  // 字号 (相对目标高度)
            public Color Color;
            public override EditableImage Apply(EditableImage img) => img; // 文字在 UI 线程另行合成
        }

        private static EditableImage RenderOpsSync(EditableImage baseImg, List<EditOp> ops, int count, List<Point> pendingMosaic, double pendingSize)
        {
            EditableImage cur = baseImg;
            for (int i = 0; i < count; i++)
            {
                cur = ops[i].Apply(cur);
            }
            if (pendingMosaic != null && pendingMosaic.Count >= 2)
            {
                cur = cur.Clone();
                ImageEditService.ApplyMosaicStroke(cur, pendingMosaic, pendingSize);
            }
            return cur;
        }

        /// <summary>
        /// 计算某个操作之后的文字锚点 (像素坐标): 文字位置是相对它入栈时的画面状态,
        /// 后续几何操作 (裁剪/旋转/翻转/边框) 会改变画布, 锚点必须跟随画面内容。
        /// </summary>
        private static void ComputeTextAnchor(List<EditOp> ops, int count, int idx, double baseW, double baseH,
            double nx, double ny, out double ax, out double ay)
        {
            double w = baseW, h = baseH;
            for (int i = 0; i < idx && i < count; i++) ApplyDimsOnly(ops[i], ref w, ref h);
            double px = nx * w, py = ny * h;
            for (int i = idx + 1; i < count; i++) ApplyPointTransform(ops[i], ref w, ref h, ref px, ref py);
            ax = px;
            ay = py;
        }

        private static void ApplyDimsOnly(EditOp op, ref double w, ref double h)
        {
            if (op is CropOp c)
            {
                w *= Math.Max(0, c.Rect.Width);
                h *= Math.Max(0, c.Rect.Height);
            }
            else if (op is RotateOp r)
            {
                if (((r.Degrees % 360) + 360) % 360 % 180 != 0)
                {
                    double t = w; w = h; h = t;
                }
            }
            else if (op is BorderOp b)
            {
                double p = Math.Max(w, h) * Math.Min(0.5, Math.Max(0, b.Ratio));
                w += 2 * p;
                h += 2 * p;
            }
        }

        private static void ApplyPointTransform(EditOp op, ref double w, ref double h, ref double px, ref double py)
        {
            if (op is CropOp c)
            {
                double rx = Math.Max(0, c.Rect.X), ry = Math.Max(0, c.Rect.Y);
                double rw = Math.Max(0, c.Rect.Width), rh = Math.Max(0, c.Rect.Height);
                px = px - rx * w;
                py = py - ry * h;
                w *= rw;
                h *= rh;
            }
            else if (op is RotateOp r)
            {
                int d = ((r.Degrees % 360) + 360) % 360;
                if (d == 90) { double nx = h - 1 - py, ny = px; px = nx; py = ny; double t = w; w = h; h = t; }
                else if (d == 180) { px = w - 1 - px; py = h - 1 - py; }
                else if (d == 270) { double nx = py, ny = w - 1 - px; px = nx; py = ny; double t = w; w = h; h = t; }
            }
            else if (op is FlipOp f)
            {
                if (f.H) px = w - 1 - px;
                if (f.V) py = h - 1 - py;
            }
            else if (op is BorderOp b)
            {
                double p = Math.Max(w, h) * Math.Min(0.5, Math.Max(0, b.Ratio));
                px += p;
                py += p;
                w += 2 * p;
                h += 2 * p;
            }
        }

        private void PushOp(EditOp op)
        {
            if (_opIndex < _ops.Count) _ops.RemoveRange(_opIndex, _ops.Count - _opIndex);
            _ops.Add(op);
            _opIndex = _ops.Count;
            UpdateOpButtons();
            UpdatePreview();
        }

        private void UpdateOpButtons()
        {
            if (UndoBtn != null) UndoBtn.IsEnabled = _opIndex > 0;
            if (RedoBtn != null) RedoBtn.IsEnabled = _opIndex < _ops.Count;
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_opIndex <= 0) return;
            _opIndex--;
            UpdateOpButtons();
            UpdatePreview();
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            if (_opIndex >= _ops.Count) return;
            _opIndex++;
            UpdateOpButtons();
            UpdatePreview();
        }

        // ===== 模式管理 (裁剪/墨迹/马赛克/文字互斥) =====

        private void SetMode(EditMode m)
        {
            var prev = _mode;
            _mode = m;
            CropToggle.IsChecked = m == EditMode.Crop;
            InkToggle.IsChecked = m == EditMode.Ink;
            MosaicToggle.IsChecked = m == EditMode.Mosaic;
            TextToggle.IsChecked = m == EditMode.Text;
            InkBar.Visibility = m == EditMode.Ink ? Visibility.Visible : Visibility.Collapsed;
            MosaicPanel.Visibility = m == EditMode.Mosaic ? Visibility.Visible : Visibility.Collapsed;
            TextPanel.Visibility = m == EditMode.Text ? Visibility.Visible : Visibility.Collapsed;
            InkHost.IsHitTestVisible = m == EditMode.Ink;
            CropCanvas.IsHitTestVisible = m != EditMode.Ink;
            if (m != EditMode.Crop) ClearCropUi();
            if (m != EditMode.Text)
            {
                if (prev == EditMode.Text && TextSticker.Visibility == Visibility.Visible)
                    CommitStickerText();
                TextSticker.Visibility = Visibility.Collapsed;
                TextBoxMarquee.Visibility = Visibility.Collapsed;
                _stickerDragging = false;
            }
            switch (m)
            {
                case EditMode.Crop:
                    HintText.Visibility = Visibility.Visible;
                    HintText.Text = Loc.Get("EditCropHint");
                    break;
                case EditMode.Mosaic:
                    HintText.Visibility = Visibility.Visible;
                    HintText.Text = Loc.Get("EditMosaicHint");
                    break;
                case EditMode.Text:
                    HintText.Visibility = Visibility.Visible;
                    HintText.Text = Loc.Get("EditTextHint");
                    break;
                default:
                    HintText.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private void CropToggle_Click(object sender, RoutedEventArgs e)
            => SetMode(CropToggle.IsChecked == true ? EditMode.Crop : EditMode.None);

        private void InkToggle_Click(object sender, RoutedEventArgs e)
            => SetMode(InkToggle.IsChecked == true ? EditMode.Ink : EditMode.None);

        private void MosaicToggle_Click(object sender, RoutedEventArgs e)
        {
            SetMode(MosaicToggle.IsChecked == true ? EditMode.Mosaic : EditMode.None);
            _pendingMosaic.Clear();
        }

        private void TextToggle_Click(object sender, RoutedEventArgs e)
            => SetMode(TextToggle.IsChecked == true ? EditMode.Text : EditMode.None);

        // ===== 预览 =====

        private void RenderPixels(byte[] px, int w, int h)
        {
            _previewW = w;
            _previewH = h;
            var wb = ToWriteableBitmap(px, w, h);
            PreviewImage.Source = wb;
            UpdateHostSize();
        }

        private static WriteableBitmap ToWriteableBitmap(byte[] px, int w, int h)
        {
            var wb = new WriteableBitmap(w, h);
            using (var stream = wb.PixelBuffer.AsStream())
            {
                stream.Write(px, 0, px.Length);
            }
            wb.Invalidate();
            return wb;
        }

        private async void UpdatePreview()
        {
            if (_previewBase == null || _comparing) return;
            int gen = ++_renderGen;
            try
            {
                var ink = CaptureInk();
                double hostW = InkHost.ActualWidth;
                double hostH = InkHost.ActualHeight;
                bool hasPending = _mode == EditMode.Mosaic && _pendingMosaic.Count >= 2;
                double mosaicSize = MosaicSizeSlider.Value / 100.0;
                var rendered = await Task.Run(() => RenderOpsSync(_previewBase, _ops, _opIndex,
                    hasPending ? _pendingMosaic : null,
                    mosaicSize));
                if (gen != _renderGen || rendered == null) return;

                var tmp = new byte[rendered.Pixels.Length];
                Array.Copy(rendered.Pixels, tmp, tmp.Length);
                ApplySliderFx(tmp, rendered.Width, rendered.Height);

                for (int i = 0; i < _opIndex; i++)
                {
                    if (!(_ops[i] is TextOp t)) continue;
                    double ax, ay;
                    ComputeTextAnchor(_ops, _opIndex, i, _previewBase.Width, _previewBase.Height,
                        t.X, t.Y, out ax, out ay);
                    var bmp = await RenderTextBitmapAsync(t.Text, t.FontSizeNorm * rendered.Height, t.Color);
                    if (gen != _renderGen) return;
                    PasteText(tmp, rendered.Width, rendered.Height, ax - bmp.W / 2.0, ay - bmp.H / 2.0, bmp);
                }
                if (gen != _renderGen) return;

                if (ink.Count > 0)
                {
                    // 墨迹实时合成到预览 (所见即所得, 与保存结果一致)
                    var inkTarget = new EditableImage(tmp, rendered.Width, rendered.Height, (uint)rendered.Width, (uint)rendered.Height);
                    await Task.Run(() => CompositeInk(inkTarget, ink, hostW, hostH));
                    if (gen != _renderGen) return;
                }

                RenderPixels(tmp, rendered.Width, rendered.Height);
                _lastHisto = ImageEditService.BuildHistogram(tmp, rendered.Width, rendered.Height);
                if (HistoBar.Visibility == Visibility.Visible && EditHistoImage != null)
                    EditHistoImage.Source = _lastHisto;
            }
            catch
            {
            }
        }

        /// <summary>滑块实时预览效果 (不落栈)。</summary>
        private void ApplySliderFx(byte[] px, int w, int h)
        {
            ImageEditService.ApplyAdjustments(
                px,
                (int)BrightnessSlider.Value,
                (int)ContrastSlider.Value,
                (int)SaturationSlider.Value,
                (AdjustFilter)FilterBox.SelectedIndex);
            int t = (int)TempSlider.Value;
            int ti = (int)TintSlider.Value;
            if (t != 0 || ti != 0) ImageEditService.ApplyWhiteBalance(px, t, ti);
            int bl = (int)BlurSlider.Value;
            if (bl > 0) ImageEditService.ApplyBoxBlur(px, w, h, Math.Max(1, bl * 4 / 10));
            int sh = (int)SharpenSlider.Value;
            if (sh > 0) ImageEditService.ApplySharpen(px, w, h, sh / 100.0 * 1.5);
            if ((AdjustFilter)FilterBox.SelectedIndex == AdjustFilter.Vignette)
                ImageEditService.ApplyVignette(px, w, h, 0.55f);
        }

        private void Adjust_Changed(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
            => UpdatePreview();

        private void Wb_Changed(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
            => UpdatePreview();

        private void BlurSharpen_Changed(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
            => UpdatePreview();

        private void Filter_Changed(object sender, SelectionChangedEventArgs e)
            => UpdatePreview();

        private void ResetAdjust()
        {
            if (BrightnessSlider != null) BrightnessSlider.Value = 0;
            if (ContrastSlider != null) ContrastSlider.Value = 0;
            if (SaturationSlider != null) SaturationSlider.Value = 0;
            if (FilterBox != null) FilterBox.SelectedIndex = 0;
            if (TempSlider != null) TempSlider.Value = 0;
            if (TintSlider != null) TintSlider.Value = 0;
            if (BlurSlider != null) BlurSlider.Value = 0;
            if (SharpenSlider != null) SharpenSlider.Value = 0;
        }

        // ===== 几何操作 =====

        private void RotateCw_Click(object sender, RoutedEventArgs e) => PushOp(new RotateOp { Degrees = 90 });
        private void RotateCcw_Click(object sender, RoutedEventArgs e) => PushOp(new RotateOp { Degrees = 270 });
        private void FlipH_Click(object sender, RoutedEventArgs e) => PushOp(new FlipOp { H = true, V = false });
        private void FlipV_Click(object sender, RoutedEventArgs e) => PushOp(new FlipOp { H = false, V = true });

        // ===== 提交型操作 (调色/白平衡/模糊锐化/边框) =====

        private void ApplyAdjust_Click(object sender, RoutedEventArgs e)
        {
            if (_original == null) return;
            var b = (int)BrightnessSlider.Value;
            var c = (int)ContrastSlider.Value;
            var s = (int)SaturationSlider.Value;
            var f = (AdjustFilter)FilterBox.SelectedIndex;
            ResetAdjust();
            PushOp(new AdjustOp { B = b, C = c, S = s, F = f });
            StatusText.Text = Loc.Get("EditAppliedDone");
        }

        private void ApplyWb_Click(object sender, RoutedEventArgs e)
        {
            if (_original == null) return;
            var t = (int)TempSlider.Value;
            var ti = (int)TintSlider.Value;
            ResetAdjust();
            PushOp(new WbOp { Temp = t, Tint = ti });
            StatusText.Text = Loc.Get("EditAppliedDone");
        }

        private void ApplyBlurSharpen_Click(object sender, RoutedEventArgs e)
        {
            if (_original == null) return;
            var bl = (int)BlurSlider.Value;
            var sh = (int)SharpenSlider.Value;
            ResetAdjust();
            PushOp(new BlurSharpenOp { Blur = bl, Sharpen = sh });
            StatusText.Text = Loc.Get("EditAppliedDone");
        }

        private void ApplyBorder_Click(object sender, RoutedEventArgs e)
        {
            if (_original == null) return;
            double ratio = BorderWidthSlider.Value / 100.0;
            var color = ColorFromCombo(BorderColorCombo);
            PushOp(new BorderOp { Ratio = ratio, Color = color });
            StatusText.Text = Loc.Get("EditAppliedDone");
        }

        /// <summary>色板顺序必须与 XAML 中两个 ComboBox 的色块顺序一致。</summary>
        private static readonly Color[] SwatchColors =
        {
            Color.FromArgb(255, 255, 255, 255), // 白
            Color.FromArgb(255, 0, 0, 0),       // 黑
            Color.FromArgb(255, 128, 128, 128), // 灰
            Color.FromArgb(255, 229, 57, 53),   // 红
            Color.FromArgb(255, 30, 136, 229),  // 蓝
            Color.FromArgb(255, 67, 160, 71),   // 绿
            Color.FromArgb(255, 253, 216, 53),  // 黄
            Color.FromArgb(255, 142, 36, 170),  // 紫
        };

        private static Color ColorFromCombo(ComboBox combo)
        {
            int idx = combo.SelectedIndex;
            if (idx >= 0 && idx < SwatchColors.Length) return SwatchColors[idx];
            return Colors.White;
        }

        // ===== 文字水印 (图片上直接编辑的贴纸) =====

        private void StickerBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            CommitStickerText();
        }

        private void StickerBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (StickerBox.Text.Length == 0) return;
            double w = Math.Min(500, Math.Max(_stickerDragWidth, MeasureTextWidth(StickerBox.Text, StickerBox.FontSize) + 20));
            if (Math.Abs(StickerBox.Width - w) > 1)
            {
                StickerBox.Width = w;
                PositionSticker();
            }
        }

        private void ShowStickerAtRect(double x, double y, double w, double h)
        {
            _stickerDragWidth = Math.Max(60, Math.Min(500, w));
            _stickerNorm = new Point(
                Math.Max(0, Math.Min(1, (x + w / 2.0) / Math.Max(1, ImageHost.Width))),
                Math.Max(0, Math.Min(1, (y + h / 2.0) / Math.Max(1, ImageHost.Height))));
            UpdateStickerStyle();
            StickerBox.Width = _stickerDragWidth;
            TextSticker.Visibility = Visibility.Visible;
            PositionSticker();
            StickerBox.Focus(FocusState.Programmatic);
        }

        private void CommitStickerText()
        {
            string text = StickerBox.Text.Trim();
            TextSticker.Visibility = Visibility.Collapsed;
            TextBoxMarquee.Visibility = Visibility.Collapsed;
            _stickerDragging = false;
            if (text.Length == 0) return;
            double fontNorm = TextSizeSlider.Value / _previewBase.Height;
            PushOp(new TextOp
            {
                Text = text,
                X = _stickerNorm.X,
                Y = _stickerNorm.Y,
                FontSizeNorm = fontNorm,
                Color = ColorFromCombo(TextColorCombo),
            });
            StickerBox.Text = "";
            StatusText.Text = Loc.Get("EditAppliedDone");
        }

        private static double MeasureTextWidth(string text, double fontSize)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = Math.Max(8, fontSize),
                FontFamily = new FontFamily("Segoe UI"),
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return tb.DesiredSize.Width;
        }

        private void UpdateStickerStyle()
        {
            if (StickerBox == null || _previewW < 1) return;
            double hostScale = ImageHost.Width / Math.Max(1, _previewW);
            double devScale = DevScaleOf(StickerBox);
            StickerBox.FontSize = Math.Max(8, TextSizeSlider.Value * hostScale / Math.Max(1.0, devScale));
            StickerBox.Foreground = new SolidColorBrush(ColorFromCombo(TextColorCombo));
        }

        private void PositionSticker()
        {
            if (ImageHost.Width < 1 || ImageHost.Height < 1) return;
            double w = TextSticker.ActualWidth > 0 ? TextSticker.ActualWidth : 80;
            double h = TextSticker.ActualHeight > 0 ? TextSticker.ActualHeight : 40;
            Canvas.SetLeft(TextSticker, _stickerNorm.X * ImageHost.Width - w / 2.0);
            Canvas.SetTop(TextSticker, _stickerNorm.Y * ImageHost.Height - h / 2.0);
        }

        private void StickerGrip_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _stickerDragging = true;
            _gripStart = e.GetCurrentPoint(StickerHost).Position;
            StickerGrip.CapturePointer(e.Pointer);
        }

        private void StickerGrip_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_stickerDragging) return;
            var pos = e.GetCurrentPoint(StickerHost).Position;
            double dx = (pos.X - _gripStart.X) / Math.Max(1, ImageHost.Width);
            double dy = (pos.Y - _gripStart.Y) / Math.Max(1, ImageHost.Height);
            _stickerNorm.X = Math.Max(0, Math.Min(1, _stickerNorm.X + dx));
            _stickerNorm.Y = Math.Max(0, Math.Min(1, _stickerNorm.Y + dy));
            _gripStart = pos;
            PositionSticker();
        }

        private void StickerGrip_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _stickerDragging = false;
        }

        private void TextSize_Changed(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            UpdateStickerStyle();
            if (StickerBox != null && StickerBox.Text.Length > 0)
            {
                double w = Math.Min(500, Math.Max(_stickerDragWidth, MeasureTextWidth(StickerBox.Text, StickerBox.FontSize) + 20));
                StickerBox.Width = w;
            }
        }

        private void TextColor_Changed(object sender, SelectionChangedEventArgs e)
            => UpdateStickerStyle();

        private sealed class TextBitmap
        {
            public byte[] Px;
            public int W, H;
        }

        private async Task<TextBitmap> RenderTextBitmapAsync(string text, double fontSizePx, Color color)
        {
            var tb = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(color),
                FontFamily = new FontFamily("Segoe UI"),
                IsHitTestVisible = false,
            };
            double scale = DevScaleOf(tb);
            tb.FontSize = Math.Max(8, fontSizePx / Math.Max(1.0, scale));
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            tb.Arrange(new Rect(0, 0, tb.DesiredSize.Width, tb.DesiredSize.Height));
            TextRenderHost.Children.Add(tb);
            try
            {
                var rtb = new RenderTargetBitmap();
                await rtb.RenderAsync(tb);
                return new TextBitmap { Px = (await rtb.GetPixelsAsync()).ToArray(), W = (int)rtb.PixelWidth, H = (int)rtb.PixelHeight };
            }
            finally
            {
                TextRenderHost.Children.Remove(tb);
            }
        }

        private static void PasteText(byte[] px, int w, int h, double cx, double cy, TextBitmap bmp)
        {
            int ix = (int)Math.Round(cx - bmp.W / 2.0);
            int iy = (int)Math.Round(cy - bmp.H / 2.0);
            for (int y = 0; y < bmp.H; y++)
            {
                for (int x = 0; x < bmp.W; x++)
                {
                    int sx = ix + x, sy = iy + y;
                    if (sx < 0 || sy < 0 || sx >= w || sy >= h) continue;
                    int si = (y * bmp.W + x) * 4;
                    int a = bmp.Px[si + 3];
                    if (a == 0) continue;
                    double f = a / 255.0;
                    int di = (sy * w + sx) * 4;
                    px[di] = (byte)(px[di] * (1 - f) + bmp.Px[si] * f);
                    px[di + 1] = (byte)(px[di + 1] * (1 - f) + bmp.Px[si + 1] * f);
                    px[di + 2] = (byte)(px[di + 2] * (1 - f) + bmp.Px[si + 2] * f);
                }
            }
        }

        // ===== 对比 (按住显示原图) =====

        private void Compare_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_previewBase == null || _rawCompareBmp == null) return;
            _comparing = true;
            ++_renderGen; // 作废任何在途预览渲染, 防止其覆盖原图
            PreviewImage.Source = _rawCompareBmp;
            _previewW = _previewBase.Width;
            _previewH = _previewBase.Height;
            UpdateHostSize();
            CompareLabel.Visibility = Visibility.Visible;
        }

        private void Compare_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _comparing = false;
            CompareLabel.Visibility = Visibility.Collapsed;
            UpdatePreview();
        }

        // ===== 直方图 =====

        private void HistoToggle_Click(object sender, RoutedEventArgs e)
        {
            bool on = HistoToggle.IsChecked == true;
            HistoBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on && _lastHisto != null) EditHistoImage.Source = _lastHisto;
        }

        // ===== 裁剪 / 马赛克 / 文字 指针交互 =====

        private void CropCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var pos = e.GetCurrentPoint(CropCanvas).Position;
            switch (_mode)
            {
                case EditMode.Crop:
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
                    break;
                case EditMode.Mosaic:
                    _pendingMosaic.Add(new Point(
                        Math.Max(0, Math.Min(1, pos.X / Math.Max(1, CropCanvas.ActualWidth))),
                        Math.Max(0, Math.Min(1, pos.Y / Math.Max(1, CropCanvas.ActualHeight)))));
                    CropCanvas.CapturePointer(e.Pointer);
                    UpdatePreview();
                    break;
                case EditMode.Text:
                    if (TextSticker.Visibility == Visibility.Visible)
                        CommitStickerText(); // 点击框外结束输入
                    _textDragging = true;
                    _textDragStart = pos;
                    CropCanvas.CapturePointer(e.Pointer);
                    break;
            }
        }

        private void CropCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_mode == EditMode.Text)
            {
                if (!_textDragging) return;
                var p = e.GetCurrentPoint(CropCanvas).Position;
                p.X = Math.Max(0, Math.Min(p.X, CropCanvas.ActualWidth));
                p.Y = Math.Max(0, Math.Min(p.Y, CropCanvas.ActualHeight));
                double x = Math.Min(_textDragStart.X, p.X);
                double y = Math.Min(_textDragStart.Y, p.Y);
                double w = Math.Abs(p.X - _textDragStart.X);
                double h = Math.Abs(p.Y - _textDragStart.Y);
                if (w < 2 || h < 2)
                {
                    TextBoxMarquee.Visibility = Visibility.Collapsed;
                    return;
                }
                TextBoxMarquee.Visibility = Visibility.Visible;
                TextBoxMarquee.Width = w;
                TextBoxMarquee.Height = h;
                Canvas.SetLeft(TextBoxMarquee, x);
                Canvas.SetTop(TextBoxMarquee, y);
                return;
            }
            if (_mode == EditMode.Mosaic)
            {
                var pos = e.GetCurrentPoint(CropCanvas).Position;
                var last = _pendingMosaic.Count > 0 ? _pendingMosaic[_pendingMosaic.Count - 1] : new Point(-1, -1);
                var cur = new Point(
                    Math.Max(0, Math.Min(1, pos.X / Math.Max(1, CropCanvas.ActualWidth))),
                    Math.Max(0, Math.Min(1, pos.Y / Math.Max(1, CropCanvas.ActualHeight))));
                if (Math.Abs(cur.X - last.X) + Math.Abs(cur.Y - last.Y) > 0.004)
                {
                    _pendingMosaic.Add(cur);
                    UpdatePreview();
                }
                return;
            }
            if (!_dragging || _mode != EditMode.Crop) return;
            var cpos = e.GetCurrentPoint(CropCanvas).Position;
            cpos.X = Math.Max(0, Math.Min(cpos.X, CropCanvas.ActualWidth));
            cpos.Y = Math.Max(0, Math.Min(cpos.Y, CropCanvas.ActualHeight));

            if (_dragMove)
            {
                double dx = cpos.X - _dragStart.X;
                double dy = cpos.Y - _dragStart.Y;
                _cropRect.X += dx;
                _cropRect.Y += dy;
                if (_cropRect.X < 0) _cropRect.X = 0;
                if (_cropRect.Y < 0) _cropRect.Y = 0;
                if (_cropRect.Right > CropCanvas.ActualWidth)
                    _cropRect.X = Math.Max(0, CropCanvas.ActualWidth - _cropRect.Width);
                if (_cropRect.Bottom > CropCanvas.ActualHeight)
                    _cropRect.Y = Math.Max(0, CropCanvas.ActualHeight - _cropRect.Height);
                _dragStart = cpos;
            }
            else
            {
                double x = Math.Min(_dragStart.X, cpos.X);
                double y = Math.Min(_dragStart.Y, cpos.Y);
                _cropRect = new Rect(x, y, Math.Abs(cpos.X - _dragStart.X), Math.Abs(cpos.Y - _dragStart.Y));
            }
            UpdateCropVisuals();
        }

        private void CropCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_mode == EditMode.Text)
            {
                _textDragging = false;
                TextBoxMarquee.Visibility = Visibility.Collapsed;
                var pos = e.GetCurrentPoint(CropCanvas).Position;
                double w = Math.Abs(pos.X - _textDragStart.X);
                double h = Math.Abs(pos.Y - _textDragStart.Y);
                if (w < 12 || h < 12) return; // 单击框外只结束输入,不新建
                double x = Math.Min(_textDragStart.X, pos.X);
                double y = Math.Min(_textDragStart.Y, pos.Y);
                ShowStickerAtRect(x, y, w, h);
                return;
            }
            if (_mode == EditMode.Mosaic)
            {
                if (_pendingMosaic.Count >= 2)
                {
                    var size = MosaicSizeSlider.Value / 100.0;
                    var pts = new List<Point>(_pendingMosaic);
                    _pendingMosaic.Clear();
                    PushOp(new MosaicStrokeOp { Pts = pts, Size = size });
                }
                return;
            }
            _dragging = false;
            if (_cropRect.Width < 8 || _cropRect.Height < 8) ClearCropUi();
        }

        private void ClearCropUi()
        {
            _cropRect = default(Rect);
            CropRectVisual.Visibility = Visibility.Collapsed;
            DimPath.Visibility = Visibility.Collapsed;
            _dragging = false;
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

        // ===== 墨迹 (ink) =====

        private void InkUndo_Click(object sender, RoutedEventArgs e)
        {
            var container = InkHost.InkPresenter.StrokeContainer;
            var strokes = container.GetStrokes();
            if (strokes.Count == 0) return;
            var last = strokes[strokes.Count - 1];
            var r = last.BoundingRect;
            container.SelectWithPolyLine(new List<Point>
            {
                new Point(r.Left, r.Top),
                new Point(r.Right, r.Top),
                new Point(r.Right, r.Bottom),
                new Point(r.Left, r.Bottom)
            });
            container.DeleteSelected();
        }

        private void InkClear_Click(object sender, RoutedEventArgs e)
        {
            InkHost.InkPresenter.StrokeContainer.Clear();
        }

        /// <summary>墨迹笔画数据快照 (可跨线程)。</summary>
        private sealed class InkStrokeData
        {
            public List<Point> Pts = new List<Point>();
            public Color Color;
            public double SizeW;
            public byte Opacity;
        }

        /// <summary>在 UI 线程抓取墨迹笔画数据 (InkPresenter 只能在 UI 线程访问)。</summary>
        private List<InkStrokeData> CaptureInk()
        {
            var result = new List<InkStrokeData>();
            if (InkHost == null) return result;
            try
            {
                var strokes = InkHost.InkPresenter.StrokeContainer.GetStrokes();
                foreach (var st in strokes)
                {
                    var pts = st.GetInkPoints();
                    if (pts.Count == 0) continue;
                    var attrs = st.DrawingAttributes;
                    byte a = 255;
                    if (UwpCompat.HasContractV2
                        && attrs.Kind == InkDrawingAttributesKind.Pencil
                        && attrs.PencilProperties != null)
                    {
                        a = (byte)Math.Round(255 * Math.Max(0.0, Math.Min(1.0, attrs.PencilProperties.Opacity)));
                    }
                    var d = new InkStrokeData { Color = attrs.Color, SizeW = attrs.Size.Width, Opacity = a };
                    foreach (var p in pts) d.Pts.Add(p.Position);
                    result.Add(d);
                }
            }
            catch { }
            return result;
        }

        /// <summary>把墨迹笔画按比例合成到目标像素缓冲 (BGRA8)。仅操作纯数据, 可后台执行。</summary>
        private void CompositeInk(EditableImage target, List<InkStrokeData> strokes, double hostW, double hostH)
        {
            if (target == null || strokes == null || strokes.Count == 0) return;
            double sx = target.Width / Math.Max(1, hostW);
            double sy = target.Height / Math.Max(1, hostH);
            foreach (var st in strokes)
            {
                var pts = st.Pts;
                var color = st.Color;
                byte a = st.Opacity;
                for (int i = 0; i < pts.Count; i++)
                {
                    double px = pts[i].X * sx;
                    double py = pts[i].Y * sy;
                    double r = Math.Max(1, st.SizeW * 0.5 * (sx + sy) / 2);
                    if (i == 0)
                    {
                        StampCircle(target, px, py, r, color, a);
                    }
                    else
                    {
                        double fx = pts[i - 1].X * sx;
                        double fy = pts[i - 1].Y * sy;
                        DrawThickSegment(target, fx, fy, px, py, r, color, a);
                    }
                }
            }
        }

        private void DrawThickSegment(EditableImage target, double x0, double y0, double x1, double y1, double r, Color color, byte a)
        {
            double dx = x1 - x0, dy = y1 - y0;
            double len = Math.Sqrt(dx * dx + dy * dy);
            int steps = Math.Max(1, (int)Math.Ceiling(len));
            for (int s = 0; s <= steps; s++)
            {
                double t = s / (double)steps;
                StampCircle(target, x0 + dx * t, y0 + dy * t, r, color, a);
            }
        }

        private void StampCircle(EditableImage target, double cx, double cy, double r, Color color, byte a)
        {
            int x0 = (int)Math.Floor(cx - r), x1 = (int)Math.Ceiling(cx + r);
            int y0 = (int)Math.Floor(cy - r), y1 = (int)Math.Ceiling(cy + r);
            double r2 = r * r;
            double alphaN = a / 255.0;
            var px = target.Pixels;
            int w = target.Width, h = target.Height;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    double dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy > r2) continue;
                    int idx = (y * w + x) * 4;
                    px[idx] = (byte)(px[idx] * (1 - alphaN) + color.B * alphaN);
                    px[idx + 1] = (byte)(px[idx + 1] * (1 - alphaN) + color.G * alphaN);
                    px[idx + 2] = (byte)(px[idx + 2] * (1 - alphaN) + color.R * alphaN);
                }
            }
        }

        // ===== 裁剪提交 =====

        private void ApplyCrop_Click(object sender, RoutedEventArgs e)
        {
            if (_mode != EditMode.Crop || _cropRect.IsEmpty || CropCanvas.ActualWidth < 1) return;
            var norm = new Rect(
                Math.Max(0, Math.Min(1, _cropRect.X / CropCanvas.ActualWidth)),
                Math.Max(0, Math.Min(1, _cropRect.Y / CropCanvas.ActualHeight)),
                Math.Max(0, Math.Min(1, _cropRect.Width / CropCanvas.ActualWidth)),
                Math.Max(0, Math.Min(1, _cropRect.Height / CropCanvas.ActualHeight)));
            if (norm.Width < 0.01 || norm.Height < 0.01) return;
            ClearCropUi();
            PushOp(new CropOp { Rect = norm });
            StatusText.Text = Loc.Get("EditCroppedDone");
        }

        private async void Reset_Click(object sender, RoutedEventArgs e)
        {
            ClearCropUi();
            await LoadOriginalAsync();
        }

        private void UpdateHostSize()
        {
            if (_previewW < 1 || _previewH < 1 || HostSizer.ActualWidth < 1 || HostSizer.ActualHeight < 1) return;
            double scale = Math.Min(HostSizer.ActualWidth / _previewW, HostSizer.ActualHeight / _previewH);
            ImageHost.Width = Math.Max(1, _previewW * scale);
            ImageHost.Height = Math.Max(1, _previewH * scale);
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_original == null || _photo == null) return;
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
            double quality;
            switch (QualityCombo.SelectedIndex)
            {
                case 1: quality = 0.8; break;
                case 2: quality = 0.6; break;
                default: quality = 0.95; break;
            }
            BusyRing.IsActive = true;
            StatusText.Text = Loc.Get("EditProcessing");
            try
            {
                var ink = CaptureInk();
                double hostW = InkHost.ActualWidth;
                double hostH = InkHost.ActualHeight;
                var rendered = await Task.Run(() => RenderOpsSync(_original, _ops, _opIndex, null, 0));
                for (int i = 0; i < _opIndex; i++)
                {
                    if (!(_ops[i] is TextOp t)) continue;
                    double ax, ay;
                    ComputeTextAnchor(_ops, _opIndex, i, _original.Width, _original.Height,
                        t.X, t.Y, out ax, out ay);
                    var bmp = await RenderTextBitmapAsync(t.Text, t.FontSizeNorm * rendered.Height, t.Color);
                    PasteText(rendered.Pixels, rendered.Width, rendered.Height, ax - bmp.W / 2.0, ay - bmp.H / 2.0, bmp);
                }
                await Task.Run(() => CompositeInk(rendered, ink, hostW, hostH));
                await ImageEditService.SaveAsync(file, rendered.Pixels, rendered.Width, rendered.Height, encoderId, quality);
                StatusText.Text = Loc.Format("EditSaved", file.Name);
            }
            catch
            {
                StatusText.Text = Loc.Get("EditSaveFailed");
            }
            BusyRing.IsActive = false;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
