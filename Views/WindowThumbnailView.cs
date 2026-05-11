using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using HotKeyCommandApp.Services;

namespace HotKeyCommandApp.Views
{
    /// <summary>
    /// 指定されたウィンドウハンドル (HWND) のライブプレビューを表示するコントロール
    /// </summary>
    public class WindowThumbnailView : FrameworkElement
    {
        private IntPtr _thumbnailHandle = IntPtr.Zero;

        public static readonly DependencyProperty IsThumbnailEnabledProperty =
            DependencyProperty.Register("IsThumbnailEnabled", typeof(bool), typeof(WindowThumbnailView),
                new PropertyMetadata(false, OnIsThumbnailEnabledChanged));

        public bool IsThumbnailEnabled
        {
            get => (bool)GetValue(IsThumbnailEnabledProperty);
            set => SetValue(IsThumbnailEnabledProperty, value);
        }

        private static void OnIsThumbnailEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WindowThumbnailView view)
            {
                view.UpdateThumbnail();
            }
        }

        public static readonly DependencyProperty SourceWindowHandleProperty =
            DependencyProperty.Register("SourceWindowHandle", typeof(IntPtr), typeof(WindowThumbnailView),
                new PropertyMetadata(IntPtr.Zero, OnSourceWindowHandleChanged));

        public IntPtr SourceWindowHandle
        {
            get => (IntPtr)GetValue(SourceWindowHandleProperty);
            set => SetValue(SourceWindowHandleProperty, value);
        }

        private static void OnSourceWindowHandleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WindowThumbnailView view)
            {
                view.UnregisterThumbnail();
                view.UpdateThumbnail();
            }
        }

        public static readonly DependencyProperty SourceWindowWidthProperty =
            DependencyProperty.Register("SourceWindowWidth", typeof(int), typeof(WindowThumbnailView),
                new PropertyMetadata(0, OnSourceSizeChanged));

        public int SourceWindowWidth
        {
            get => (int)GetValue(SourceWindowWidthProperty);
            set => SetValue(SourceWindowWidthProperty, value);
        }

        public static readonly DependencyProperty SourceWindowHeightProperty =
            DependencyProperty.Register("SourceWindowHeight", typeof(int), typeof(WindowThumbnailView),
                new PropertyMetadata(0, OnSourceSizeChanged));

        public int SourceWindowHeight
        {
            get => (int)GetValue(SourceWindowHeightProperty);
            set => SetValue(SourceWindowHeightProperty, value);
        }

        private static void OnSourceSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WindowThumbnailView view)
            {
                view.UpdateThumbnail();
            }
        }

        public WindowThumbnailView()
        {
            this.SizeChanged += (s, e) => UpdateThumbnail();
            this.IsVisibleChanged += (s, e) => UpdateThumbnail();
            this.Unloaded += (s, e) => UnregisterThumbnail();
        }

        private void UnregisterThumbnail()
        {
            if (_thumbnailHandle != IntPtr.Zero)
            {
                NativeMethods.DwmUnregisterThumbnail(_thumbnailHandle);
                _thumbnailHandle = IntPtr.Zero;
            }
        }

        private void UpdateThumbnail()
        {
            if (!IsThumbnailEnabled || SourceWindowHandle == IntPtr.Zero || !this.IsVisible || this.ActualWidth <= 0 || this.ActualHeight <= 0)
            {
                UnregisterThumbnail();
                return;
            }

            var window = Window.GetWindow(this);
            if (window == null) return;

            var targetHwnd = new WindowInteropHelper(window).Handle;
            if (targetHwnd == IntPtr.Zero) return;

            if (_thumbnailHandle == IntPtr.Zero)
            {
                if (NativeMethods.DwmRegisterThumbnail(targetHwnd, SourceWindowHandle, out _thumbnailHandle) != 0)
                {
                    return;
                }
            }

            // パレットウィンドウ内での相対座標を計算
            var point = this.TranslatePoint(new Point(0, 0), window);
            
            // DPI スケーリングの考慮
            var source = PresentationSource.FromVisual(this);
            double dpiX = 1.0;
            double dpiY = 1.0;

            if (source?.CompositionTarget != null)
            {
                dpiX = source.CompositionTarget.TransformToDevice.M11;
                dpiY = source.CompositionTarget.TransformToDevice.M22;
            }

            // アスペクト比を維持した表示領域の計算 (Uniform)
            double targetWidth = this.ActualWidth;
            double targetHeight = this.ActualHeight;
            double offsetX = 0;
            double offsetY = 0;

            if (SourceWindowWidth > 0 && SourceWindowHeight > 0)
            {
                double sourceAspect = (double)SourceWindowWidth / SourceWindowHeight;
                double targetAspect = targetWidth / targetHeight;

                if (sourceAspect > targetAspect)
                {
                    // ウィンドウが横長：横幅いっぱいで上下に余白
                    double newHeight = targetWidth / sourceAspect;
                    offsetY = (targetHeight - newHeight) / 2;
                    targetHeight = newHeight;
                }
                else
                {
                    // ウィンドウが縦長：縦幅いっぱいで左右に余白
                    double newWidth = targetHeight * sourceAspect;
                    offsetX = (targetWidth - newWidth) / 2;
                    targetWidth = newWidth;
                }
            }

            var props = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = NativeMethods.DWM_TNP_RECTDESTINATION | NativeMethods.DWM_TNP_VISIBLE | NativeMethods.DWM_TNP_SOURCECLIENTAREAONLY,
                rcDestination = new NativeMethods.RECT
                {
                    Left = (int)((point.X + offsetX) * dpiX),
                    Top = (int)((point.Y + offsetY) * dpiY),
                    Right = (int)((point.X + offsetX + targetWidth) * dpiX),
                    Bottom = (int)((point.Y + offsetY + targetHeight) * dpiY)
                },
                fVisible = true,
                fSourceClientAreaOnly = true,
                opacity = 255
            };

            NativeMethods.DwmUpdateThumbnailProperties(_thumbnailHandle, ref props);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (ActualWidth <= 0 || ActualHeight <= 0) return;

            // 背景を描画しないと、領域がヒットテストやレイアウト更新の対象から外れる場合があるため、
            // 透明な背景を描画しておく
            drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        }
    }
}
