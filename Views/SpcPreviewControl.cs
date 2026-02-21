using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AffToSpcConverter.Convert.Preview;
using AffToSpcConverter.Models;

namespace AffToSpcConverter.Views
{
    public class SpcPreviewControl : FrameworkElement
    {
        private readonly SpcPreviewRenderer _renderer;
        private readonly VisualCollection _visuals;
        private readonly DrawingVisual _fpsVisual = new();
        
        private int _frameCount;
        private int _lastTick;

        public SpcPreviewControl() 
        {
            Focusable = true;
            _renderer = new SpcPreviewRenderer(this);
            _visuals = new VisualCollection(this) { _renderer.RootVisual, _fpsVisual };

            RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);

            Loaded += (s, e) => CompositionTarget.Rendering += OnRendering;
            Unloaded += (s, e) => CompositionTarget.Rendering -= OnRendering;
        }

        protected override int VisualChildrenCount => _visuals.Count;
        protected override Visual GetVisualChild(int index) => _visuals[index];

        private void OnRendering(object? sender, EventArgs e)
        {
            _frameCount++;
            int now = Environment.TickCount;
            if (now - _lastTick >= 1000)
            {
                int fps = _frameCount;
                _frameCount = 0;
                _lastTick = now;
                RenderFps(fps);
            }
        }

