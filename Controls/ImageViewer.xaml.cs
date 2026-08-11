using System;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace HyperViewer.Controls
{
    /// <summary>
    /// 支持鼠标滚轮缩放、双指缩放、拖动平移、旋转/翻转的图片查看控件。
    /// 依赖 ScrollViewer 内置 ZoomMode 实现真正的渲染级缩放（性能最佳）。
    /// </summary>
    public sealed partial class ImageViewer : UserControl
    {
        // 双击切换缩放的目标值 (1.0 ↔ 2.0)
        private const float DoubleTapZoomTarget = 2.0f;

        // 滚轮缩放每次放大缩小的倍率因子
        private const float WheelZoomStep = 1.15f;

        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register(
                nameof(Source),
                typeof(BitmapImage),
                typeof(ImageViewer),
                new PropertyMetadata(null, OnSourceChanged));

        public static readonly DependencyProperty ImageRotationProperty =
            DependencyProperty.Register(
                nameof(ImageRotation),
                typeof(double),
                typeof(ImageViewer),
                new PropertyMetadata(0.0, OnTransformChanged));

        public static readonly DependencyProperty FlipHProperty =
            DependencyProperty.Register(
                nameof(FlipH),
                typeof(int),
                typeof(ImageViewer),
                new PropertyMetadata(1, OnTransformChanged));

        public static readonly DependencyProperty FlipVProperty =
            DependencyProperty.Register(
                nameof(FlipV),
                typeof(int),
                typeof(ImageViewer),
                new PropertyMetadata(1, OnTransformChanged));

        public BitmapImage Source
        {
            get => (BitmapImage)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public double ImageRotation
        {
            get => (double)GetValue(ImageRotationProperty);
            set => SetValue(ImageRotationProperty, value);
        }

        public int FlipH
        {
            get => (int)GetValue(FlipHProperty);
            set => SetValue(FlipHProperty, value);
        }

        public int FlipV
        {
            get => (int)GetValue(FlipVProperty);
            set => SetValue(FlipVProperty, value);
        }

        public event EventHandler ZoomFactorChanged;

        private bool _suppressViewChange;
        private readonly CompositeTransform _imageTransform = new CompositeTransform();

        public ImageViewer()
        {
            InitializeComponent();
            TheImage.RenderTransform = _imageTransform;
            TheImage.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageViewer v)
            {
                v.TheImage.Source = e.NewValue as BitmapImage;
                v.ResetView();
            }
        }

        private static void OnTransformChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageViewer v) v.ApplyTransform();
        }

        private void ApplyTransform()
        {
            _imageTransform.Rotation = ImageRotation;
            _imageTransform.ScaleX = FlipH;
            _imageTransform.ScaleY = FlipV;
        }

        private void Scroller_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            var p = e.GetPosition(Scroller);
            ToggleZoom(p);
            e.Handled = true;
        }

        private void Scroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            // 直接滚轮 = 缩放；按住 Ctrl 滚轮 = 滚动 (ScrollViewer 默认行为)
            var ctrl = Windows.UI.Core.CoreVirtualKeyStates.Down ==
                       Window.Current.CoreWindow.GetKeyState(VirtualKey.Control);
            if (ctrl)
            {
                // 走默认滚动
                return;
            }

            var delta = e.GetCurrentPoint(Scroller).Properties.MouseWheelDelta;
            var center = e.GetCurrentPoint(Scroller).Position;
            ZoomAt(center, delta > 0 ? WheelZoomStep : 1f / WheelZoomStep);
            e.Handled = true;
        }

        private void ToggleZoom(Point center)
        {
            var current = Scroller.ZoomFactor;
            var target = (current > 0.999 && current < 1.001) ? DoubleTapZoomTarget : 1.0f;
            ZoomTo(target, center);
        }

        private void ZoomTo(float target, Point center)
        {
            var offsetX = center.X * target - Math.Max(0, (Scroller.ViewportWidth - TheImage.ActualWidth * target) / 2);
            var offsetY = center.Y * target - Math.Max(0, (Scroller.ViewportHeight - TheImage.ActualHeight * target) / 2);
            _suppressViewChange = true;
            Scroller.ChangeView(offsetX, offsetY, target);
            _suppressViewChange = false;
        }

        public void ZoomAt(Point center, float delta)
        {
            var current = Scroller.ZoomFactor;
            var newZoom = Math.Max(Scroller.MinZoomFactor,
                                  Math.Min(Scroller.MaxZoomFactor, current * delta));
            var offsetX = (center.X * newZoom) - (Scroller.ViewportWidth / 2);
            var offsetY = (center.Y * newZoom) - (Scroller.ViewportHeight / 2);
            _suppressViewChange = true;
            Scroller.ChangeView(Math.Max(0, offsetX), Math.Max(0, offsetY), newZoom);
            _suppressViewChange = false;
            ZoomFactorChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Scroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (!_suppressViewChange && !e.IsIntermediate)
            {
                ZoomFactorChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void ResetView()
        {
            _suppressViewChange = true;
            Scroller.ChangeView(null, null, 1.0f, true);
            _suppressViewChange = false;
        }

        public float CurrentZoomFactor => Scroller.ZoomFactor;
    }
}
