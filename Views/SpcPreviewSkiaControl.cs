using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AffToSpcConverter.Convert.Preview;
using AffToSpcConverter.Models;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace AffToSpcConverter.Views
{
    public class SpcPreviewSkiaControl : SKElement
    {
        private int _frameCount;
        private int _lastTick;
        private int _fps;
        private const int FpsSampleMs = 250;

        public SpcPreviewSkiaControl()
        {
            Focusable = true;
            Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
            Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            SpcSkiaGeometryBuilder.ClearCache();
            _layoutValid = false;
            InvalidateVisual();
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            _frameCount++;
            int now = Environment.TickCount;
            int elapsed = now - _lastTick;
            if (elapsed >= FpsSampleMs)
            {
                _fps = (int)Math.Round(_frameCount * 1000.0 / Math.Max(1, elapsed));
                _frameCount = 0;
                _lastTick = now;
            }
            InvalidateVisual();
        }

        // ===== Dependency Properties =====

        public IReadOnlyList<ISpcEvent>? Events
        {
            get => (IReadOnlyList<ISpcEvent>?)GetValue(EventsProperty);
            set => SetValue(EventsProperty, value);
        }
        public static readonly DependencyProperty EventsProperty =
            DependencyProperty.Register(nameof(Events), typeof(IReadOnlyList<ISpcEvent>), typeof(SpcPreviewSkiaControl),
                new FrameworkPropertyMetadata(null, (d, _) =>
                {
                    var c = (SpcPreviewSkiaControl)d;
                    c.Model = c.Events != null ? SpcRenderModelBuilder.Build(c.Events) : null;
                    SpcSkiaGeometryBuilder.ClearCache();
                    c.InvalidateVisual();
                }));

        public double PixelsPerSecond
        {
            get => (double)GetValue(PixelsPerSecondProperty);
            set => SetValue(PixelsPerSecondProperty, value);
        }
        public static readonly DependencyProperty PixelsPerSecondProperty =
            DependencyProperty.Register(nameof(PixelsPerSecond), typeof(double), typeof(SpcPreviewSkiaControl),
                new FrameworkPropertyMetadata(240.0, (d, _) =>
                {
                    var c = (SpcPreviewSkiaControl)d;
                    c.UpdatePxPerMs();
                }));

        public double Speed
        {
            get => (double)GetValue(SpeedProperty);
            set => SetValue(SpeedProperty, value);
        }
        public static readonly DependencyProperty SpeedProperty =
            DependencyProperty.Register(nameof(Speed), typeof(double), typeof(SpcPreviewSkiaControl),
                new FrameworkPropertyMetadata(1.0, (d, _) =>
                {
                    var c = (SpcPreviewSkiaControl)d;
                    c.UpdatePxPerMs();
                }));

        private void UpdatePxPerMs()
        {
            var speed = Math.Max(0.01, Speed);
            PxPerMs = PixelsPerSecond * speed / 1000.0;
        }

        public RenderModel? Model { get; private set; }

        public int JudgeTimeMs
        {
            get => (int)GetValue(JudgeTimeMsProperty);
            set => SetValue(JudgeTimeMsProperty, value);
        }
        public static readonly DependencyProperty JudgeTimeMsProperty =
            DependencyProperty.Register(nameof(JudgeTimeMs), typeof(int), typeof(SpcPreviewSkiaControl),
                new FrameworkPropertyMetadata(0, (d, _) =>
                {
                    var c = (SpcPreviewSkiaControl)d;
                    c._judgeTimeMsPrecise = c.JudgeTimeMs;
                    c.InvalidateVisual();
                }));

        public double JudgeTimeMsPrecise
        {
            get => (double)GetValue(JudgeTimeMsPreciseProperty);
            set => SetValue(JudgeTimeMsPreciseProperty, value);
        }
        public static readonly DependencyProperty JudgeTimeMsPreciseProperty =
            DependencyProperty.Register(nameof(JudgeTimeMsPrecise), typeof(double), typeof(SpcPreviewSkiaControl),
                new FrameworkPropertyMetadata(0.0, (d, _) =>
                {
                    var c = (SpcPreviewSkiaControl)d;
                    c._judgeTimeMsPrecise = c.JudgeTimeMsPrecise;
                    c.InvalidateVisual();
                }));

        public double PxPerMs
        {
            get => (double)GetValue(PxPerMsProperty);
            set => SetValue(PxPerMsProperty, value);
        }
        public static readonly DependencyProperty PxPerMsProperty =
            DependencyProperty.Register(nameof(PxPerMs), typeof(double), typeof(SpcPreviewSkiaControl),
                new FrameworkPropertyMetadata(0.12, (d, _) =>
                {
                    var c = (SpcPreviewSkiaControl)d;
                    SpcSkiaGeometryBuilder.ClearCache();
                    c.InvalidateVisual();
                }));

        // ===== Interaction =====
        private bool _dragTime, _dragZoom;
        private Point _dragStart;
        private int _judgeTimeStart;
        private double _pxPerMsStart;

        private const double MinPxPerMs = 0.02;
        private const double MaxPxPerMs = 1.20;
        private const double RulerW = 60.0;
        private const double JudgeFromBottom = 40.0;
        private const double TopPad = 8.0;

        // ===== ±à¼­¹¦ÄÜÔ¤Áô =====
        private int _selectedItemIndex = -1;

        public int SelectedItemIndex
        {
            get => _selectedItemIndex;
            set { _selectedItemIndex = value; InvalidateVisual(); }
        }

        // ===== Layout snapshot =====
        private double _lSkyLeft, _lSkyWidth, _lJudgeY, _lW, _lH;
        private bool _layoutValid = false;

        public double GetJudgeY() => Math.Max(TopPad + 20, ActualHeight - JudgeFromBottom);

        private double TimeAtY(double judgeY, double y)
            => _judgeTimeMsPrecise + (judgeY - y) / Math.Max(1e-6, PxPerMs);

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            Focus();
            double judgeY = GetJudgeY();
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var pos = e.GetPosition(this);

            if (ctrl)
            {
                double tAnchor = TimeAtY(judgeY, pos.Y);
                double newPx = Math.Clamp(PxPerMs * (e.Delta > 0 ? 1.12 : 1.0 / 1.12), MinPxPerMs, MaxPxPerMs);
                var speed = Math.Max(0.01, Speed);
                PixelsPerSecond = newPx * 1000.0 / speed;
                JudgeTimeMs = Math.Max(0, (int)Math.Round(tAnchor - (judgeY - pos.Y) / PxPerMs));
                JudgeTimeMsPrecise = JudgeTimeMs;
            }
            else
            {
                int step = Math.Clamp((int)Math.Round(240 / Math.Max(0.05, PxPerMs)), 20, 2000);
                JudgeTimeMs = Math.Max(0, JudgeTimeMs + (e.Delta > 0 ? -step : step));
                JudgeTimeMsPrecise = JudgeTimeMs;
            }
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            _dragStart = e.GetPosition(this);
            _judgeTimeStart = JudgeTimeMs;
            _pxPerMsStart = PxPerMs;
            if (e.ChangedButton == MouseButton.Left) { _dragTime = true; CaptureMouse(); e.Handled = true; }
            else if (e.ChangedButton == MouseButton.Right) { _dragZoom = true; CaptureMouse(); e.Handled = true; }
        }

        private int _lastMouseMoveTick;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragTime && !_dragZoom) return;

            int now = Environment.TickCount;
            if (now - _lastMouseMoveTick < 8) return;
            _lastMouseMoveTick = now;

            double dy = e.GetPosition(this).Y - _dragStart.Y;
            double judgeY = GetJudgeY();
            if (_dragTime)
            {
                JudgeTimeMs = Math.Max(0, _judgeTimeStart + (int)Math.Round(dy / Math.Max(1e-6, PxPerMs)));
                JudgeTimeMsPrecise = JudgeTimeMs;
            }
            else
            {
                double tAnchor = TimeAtY(judgeY, _dragStart.Y);
                double newPx = Math.Clamp(_pxPerMsStart * Math.Exp(-dy * 0.005), MinPxPerMs, MaxPxPerMs);
                var speed = Math.Max(0.01, Speed);
                PixelsPerSecond = newPx * 1000.0 / speed;
                JudgeTimeMs = Math.Max(0, (int)Math.Round(tAnchor - (judgeY - _dragStart.Y) / PxPerMs));
                JudgeTimeMsPrecise = JudgeTimeMs;
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.ChangedButton == MouseButton.Left && _dragTime) { _dragTime = false; ReleaseMouseCapture(); e.Handled = true; }
            else if (e.ChangedButton == MouseButton.Right && _dragZoom) { _dragZoom = false; ReleaseMouseCapture(); e.Handled = true; }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (_dragTime || _dragZoom) { _dragTime = false; _dragZoom = false; ReleaseMouseCapture(); }
        }

        // ===== Rendering =====
        private Rect _sky;
        private Rect _ground;
        private double _judgeY;
        private double _contentLeft;
        private double _contentW;
        private double _panelH;
        private double _w;
        private double _h;
        private double _judgeTimeMs;
        private double _judgeTimeMsPrecise;

        private void EnsureLayout()
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
                SpcSkiaGeometryBuilder.ClearCache();
            }

            _sky = sky;
            _ground = ground;
            _judgeY = judgeY;
            _contentLeft = contentLeft;
            _contentW = contentW;
            _panelH = panelH;
            _w = w;
            _h = h;
            _judgeTimeMs = JudgeTimeMs;
            _judgeTimeMsPrecise = JudgeTimeMsPrecise;
        }

        protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
        {
            base.OnPaintSurface(e);
            var canvas = e.Surface.Canvas;
            canvas.Clear();

            float scaleX = (float)(e.Info.Width / Math.Max(1.0, ActualWidth));
            float scaleY = (float)(e.Info.Height / Math.Max(1.0, ActualHeight));
            canvas.Scale(scaleX, scaleY);

            EnsureLayout();
            if (_w <= 0 || _h <= 0) return;

            DrawBackground(canvas);
            DrawJudge(canvas);
            DrawRuler(canvas);
            DrawNotes(canvas);
            DrawFps(canvas);
        }

        private static readonly SKPaint BgPaint = CreateFill(15, 15, 18);
        private static readonly SKPaint PanelBgPaint = CreateFill(18, 18, 24);
        private static readonly SKPaint BorderPaint = CreateStroke(55, 55, 70, 1);
        private static readonly SKPaint GridPaint = CreateStroke(35, 35, 46, 1);
        private static readonly SKPaint LabelPaint = CreateTextPaint(120, 120, 140, 11);
        private static readonly SKPaint MeasurePaint = CreateStroke(80, 86, 112, 1.8f);
        private static readonly SKPaint BeatPaint = CreateStroke(55, 58, 78, 1.4f, new float[] { 1, 6 });
        private static readonly SKPaint RulerLabelPaint = CreateTextPaint(130, 130, 155, 10);
        private static readonly SKPaint JudgePaint = CreateStroke(220, 60, 30, 1.5f);
        private static readonly SKPaint JudgeTextPaint = CreateTextPaint(220, 60, 30, 11);
        private static readonly SKPaint TapFillPaint = CreateFill(100, 210, 255);
        private static readonly SKPaint TapStrokePaint = CreateStroke(180, 240, 255, 1);
        private static readonly SKPaint HoldFillPaint = CreateFill(60, 170, 230, 110);
        private static readonly SKPaint HoldStrokePaint = CreateStroke(100, 210, 255, 1, alpha: 180);
        private static readonly SKPaint FlickFillLeftPaint = CreateFill(220, 200, 80);
        private static readonly SKPaint FlickStrokeLeftPaint = CreateStroke(255, 240, 120, 1.5f);
        private static readonly SKPaint FlickFillRightPaint = CreateFill(80, 200, 120);
        private static readonly SKPaint FlickStrokeRightPaint = CreateStroke(120, 240, 160, 1.5f);
        private static readonly SKPaint SkyAreaFillPaint = CreateFill(140, 100, 230, 90);
        private static readonly SKPaint SkyAreaStrokePaint = CreateStroke(180, 150, 255, 1, alpha: 200);
        private static readonly SKPaint SelectedFillPaint = CreateFill(255, 220, 60, 80);
        private static readonly SKPaint SelectedStrokePaint = CreateStroke(255, 220, 60, 2);
        private static readonly SKPaint HoverFillPaint = CreateFill(255, 255, 255, 50);
        private static readonly SKPaint FpsPaint = CreateTextPaint(0, 255, 0, 14);

        private static SKPaint CreateFill(byte r, byte g, byte b, byte alpha = 255)
        {
            return new SKPaint
            {
                Color = new SKColor(r, g, b, alpha),
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
        }

        private static SKPaint CreateStroke(byte r, byte g, byte b, float width, float[]? dash = null, byte alpha = 255)
        {
            var paint = new SKPaint
            {
                Color = new SKColor(r, g, b, alpha),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = width
            };
            if (dash != null)
            {
                paint.PathEffect = SKPathEffect.CreateDash(dash, 0);
            }
            return paint;
        }

        private static SKPaint CreateTextPaint(byte r, byte g, byte b, float size)
        {
            return new SKPaint
            {
                Color = new SKColor(r, g, b),
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Consolas"),
                TextSize = size
            };
        }

        private void DrawBackground(SKCanvas canvas)
        {
            var clip = new SKRect((float)_contentLeft, 8f, (float)(_contentLeft + _contentW), (float)(8 + _panelH));
            canvas.DrawRect(new SKRect(0, 0, (float)_w, (float)_h), BgPaint);
            canvas.Save();
            canvas.ClipRect(clip);

            var skyRect = ToSkRect(_sky);
            var groundRect = ToSkRect(_ground);
            canvas.DrawRect(skyRect, PanelBgPaint);
            canvas.DrawRect(skyRect, BorderPaint);
            canvas.DrawRect(groundRect, PanelBgPaint);
            canvas.DrawRect(groundRect, BorderPaint);

            for (int i = 1; i < 8; i++)
            {
                float x = (float)(_sky.Left + _sky.Width * i / 8.0);
                canvas.DrawLine(x, (float)_sky.Top, x, (float)_sky.Bottom, GridPaint);
            }
            for (int i = 0; i <= 6; i++)
            {
                float x = (float)(_ground.Left + _ground.Width * i / 6.0);
                canvas.DrawLine(x, (float)_ground.Top, x, (float)_ground.Bottom, GridPaint);
            }

            DrawText(canvas, "SKY", (float)_sky.Left + 6, (float)_sky.Top + 4, LabelPaint);
            DrawText(canvas, "GROUND", (float)_ground.Left + 6, (float)_ground.Top + 4, LabelPaint);
            canvas.Restore();
        }

        private void DrawJudge(SKCanvas canvas)
        {
            canvas.DrawLine((float)_contentLeft, (float)_judgeY, (float)_w, (float)_judgeY, JudgePaint);
            DrawText(canvas, "JUDGE", (float)_contentLeft + 4, (float)_judgeY + 3, JudgeTextPaint);
        }

        private void DrawRuler(SKCanvas canvas)
        {
            var model = Model;
            if (model == null || model.Bpm <= 0 || model.Beats <= 0) return;

            double msPerBeat = 60000.0 / model.Bpm;
            double msPerMeasure = msPerBeat * model.Beats;
            double pxPerMs = PxPerMs;

            double chosenMs = msPerMeasure;
            foreach (var m in new[] { 0.25, 0.5, 1.0, 2.0, 4.0, 8.0, 16.0, 32.0 })
            {
                double c = msPerMeasure * m;
                if (c * pxPerMs >= 80) { chosenMs = c; break; }
            }

            double topY = _sky.Top;
            double bottomY = _sky.Bottom;

            double tMin = _judgeTimeMsPrecise + (_judgeY - bottomY) / pxPerMs;
            double tMax = _judgeTimeMsPrecise + (_judgeY - topY) / pxPerMs;

            int startM = (int)Math.Floor(tMin / chosenMs);
            int endM = (int)Math.Ceiling(tMax / chosenMs);

            for (int m = startM; m <= endM; m++)
            {
                double t = m * chosenMs;
                double y = _judgeY - (t - _judgeTimeMsPrecise) * pxPerMs;
                if (y < topY - 2 || y > bottomY + 2) continue;
                canvas.DrawLine((float)_sky.Left, (float)y, (float)_ground.Right, (float)y, MeasurePaint);
                DrawText(canvas, $"#{t / msPerMeasure:0.##}", 2, (float)y - 10, RulerLabelPaint);
            }

            double pxPerBeat = msPerBeat * pxPerMs;
            double subMs = 0;
            if (pxPerBeat >= 28) subMs = msPerBeat / 4.0;
            else if (pxPerBeat >= 16) subMs = msPerBeat / 2.0;
            else if (pxPerBeat >= 10) subMs = msPerBeat;

            if (subMs <= 0) return;

            int startS = (int)Math.Floor(tMin / subMs);
            int endS = (int)Math.Ceiling(tMax / subMs);
            for (int s = startS; s <= endS; s++)
            {
                double t = s * subMs;
                if (Math.Abs(t % chosenMs) < 1.0) continue;
                double y = _judgeY - (t - _judgeTimeMsPrecise) * pxPerMs;
                if (y < topY - 1 || y > bottomY + 1) continue;
                canvas.DrawLine((float)_sky.Left, (float)y, (float)_ground.Right, (float)y, BeatPaint);
            }
        }

        private static int FindFirstIndexByTime(List<RenderItem> items, int timeMs)
        {
            int lo = 0;
            int hi = items.Count - 1;
            int result = items.Count;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                if (items[mid].TimeMs >= timeMs)
                {
                    result = mid;
                    hi = mid - 1;
                }
                else
                {
                    lo = mid + 1;
                }
            }
            return result;
        }

        private void DrawNotes(SKCanvas canvas)
        {
            var model = Model;
            if (model == null) return;

            var items = model.Items;
            double pxPerMs = PxPerMs;
            double clipTop = _sky.Top - 50;
            double clipBottom = _judgeY + 50;

            double tMin = _judgeTimeMsPrecise + (_judgeY - clipBottom) / pxPerMs;
            double tMax = _judgeTimeMsPrecise + (_judgeY - clipTop) / pxPerMs;

            int start = FindFirstIndexByTime(items, (int)Math.Floor(tMin));
            while (start > 0 && items[start - 1].EndTimeMs >= tMin)
            {
                start--;
            }

            int end = FindFirstIndexByTime(items, (int)Math.Ceiling(tMax + 1));

            for (int idx = start; idx < end; idx++)
            {
                var item = items[idx];
                double yStart = _judgeY - (item.TimeMs - _judgeTimeMsPrecise) * pxPerMs;
                double yEnd = _judgeY - (item.EndTimeMs - _judgeTimeMsPrecise) * pxPerMs;

                double top = Math.Min(yStart, yEnd);
                double bottom = Math.Max(yStart, yEnd);
                if (bottom < clipTop || top > clipBottom) continue;

                bool isSelected = idx == SelectedItemIndex;
                bool isHovered = false;

                switch (item.Type)
                {
                    case RenderItemType.GroundTap:
                        DrawGroundTap(canvas, _ground, yStart, item.Lane, item.Kind, isSelected, isHovered);
                        break;
                    case RenderItemType.GroundHold:
                        DrawGroundHold(canvas, _ground, item, yStart, yEnd, isSelected, isHovered);
                        break;
                    case RenderItemType.SkyFlick:
                        DrawSkyFlick(canvas, _sky, yStart, item, isSelected, isHovered);
                        break;
                    case RenderItemType.SkyArea:
                        DrawSkyArea(canvas, _sky, item, pxPerMs, yStart, isSelected, isHovered);
                        break;
                }
            }
        }

        private void DrawGroundTap(SKCanvas canvas, Rect ground, double y, int lane, int kind, bool selected, bool hovered)
        {
            lane = Math.Clamp(lane, 0, 5);
            kind = Math.Clamp(kind, 1, 4);
            int leftLane = lane;
            if (leftLane + kind > 6) leftLane = 6 - kind;

            double x0 = ground.Left + ground.Width * leftLane / 6.0;
            double x1 = ground.Left + ground.Width * (leftLane + kind) / 6.0;

            double width = (x1 - x0) - 4;
            if (width <= 0) return;

            var rect = new SKRect((float)(x0 + 2), (float)(y - 5), (float)(x0 + 2 + width), (float)(y + 5));
            canvas.DrawRoundRect(rect, 3, 3, TapFillPaint);
            canvas.DrawRoundRect(rect, 3, 3, TapStrokePaint);
            if (selected)
            {
                canvas.DrawRoundRect(rect, 3, 3, SelectedFillPaint);
                canvas.DrawRoundRect(rect, 3, 3, SelectedStrokePaint);
            }
            else if (hovered)
            {
                canvas.DrawRoundRect(rect, 3, 3, HoverFillPaint);
            }
        }

        private void DrawGroundHold(SKCanvas canvas, Rect ground, RenderItem item, double y0, double y1, bool selected, bool hovered)
        {
            int lane = Math.Clamp(item.Lane, 0, 5);
            int kind = Math.Clamp(item.Kind, 1, 4);
            int leftLane = lane;
            if (leftLane + kind > 6) leftLane = 6 - kind;

            double x0 = ground.Left + ground.Width * leftLane / 6.0;
            double x1 = ground.Left + ground.Width * (leftLane + kind) / 6.0;

            double top = Math.Min(y0, y1);
            double bottom = Math.Max(y0, y1);
            if (bottom - top < 1) return;

            double width = (x1 - x0) - 10;
            if (width <= 0) return;

            var body = new SKRect((float)(x0 + 5), (float)top, (float)(x0 + 5 + width), (float)bottom);
            canvas.DrawRect(body, HoldFillPaint);
            canvas.DrawRect(body, HoldStrokePaint);
            if (selected)
            {
                canvas.DrawRect(body, SelectedFillPaint);
                canvas.DrawRect(body, SelectedStrokePaint);
            }
            else if (hovered)
            {
                canvas.DrawRect(body, HoverFillPaint);
            }

            DrawGroundTap(canvas, ground, y0, item.Lane, item.Kind, selected, hovered);
        }

        private void DrawSkyFlick(SKCanvas canvas, Rect sky, double y, RenderItem item, bool selected, bool hovered)
        {
            int den = Math.Max(1, item.Den);
            double cx = sky.Left + sky.Width * Math.Clamp(item.X0 / (double)den, 0, 1);
            double wPx = sky.Width * Math.Clamp(item.W0 / (double)den, 0, 1);
            double half = Math.Max(20, wPx * 0.5);
            const double triH = 45;

            bool isLeft = item.Dir == 16;
            using var path = SpcSkiaGeometryBuilder.BuildFlickPath((float)cx, (float)y, (float)half, (float)triH, isLeft);

            var fill = isLeft ? FlickFillLeftPaint : FlickFillRightPaint;
            var stroke = isLeft ? FlickStrokeLeftPaint : FlickStrokeRightPaint;

            canvas.DrawPath(path, fill);
            canvas.DrawPath(path, stroke);
            if (selected)
            {
                canvas.DrawPath(path, SelectedFillPaint);
                canvas.DrawPath(path, SelectedStrokePaint);
            }
            else if (hovered)
            {
                canvas.DrawPath(path, HoverFillPaint);
            }
        }

        private void DrawSkyArea(SKCanvas canvas, Rect sky, RenderItem item, double pxPerMs, double yStart, bool selected, bool hovered)
        {
            var skyRect = ToSkRect(sky);
            var path = SpcSkiaGeometryBuilder.BuildSkyAreaPath(skyRect, item, pxPerMs);
            canvas.Save();
            canvas.Translate(0, (float)yStart);
            canvas.DrawPath(path, SkyAreaFillPaint);
            canvas.DrawPath(path, SkyAreaStrokePaint);
            if (selected)
            {
                canvas.DrawPath(path, SelectedFillPaint);
                canvas.DrawPath(path, SelectedStrokePaint);
            }
            else if (hovered)
            {
                canvas.DrawPath(path, HoverFillPaint);
            }
            canvas.Restore();
        }

        private void DrawFps(SKCanvas canvas)
        {
            DrawText(canvas, $"FPS: {_fps}", (float)(_w - 80), 10, FpsPaint);
        }

        private static SKRect ToSkRect(Rect rect)
        {
            return new SKRect((float)rect.Left, (float)rect.Top, (float)rect.Right, (float)rect.Bottom);
        }

        private static void DrawText(SKCanvas canvas, string text, float x, float y, SKPaint paint)
        {
            var metrics = paint.FontMetrics;
            float baseline = y - metrics.Ascent;
            canvas.DrawText(text, x, baseline, paint);
        }
    }
}
