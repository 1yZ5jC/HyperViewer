using System;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
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

        /// <summary>
        /// 低清占位图显示中 (MainViewModel 显示邻居缓存 quick 时置 true)。
        /// 占位期间不隐藏、不拟合、保持当前 zoom 直接显示, 高清图到达后再走正常流程,
        /// 避免"低清 fit 放大 -> 高清 fit 缩小"的两次缩放跳变。
        /// </summary>
        public static readonly DependencyProperty IsPlaceholderProperty =
            DependencyProperty.Register(
                nameof(IsPlaceholder),
                typeof(bool),
                typeof(ImageViewer),
                new PropertyMetadata(false));

        public bool IsPlaceholder
        {
            get => (bool)GetValue(IsPlaceholderProperty);
            set => SetValue(IsPlaceholderProperty, value);
        }

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

        /// <summary>图片源变化时触发 (用于换图淡入)。</summary>
        public event EventHandler ImageChanged;

        /// <summary>双击触发 (透传给页面, 页面用它区分单击/双击)。</summary>
        public event EventHandler DoubleTappedOccurred;

        private bool _suppressViewChange;
        private float _lastFitZoom = -1f;
        // 渲染当前旋转角度 (动画目标): Stop 旋转动画后 CompositeTransform.Rotation
        // 会回落基值 (通常 0), 新动画若不带 From 就会从 0° 起步, 连续旋转表现为
        // "以 0° 为基准倒转"; 用跟踪值保证动画始终从当前状态顺时针转
        private double _currentRotation;
        private readonly CompositeTransform _imageTransform = new CompositeTransform();
        private readonly ScaleTransform _transitionScale = new ScaleTransform();
        private readonly TranslateTransform _transitionTranslate = new TranslateTransform();
        private TransformGroup _transitionGroup;
        private Storyboard _fadeStoryboard;
        private Storyboard _rotationStoryboard;

        public ImageViewer()
        {
            InitializeComponent();
            _transitionGroup = new TransformGroup();
            _transitionGroup.Children.Add(_imageTransform);
            _transitionGroup.Children.Add(_transitionScale);
            _transitionGroup.Children.Add(_transitionTranslate);
            TheImage.RenderTransform = _transitionGroup;
            TheImage.RenderTransformOrigin = new Point(0.5, 0.5);
            _fitRetryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _fitRetryTimer.Tick += OnFitRetryTick;
            // 淡入兜底: ChangeView 动画完成事件 (ViewChanged) 断链时也要淡入
            _fadeInTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _fadeInTimer.Tick += OnFadeInTimerTick;
        }

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageViewer v)
            {
                // 换图: 立即停掉旧旋转动画并归位 (防止 360->0 之类倒转), 旋转从新图的角度开始
                v._rotationStoryboard?.Stop();
                v._imageTransform.Rotation = v.ImageRotation;
                v._currentRotation = v.ImageRotation;
                v.TheImage.Source = e.NewValue as BitmapImage;
                if (v.IsPlaceholder)
                {
                    // 低清占位图: 不隐藏、正常做 fit —— 低清与高清是同一张图,
                    // 布局比例相同, 按各自布局算出的 fit 视觉尺寸一致
                    // (低清 1024x603: fit=1.489 -> 视觉 1515 宽; 高清 2248x1323: fit=0.674 -> 视觉 1515 宽),
                    // 因此高清到达时替换无尺寸跳变, 只是清晰化, 不再"先缩略图后原图"晃眼。
                    // 不做淡入 (未隐藏无需淡入), 高清到达后走正常"隐藏 -> fit -> 淡入"。
                    v._pendingFit = true;
                    v._pendingFadeIn = false;
                    v._fadeInTimer.Stop();
                    v._lastFitZoom = -1f;
                    v.ImageChanged?.Invoke(v, EventArgs.Empty);
                    Helpers.DebugLog.Write("IMG", $"SourceChanged (placeholder) new={(e.NewValue as BitmapImage)?.PixelWidth}x{(e.NewValue as BitmapImage)?.PixelHeight} fit-for-stable");
                    // 兜底: 事件断链时也要 fit (与正常路径一致)
                    v._fitRetryTimer.Stop();
                    v._fitRetryTimer.Start();
                    return;
                }
                // 14393+: 不隐藏、不淡入。低清与高清 fit 的视觉尺寸一致 (低清 1024x603
                // fit 1.489 视觉 1515 宽 = 高清 2248x1323 fit 0.674 视觉 1515 宽),
                // 换 Source (布局变化) 与无动画 fit 同帧更新, 每一帧视觉尺寸都是 1515,
                // 只有清晰度渐变 —— 无闪、缩放比例必然正确。
                // 10240: 三参 ChangeView 无法禁用动画, 必须隐藏 + 动画后淡入,
                // 否则会看到"高清图以旧 zoom 显示再缩放回 fit"的可见动画。
                if (!Helpers.UwpCompat.HasContractV2)
                {
                    // 换图瞬间隐藏: 防止旧 zoom (如放大 150%) 残留, 新图以放大态
                    // 短暂显示再缩回 fit ("先放大后缩小"跳动); 布局稳定后 fit 完成才淡入
                    v.TheImage.Opacity = 0;
                    v._pendingFadeIn = false;
                    v._fadeInTimer.Stop();
                }
                v._pendingFit = true;
                v._lastFitZoom = -1f;
                v.ImageChanged?.Invoke(v, EventArgs.Empty);
                Helpers.DebugLog.Write("IMG", $"SourceChanged new={(e.NewValue as BitmapImage)?.PixelWidth}x{(e.NewValue as BitmapImage)?.PixelHeight} opacity={(Helpers.UwpCompat.HasContractV2 ? "keep" : "0")} pendingFit=true");
                // 兜底: 事件 (ImageOpened/SizeChanged) 可能断链 (10240 上同一实例
                // 重复赋值不再触发 ImageOpened), 定时重试直到适应成功并淡入
                v._fitRetryTimer.Stop();
                v._fitRetryTimer.Start();
            }
        }

        // 10240 兼容: SetSourceAsync 返回时 BitmapImage 可能尚未解码完成
        // (PixelWidth==0), 此时若用 TheImage.ActualWidth (旧图遗留尺寸) 计算
        // 适应缩放会得到错误倍数, 解码完成后才跳变 -> 视觉抖动。
        // 因此延迟到解码完成 (ImageOpened / SizeChanged) 后再适应 + 淡入。
        private bool _pendingFit;
        private DispatcherTimer _fitRetryTimer;
        // 淡入延迟到 fit 动画完成后 (ViewChanged IsIntermediate=false): 
        // 图片可见时缩放动画已结束, 不会出现"淡入过程中缩放乱跳"
        private bool _pendingFadeIn;
        private DispatcherTimer _fadeInTimer;

        private void TheImage_ImageOpened(object sender, RoutedEventArgs e)
        {
            Helpers.DebugLog.Write("IMG", $"ImageOpened px={(TheImage.Source as BitmapImage)?.PixelWidth}x{(TheImage.Source as BitmapImage)?.PixelHeight} act={TheImage.ActualWidth:0.0}x{TheImage.ActualHeight:0.0}");
            // 不在此处 fit/淡入: 解码完成但布局 (Extent) 尚未更新, 此刻启动的
            // ChangeView 动画会被随后的布局变化中断, 造成"属性已是 fit 但
            // 渲染停在中间值"的外显不一致; 等 SizeChanged (布局完成) 再 fit
            _pendingFit = true;
        }

        private void TheImage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Helpers.DebugLog.Write("IMG", $"SizeChanged {e.NewSize.Width:0.0}x{e.NewSize.Height:0.0} pendingFit={_pendingFit}");
            if (_pendingFit && TryFit())
            {
                _pendingFit = false;
                if (!Helpers.UwpCompat.HasContractV2) RequestFadeIn();
            }
        }

        private void OnFitRetryTick(object sender, object e)
        {
            if (!_pendingFit)
            {
                _fitRetryTimer.Stop();
                return;
            }
            Helpers.DebugLog.Write("IMG", "FitRetryTick");
            if (TryFit())
            {
                _pendingFit = false;
                _fitRetryTimer.Stop();
                if (!Helpers.UwpCompat.HasContractV2) RequestFadeIn();
            }
        }

        // 请求淡入: 等 fit 动画完成 (ViewChanged 最终态) 后执行, 定时器兜底
        private void RequestFadeIn()
        {
            Helpers.DebugLog.Write("IMG", "RequestFadeIn (wait fit anim end)");
            _pendingFadeIn = true;
            _fadeInTimer.Stop();
            _fadeInTimer.Start();
        }

        private void OnFadeInTimerTick(object sender, object e)
        {
            _fadeInTimer.Stop();
            if (_pendingFadeIn)
            {
                _pendingFadeIn = false;
                Helpers.DebugLog.Write("IMG", "FadeIn (timer fallback)");
                FadeIn();
            }
        }

        // 尺寸有效才真正执行适应: 换图瞬间旧图事件可能晚到 (此时新图
        // PixelWidth==0), 误清 _pendingFit 会让新图解码完成后不再适应;
        // 失败时保持挂起, 等新图自己的事件
        private bool TryFit()
        {
            if (!(TheImage.Source is BitmapImage bmp)
                || bmp.PixelWidth <= 0
                || bmp.PixelHeight <= 0)
            {
                Helpers.DebugLog.Write("IMG", $"TryFit deferred (px invalid: {(TheImage.Source as BitmapImage)?.PixelWidth}x{(TheImage.Source as BitmapImage)?.PixelHeight})");
                return false;
            }
            FitToWindow();
            return true;
        }

        /// <summary>
        /// 换图过渡动画 (按设置选择: Fade / Zoom / Pan / Flicker)。
        /// 先把透明度归零, 图片解码完成后执行。
        /// </summary>
        public void FadeIn(string transition = null)
        {
            var kind = string.IsNullOrEmpty(transition) ? Helpers.SettingsService.SlideTransition : transition;
            // 不显式归零透明度: 动画 From=0 自带淡入起点;
            // 若动画因任何原因未执行, 图片保持可见而不是黑屏
            _fadeStoryboard?.Stop();
            var sb = new Storyboard();
            var add = new Action<string, double, double, double>((prop, from, to, ms) =>
            {
                var anim = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(ms) };
                Storyboard.SetTarget(anim, TheImage);
                Storyboard.SetTargetProperty(anim, prop);
                sb.Children.Add(anim);
            });

            switch (kind)
            {
                case "Zoom":
                    _transitionScale.ScaleX = _transitionScale.ScaleY = 1.08;
                    add("Opacity", 0, 1, 220);
                    sb.Children.Add(CreateAnim("ScaleX", 1.08, 1, 220, _transitionScale));
                    sb.Children.Add(CreateAnim("ScaleY", 1.08, 1, 220, _transitionScale));
                    break;
                case "Pan":
                    _transitionTranslate.X = 48;
                    add("Opacity", 0, 1, 220);
                    sb.Children.Add(CreateAnim("X", 48, 0, 220, _transitionTranslate));
                    break;
                case "Flicker":
                    _transitionScale.ScaleX = _transitionScale.ScaleY = 1.02;
                    add("Opacity", 0, 1, 80);
                    sb.Children.Add(CreateOpacityAnim(80, 0.3, 55));
                    sb.Children.Add(CreateOpacityAnim(135, 1, 100));
                    sb.Children.Add(CreateAnim("ScaleX", 1.02, 1, 235, _transitionScale));
                    sb.Children.Add(CreateAnim("ScaleY", 1.02, 1, 235, _transitionScale));
                    break;
                default: // Fade
                    add("Opacity", 0, 1, 160);
                    break;
            }
            _fadeStoryboard = sb;
            _fadeStoryboard.Begin();
        }

        private static DoubleAnimation CreateAnim(string prop, double from, double to, double ms, DependencyObject target)
        {
            var anim = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(ms) };
            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, prop);
            return anim;
        }

        private DoubleAnimation CreateOpacityAnim(double ms, double to, double dur)
        {
            var anim = new DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(dur) };
            Storyboard.SetTarget(anim, TheImage);
            Storyboard.SetTargetProperty(anim, "Opacity");
            return anim;
        }

        private static void OnTransformChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageViewer v) v.ApplyTransform();
        }

        private void ApplyTransform()
        {
            _imageTransform.ScaleX = FlipH;
            _imageTransform.ScaleY = FlipV;
            _rotationStoryboard?.Stop();
            var anim = new DoubleAnimation
            {
                // From = 当前状态 (跟踪值): 中断的动画 Stop 后 Rotation 回落基值,
                // 不带 From 会从 0° 起步造成"以 0° 为基准"的倒转
                From = _currentRotation,
                To = ImageRotation,
                Duration = TimeSpan.FromMilliseconds(200)
            };
            Storyboard.SetTarget(anim, _imageTransform);
            Storyboard.SetTargetProperty(anim, "Rotation");
            var sb = new Storyboard();
            sb.Children.Add(anim);
            _rotationStoryboard = sb;
            _currentRotation = ImageRotation;
            sb.Begin();
        }

        private void Scroller_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            var p = e.GetPosition(Scroller);
            ToggleZoom(p);
            e.Handled = true;
            DoubleTappedOccurred?.Invoke(this, EventArgs.Empty);
        }

        private void Scroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            // Ctrl + 滚轮 = 缩放; 直接滚轮 = 默认平移 (与 Windows 照片一致)
            var ctrl = Windows.UI.Core.CoreVirtualKeyStates.Down ==
                       Window.Current.CoreWindow.GetKeyState(VirtualKey.Control);
            if (!ctrl)
            {
                // 走默认平移滚动
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

        /// <summary>
        /// 直接设置缩放级别 (缩放滑块用, 以视口中心为锚点)。
        /// </summary>
        public void SetZoomFactor(float zoom)
        {
            var clamped = Math.Max(Scroller.MinZoomFactor,
                                   Math.Min(Scroller.MaxZoomFactor, zoom));
            _suppressViewChange = true;
            // ChangeView 四参重载 (disableAnimation) 是 14393+ 才有;
            // DebugSimulate10240 已由 UwpCompat.HasContractV2 统一强制按 10240 处理
            if (Helpers.UwpCompat.HasContractV2)
            {
                Scroller.ChangeView(null, null, clamped, true);
            }
            else
            {
                // 10240: 三参 ChangeView, offset 由系统自行锚定 (同 FitToWindow)
                Helpers.DebugLog.Write("IMG", $"SetZoom {clamped} -> ChangeView3 (anim)");
                Scroller.ChangeView(null, null, clamped);
            }
            _suppressViewChange = false;
            ZoomFactorChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Scroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            var z = Scroller.ZoomFactor;
            Helpers.DebugLog.Write("IMG", $"ViewChanged inter={e.IsIntermediate} zoom={z:0.000} off={Scroller.HorizontalOffset:0.0},{Scroller.VerticalOffset:0.0} pendFade={_pendingFadeIn} supress={_suppressViewChange}");
            // 缩放级别变化 (含捏合过程) 时显示百分比指示器
            if ((e.IsIntermediate || Math.Abs(z - 1.0f) > 0.001f)
                && Math.Abs(z - _lastBadgeZoom) > 0.005f)
            {
                _lastBadgeZoom = z;
                ShowZoomBadge();
            }
            // fit 动画结束 (最终态) 后再淡入: 图片可见时缩放已完成, 无"淡入中缩放乱跳"
            if (_pendingFadeIn && !e.IsIntermediate)
            {
                _pendingFadeIn = false;
                _fadeInTimer.Stop();
                Helpers.DebugLog.Write("IMG", "FadeIn (fit anim ended)");
                FadeIn();
            }
            if (!_suppressViewChange && !e.IsIntermediate)
            {
                ZoomFactorChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // ====== 缩放指示器 ======
        private DispatcherTimer _zoomBadgeTimer;
        private float _lastBadgeZoom = -1f;
        private readonly Storyboard _zoomBadgeFade = new Storyboard();

        private void ShowZoomBadge()
        {
            if (_zoomBadgeTimer == null)
            {
                _zoomBadgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                _zoomBadgeTimer.Tick += (_, __) =>
                {
                    _zoomBadgeTimer.Stop();
                    _zoomBadgeFade.Stop();
                    _zoomBadgeFade.Children.Clear();
                    var anim = new DoubleAnimation
                    {
                        From = ZoomBadge.Opacity,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(250)
                    };
                    Storyboard.SetTarget(anim, ZoomBadge);
                    Storyboard.SetTargetProperty(anim, "Opacity");
                    _zoomBadgeFade.Children.Add(anim);
                    _zoomBadgeFade.Begin();
                };
                // 只订阅一次, 防止重复叠加
                _zoomBadgeFade.Completed += (s, e) =>
                {
                    if (ZoomBadge.Opacity <= 0.01) ZoomBadge.Visibility = Visibility.Collapsed;
                };
            }

            ZoomText.Text = string.Format("{0:0}%", Scroller.ZoomFactor * 100);
            ZoomBadge.Visibility = Visibility.Visible;
            ZoomBadge.Opacity = 1;
            _zoomBadgeFade.Stop();
            _zoomBadgeTimer.Stop();
            _zoomBadgeTimer.Start();
        }

        /// <summary>
        /// 复位视图: 新图加载 / 重置缩放 (数字0) 时调用, 切换到"适应窗口"档。
        /// 解码未完成 (PixelWidth==0) 或视口未就绪时挂起, 由事件/定时器兜底。
        /// </summary>
        public void ResetView()
        {
            _lastFitZoom = -1f;
            if (TryFit()) _pendingFit = false;
        }

        /// <summary>
        /// 适应窗口: 按图片布局尺寸 (含旋转) 计算缩放, 使整图恰好放入视口。
        /// 必须用布局尺寸 (ActualWidth) 而非 bmp.PixelWidth: PixelWidth 是物理像素,
        /// 高 DPI 系统 (如 220%) 上它与 ScrollViewer 的 Extent 单位 (布局尺寸) 不一致,
        /// 算出的 fit 会整体偏小约 DPI 倍数 (100% DPI 的 10240 上两者相等, 故 26100 模拟
        /// 与真实 10240 显示不符即源于此)。换图瞬间 ActualWidth 是旧图遗留值, 但 fit 只在
        /// 布局稳定后 (SizeChanged/定时器) 触发, 此时已是新图尺寸; PixelWidth 守卫仍保证
        /// 解码未完成时不拟合。
        /// </summary>
        public void FitToWindow()
        {
            if (!(TheImage.Source is BitmapImage bmp)
                || bmp.PixelWidth <= 0
                || bmp.PixelHeight <= 0)
            {
                return;
            }
            var iw = TheImage.ActualWidth;
            var ih = TheImage.ActualHeight;
            if (iw <= 0 || ih <= 0) return;

            // 旋转 90/270 时宽高互换 (RenderTransform 不影响布局尺寸)
            var rot = ImageRotation % 360;
            if (rot < 0) rot += 360;
            if (Math.Abs(rot - 90) < 0.5 || Math.Abs(rot - 270) < 0.5)
            {
                var t = iw;
                iw = ih;
                ih = t;
            }

            var vw = Scroller.ViewportWidth;
            var vh = Scroller.ViewportHeight;
            if (vw <= 0 || vh <= 0) return;

            var fit = (float)Math.Min(vw / iw, vh / ih);
            fit = Math.Max(Scroller.MinZoomFactor, Math.Min(Scroller.MaxZoomFactor, fit));
            _lastFitZoom = fit;
            _suppressViewChange = true;
            // ChangeView 四参重载 (disableAnimation) 是 14393+ 才有;
            // DebugSimulate10240 已由 UwpCompat.HasContractV2 统一强制按 10240 处理
            if (Helpers.UwpCompat.HasContractV2)
            {
                Helpers.DebugLog.Write("IMG", $"Fit: {iw}x{ih} vw={vw}vh={vh} fit={fit} -> ChangeView4(true)");
                Scroller.ChangeView(null, null, fit, true);
            }
            else
            {
                // 10240: 三参 ChangeView, offset 由系统在 zoom 变化时自行锚定
                // (保持视口中心的内容点), 居中正确; 不要显式设置 offset
                // (Extent 异步更新, 手算偏移会基于不一致的内部状态而偏右下方)
                Helpers.DebugLog.Write("IMG", $"Fit: {iw}x{ih} vw={vw}vh={vh} fit={fit} -> ChangeView3 (anim)");
                Scroller.ChangeView(null, null, fit);
            }
            _suppressViewChange = false;
            ZoomFactorChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>当前是否仍处于"适应窗口"档 (窗口尺寸变化时据此自动重新适应)。</summary>
        public bool IsAtFitZoom => _lastFitZoom > 0 && Math.Abs(Scroller.ZoomFactor - _lastFitZoom) < 0.01f;

        public float CurrentZoomFactor => Scroller.ZoomFactor;
    }
}