        private void RenderFps(int fps)
        {
            using var dc = _fpsVisual.RenderOpen();
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var ft = new FormattedText($"FPS: {fps}", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), 14, Brushes.Lime, dpi);
            dc.DrawText(ft, new Point(ActualWidth - 80, 10));
        }

        // ===== Dependency Properties =====

        public IReadOnlyList<ISpcEvent>? Events
        {
            get => (IReadOnlyList<ISpcEvent>?)GetValue(EventsProperty);
            set => SetValue(EventsProperty, value);
        }
        public static readonly DependencyProperty EventsProperty =
            DependencyProperty.Register(nameof(Events), typeof(IReadOnlyList<ISpcEvent>), typeof(SpcPreviewControl),
                new FrameworkPropertyMetadata(null, (d, _) =>
                {
                    var c = (SpcPreviewControl)d;
                    c.Model = c.Events != null ? SpcRenderModelBuilder.Build(c.Events) : null;
                    SpcGeometryBuilder.ClearCache();
                    c.RebuildAll();
                }));

        public double PixelsPerSecond
        {
            get => (double)GetValue(PixelsPerSecondProperty);
            set => SetValue(PixelsPerSecondProperty, value);
        }
        public static readonly DependencyProperty PixelsPerSecondProperty =
            DependencyProperty.Register(nameof(PixelsPerSecond), typeof(double), typeof(SpcPreviewControl),
                new FrameworkPropertyMetadata(240.0, (d, e) =>
                {
                    var c = (SpcPreviewControl)d;
                    c.PxPerMs = (double)e.NewValue / 1000.0;
                }));

        public RenderModel? Model { get; private set; }

        public int JudgeTimeMs
        {
            get => (int)GetValue(JudgeTimeMsProperty);
            set => SetValue(JudgeTimeMsProperty, value);
        }
        public static readonly DependencyProperty JudgeTimeMsProperty =
            DependencyProperty.Register(nameof(JudgeTimeMs), typeof(int), typeof(SpcPreviewControl),
                new FrameworkPropertyMetadata(0, (d, e) =>
                {
                    var c = (SpcPreviewControl)d;
                    c._renderer.UpdateTime(c.JudgeTimeMs);
                }));

        public double PxPerMs
        {
            get => (double)GetValue(PxPerMsProperty);
            set => SetValue(PxPerMsProperty, value);
        }
        public static readonly DependencyProperty PxPerMsProperty =
            DependencyProperty.Register(nameof(PxPerMs), typeof(double), typeof(SpcPreviewControl),
                new FrameworkPropertyMetadata(0.12, (d, e) =>
                {
                    var c = (SpcPreviewControl)d;
                    SpcGeometryBuilder.ClearCache();
                    c.RebuildAll();
                }));

        // ===== Interaction =====
        private bool   _dragTime, _dragZoom;
        private Point  _dragStart;
        private int    _judgeTimeStart;
        private double _pxPerMsStart;

        private const double MinPxPerMs      = 0.02;
        private const double MaxPxPerMs      = 1.20;
        private const double RulerW          = 60.0;
        private const double JudgeFromBottom = 40.0;
        private const double TopPad          = 8.0;

        // ===== 编辑功能预留 =====
        private int _selectedItemIndex = -1;

        public int SelectedItemIndex
        {
            get => _selectedItemIndex;
            set { _selectedItemIndex = value; RebuildAll(); }
        }

        // ===== Layout snapshot =====
        private double _lSkyLeft, _lSkyWidth, _lJudgeY, _lW, _lH;
        private bool   _layoutValid = false;

        public double GetJudgeY() => Math.Max(TopPad + 20, ActualHeight - JudgeFromBottom);

        private double TimeAtY(double judgeY, double y)
            => JudgeTimeMs + (judgeY - y) / Math.Max(1e-6, PxPerMs);

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            SpcGeometryBuilder.ClearCache();
            _layoutValid = false;
            RebuildAll();
        }

        private void RebuildAll()
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 4 || h <= 4) return;

            double judgeY = GetJudgeY();
            double contentLeft = RulerW;
            double contentW = w - RulerW;
            if (contentW < 10) return;

            double skyW = contentW * 0.60;
            double panelH = judgeY - TopPad;
            if (panelH < 4) return;

            var sky = new Rect(contentLeft, TopPad, skyW, panelH);
            var ground = new Rect(contentLeft + skyW, TopPad, contentW - skyW, panelH);

            if (!_layoutValid ||
                Math.Abs(_lSkyLeft - sky.Left) > 0.5 ||
                Math.Abs(_lSkyWidth - sky.Width) > 0.5 ||
                Math.Abs(_lJudgeY - judgeY) > 0.5 ||
                Math.Abs(_lW - w) > 0.5 || Math.Abs(_lH - h) > 0.5)
            {
                _lSkyLeft = sky.Left;
                _lSkyWidth = sky.Width;
                _lJudgeY = judgeY;
                _lW = w; _lH = h;
                _layoutValid = true;
                SpcGeometryBuilder.ClearCache();
            }

            _renderer.RebuildAll(sky, ground, judgeY, contentLeft, contentW, panelH, w, h);
        }

        // ===== Mouse =====

        private int _lastMouseMoveTick;

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            Focus();
            double judgeY = GetJudgeY();
            bool   ctrl   = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var    pos    = e.GetPosition(this);

            if (ctrl)
            {
                double tAnchor = TimeAtY(judgeY, pos.Y);
                double newPx   = Math.Clamp(PxPerMs * (e.Delta > 0 ? 1.12 : 1.0 / 1.12), MinPxPerMs, MaxPxPerMs);
                PxPerMs        = newPx;
                JudgeTimeMs    = Math.Max(0, (int)Math.Round(tAnchor - (judgeY - pos.Y) / PxPerMs));
            }
            else
            {
                int step = Math.Clamp((int)Math.Round(240 / Math.Max(0.05, PxPerMs)), 20, 2000);
                JudgeTimeMs = Math.Max(0, JudgeTimeMs + (e.Delta > 0 ? -step : step));
            }
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            _dragStart      = e.GetPosition(this);
            _judgeTimeStart = JudgeTimeMs;
            _pxPerMsStart   = PxPerMs;
            if      (e.ChangedButton == MouseButton.Left)  { _dragTime = true; CaptureMouse(); e.Handled = true; }
            else if (e.ChangedButton == MouseButton.Right) { _dragZoom = true; CaptureMouse(); e.Handled = true; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragTime && !_dragZoom) return;

            int now = Environment.TickCount;
            if (now - _lastMouseMoveTick < 8) return; // ~120fps throttle for mouse drag to prevent UI thread starvation
            _lastMouseMoveTick = now;

            double dy     = e.GetPosition(this).Y - _dragStart.Y;
            double judgeY = GetJudgeY();
            if (_dragTime)
            {
                JudgeTimeMs = Math.Max(0, _judgeTimeStart + (int)Math.Round(dy / Math.Max(1e-6, PxPerMs)));
            }
            else
            {
                double tAnchor = TimeAtY(judgeY, _dragStart.Y);
                PxPerMs        = Math.Clamp(_pxPerMsStart * Math.Exp(-dy * 0.005), MinPxPerMs, MaxPxPerMs);
                JudgeTimeMs    = Math.Max(0, (int)Math.Round(tAnchor - (judgeY - _dragStart.Y) / PxPerMs));
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if      (e.ChangedButton == MouseButton.Left  && _dragTime) { _dragTime = false; ReleaseMouseCapture(); e.Handled = true; }
            else if (e.ChangedButton == MouseButton.Right && _dragZoom) { _dragZoom = false; ReleaseMouseCapture(); e.Handled = true; }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (_dragTime || _dragZoom) { _dragTime = false; _dragZoom = false; ReleaseMouseCapture(); }
        }
    }
}