using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using AffToSpcConverter.Convert.Preview;

namespace AffToSpcConverter.Views
{
    public class SpcPreviewRenderer
    {
        public ContainerVisual RootVisual { get; } = new();
        private readonly DrawingVisual _bgVisual = new();
        private readonly DrawingVisual _rulerVisual = new();
        private readonly DrawingVisual _notesVisual = new();
        private readonly DrawingVisual _judgeVisual = new();

        private readonly SpcPreviewControl _owner;

        private Rect _sky;
        private Rect _ground;
        private double _judgeY;
        private double _contentLeft;
        private double _contentW;
        private double _panelH;
        private double _w;
        private double _h;
        private double _judgeTimeMs;

        // Cached brushes & pens
        private static readonly Brush _bgBrush         = F(new SolidColorBrush(Color.FromRgb(15, 15, 18)));
        private static readonly Brush _panelBgBrush    = F(new SolidColorBrush(Color.FromRgb(18, 18, 24)));
        private static readonly Pen   _borderPen       = FP(new Pen(new SolidColorBrush(Color.FromRgb(55, 55, 70)), 1));
        private static readonly Pen   _gridPen         = FP(new Pen(new SolidColorBrush(Color.FromRgb(35, 35, 46)), 1));
        private static readonly Brush _labelBrush      = F(new SolidColorBrush(Color.FromRgb(120, 120, 140)));
        private static readonly Pen   _measurePen      = FP(new Pen(new SolidColorBrush(Color.FromRgb(80, 86, 112)), 1.8));
        private static readonly Pen   _beatPen         = FP(new Pen(new SolidColorBrush(Color.FromRgb(55, 58, 78)), 1.4) { DashStyle = DashStyles.Dot });
        private static readonly Brush _rulerLabelBrush = F(new SolidColorBrush(Color.FromRgb(130, 130, 155)));
        private static readonly Pen   _judgePen        = FP(new Pen(new SolidColorBrush(Color.FromRgb(220, 60, 30)), 1.5));
        private static readonly Brush _judgeBrush      = F(new SolidColorBrush(Color.FromRgb(220, 60, 30)));
        private static readonly Brush _tapFill         = F(new SolidColorBrush(Color.FromRgb(100, 210, 255)));
        private static readonly Pen   _tapPen          = FP(new Pen(new SolidColorBrush(Color.FromRgb(180, 240, 255)), 1));
        private static readonly Brush _holdFill        = F(new SolidColorBrush(Color.FromArgb(110, 60, 170, 230)));
        private static readonly Pen   _holdPen         = FP(new Pen(new SolidColorBrush(Color.FromArgb(180, 100, 210, 255)), 1));
        
        // Flick colors
        private static readonly Brush _flickFillLeft   = F(new SolidColorBrush(Color.FromRgb(220, 200, 80))); // Yellow
        private static readonly Pen   _flickPenLeft    = FP(new Pen(new SolidColorBrush(Color.FromRgb(255, 240, 120)), 1.5));
        private static readonly Brush _flickFillRight  = F(new SolidColorBrush(Color.FromRgb(80, 200, 120))); // Green
        private static readonly Pen   _flickPenRight   = FP(new Pen(new SolidColorBrush(Color.FromRgb(120, 240, 160)), 1.5));

        private static readonly Brush _skyAreaFill     = F(new SolidColorBrush(Color.FromArgb(90, 140, 100, 230)));
        private static readonly Pen   _skyAreaPen      = FP(new Pen(new SolidColorBrush(Color.FromArgb(200, 180, 150, 255)), 1));
        private static readonly Brush _selectedFill    = F(new SolidColorBrush(Color.FromArgb(80, 255, 220, 60)));
        private static readonly Pen   _selectedPen     = FP(new Pen(new SolidColorBrush(Color.FromRgb(255, 220, 60)), 2));
        private static readonly Brush _hoveredFill     = F(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)));

        private static Brush F(SolidColorBrush b)  { b.Freeze(); return b; }
        private static Pen   FP(Pen p)             { p.Freeze();  return p; }
        private static readonly Typeface _mono = new("Consolas");

        public SpcPreviewRenderer(SpcPreviewControl owner)
        {
            _owner = owner;
            RootVisual.Children.Add(_bgVisual);
            RootVisual.Children.Add(_rulerVisual);
            RootVisual.Children.Add(_notesVisual);
            RootVisual.Children.Add(_judgeVisual);
        }

        public void RebuildAll(Rect sky, Rect ground, double judgeY, double contentLeft, double contentW, double panelH, double w, double h)
        {
            _sky = sky;
            _ground = ground;
            _judgeY = judgeY;
            _contentLeft = contentLeft;
            _contentW = contentW;
            _panelH = panelH;
            _w = w;
            _h = h;
            _judgeTimeMs = _owner.JudgeTimeMs;

            var clip = new RectangleGeometry(new Rect(contentLeft, 8.0, contentW, panelH));
            clip.Freeze();
            _rulerVisual.Clip = clip;
            _notesVisual.Clip = clip;

            RebuildBg();
            RebuildJudge();
            RebuildRuler();
            RebuildNotes();
        }

        public void UpdateTime(double judgeTimeMs)
        {
            if (Math.Abs(_judgeTimeMs - judgeTimeMs) < 0.1) return;
            _judgeTimeMs = judgeTimeMs;
            
            // If layout hasn't happened yet, don't try to render
            if (_w <= 0 || _h <= 0) return;

            RebuildRuler();
            RebuildNotes();
        }

        private void RebuildBg()
        {
            using var dc = _bgVisual.RenderOpen();
            dc.DrawRectangle(_bgBrush, null, new Rect(0, 0, _w, _h));
            dc.PushClip(new RectangleGeometry(new Rect(_contentLeft, 8.0, _contentW, _panelH)));

            dc.DrawRectangle(_panelBgBrush, _borderPen, _sky);
            dc.DrawRectangle(_panelBgBrush, _borderPen, _ground);

            for (int i = 1; i < 8; i++)
            {
                double x = _sky.Left + _sky.Width * i / 8.0;
                dc.DrawLine(_gridPen, new Point(x, _sky.Top), new Point(x, _sky.Bottom));
            }
            for (int i = 0; i <= 6; i++)
            {
                double x = _ground.Left + _ground.Width * i / 6.0;
                dc.DrawLine(_gridPen, new Point(x, _ground.Top), new Point(x, _ground.Bottom));
            }

            DrawText(dc, "SKY", _sky.Left + 6, _sky.Top + 4, _labelBrush, 11);
            DrawText(dc, "GROUND", _ground.Left + 6, _ground.Top + 4, _labelBrush, 11);
            dc.Pop();
        }

        private void RebuildJudge()
        {
            using var dc = _judgeVisual.RenderOpen();
            dc.DrawLine(_judgePen, new Point(_contentLeft, _judgeY), new Point(_w, _judgeY));
            DrawText(dc, "JUDGE", _contentLeft + 4, _judgeY + 3, _judgeBrush, 11);
        }

        private void RebuildRuler()
        {
            using var dc = _rulerVisual.RenderOpen();
            var model = _owner.Model;
            if (model == null || model.Bpm <= 0 || model.Beats <= 0) return;

            double msPerBeat = 60000.0 / model.Bpm;
            double msPerMeasure = msPerBeat * model.Beats;
            double pxPerMs = _owner.PxPerMs;

            double chosenMs = msPerMeasure;
            foreach (var m in new[] { 0.25, 0.5, 1.0, 2.0, 4.0, 8.0, 16.0, 32.0 })
            {
                double c = msPerMeasure * m;
                if (c * pxPerMs >= 80) { chosenMs = c; break; }
            }

            double topY = _sky.Top;
            double bottomY = _sky.Bottom;
            
            double tMin = _judgeTimeMs + (_judgeY - bottomY) / pxPerMs;
            double tMax = _judgeTimeMs + (_judgeY - topY) / pxPerMs;

            int startM = (int)Math.Floor(tMin / chosenMs);
            int endM = (int)Math.Ceiling(tMax / chosenMs);

            for (int m = startM; m <= endM; m++)
            {
                double t = m * chosenMs;
                double y = _judgeY - (t - _judgeTimeMs) * pxPerMs;
                if (y < topY - 2 || y > bottomY + 2) continue;
                dc.DrawLine(_measurePen, new Point(_sky.Left, y), new Point(_ground.Right, y));
                DrawText(dc, $"#{t / msPerMeasure:0.##}", 2, y - 10, _rulerLabelBrush, 10);
            }

            double pxPerBeat = msPerBeat * pxPerMs;
            double subMs = 0;
            if (pxPerBeat >= 28) subMs = msPerBeat / 4.0;
            else if (pxPerBeat >= 16) subMs = msPerBeat / 2.0;
            else if (pxPerBeat >= 10) subMs = msPerBeat;

            if (subMs > 0)
            {
                int startS = (int)Math.Floor(tMin / subMs);
                int endS = (int)Math.Ceiling(tMax / subMs);
                for (int s = startS; s <= endS; s++)
                {
                    double t = s * subMs;
                    if (Math.Abs(t % chosenMs) < 1.0) continue;
                    double y = _judgeY - (t - _judgeTimeMs) * pxPerMs;
                    if (y < topY - 1 || y > bottomY + 1) continue;
                    dc.DrawLine(_beatPen, new Point(_sky.Left, y), new Point(_ground.Right, y));
                }
            }
        }

        private void RebuildNotes()
        {
            using var dc = _notesVisual.RenderOpen();
            var model = _owner.Model;
            if (model == null) return;

            var items = model.Items;
            double pxPerMs = _owner.PxPerMs;
            
            double clipTop = _sky.Top - 50;
            double clipBottom = _judgeY + 50;

            for (int idx = 0; idx < items.Count; idx++)
            {
                var item = items[idx];
                double yStart = _judgeY - (item.TimeMs - _judgeTimeMs) * pxPerMs;
                double yEnd = _judgeY - (item.EndTimeMs - _judgeTimeMs) * pxPerMs;

                double top = Math.Min(yStart, yEnd);
                double bottom = Math.Max(yStart, yEnd);

                if (bottom < clipTop || top > clipBottom)
                    continue;

                bool isSelected = idx == _owner.SelectedItemIndex;
                bool isHovered = false;

                switch (item.Type)
                {
                    case RenderItemType.GroundTap:
                        DrawGroundTap(dc, _ground, yStart, item.Lane, item.Kind, isSelected, isHovered);
                        break;
                    case RenderItemType.GroundHold:
                        DrawGroundHold(dc, _ground, item, yStart, yEnd, isSelected, isHovered);
                        break;
                    case RenderItemType.SkyFlick:
                        DrawSkyFlick(dc, _sky, yStart, item, isSelected, isHovered);
                        break;
                    case RenderItemType.SkyArea:
                        DrawSkyArea(dc, _sky, item, pxPerMs, yStart, isSelected, isHovered);
                        break;
                }
            }
        }

        private void DrawGroundTap(DrawingContext dc, Rect ground, double y, int lane, int kind, bool selected, bool hovered)
        {
            lane = Math.Clamp(lane, 0, 5);
            kind = Math.Clamp(kind, 1, 4);
            int leftLane = lane;
            if (leftLane + kind > 6) leftLane = 6 - kind;

            double x0 = ground.Left + ground.Width * leftLane / 6.0;
            double x1 = ground.Left + ground.Width * (leftLane + kind) / 6.0;
            
            double width = (x1 - x0) - 4;
            if (width <= 0) return; // Prevent ArgumentException when ground width is 0

            var rect = new Rect(x0 + 2, y - 5, width, 10);

            dc.DrawRoundedRectangle(_tapFill, _tapPen, rect, 3, 3);
            if (selected) dc.DrawRoundedRectangle(_selectedFill, _selectedPen, rect, 3, 3);
            else if (hovered) dc.DrawRoundedRectangle(_hoveredFill, null, rect, 3, 3);
        }

        private void DrawGroundHold(DrawingContext dc, Rect ground, RenderItem item, double y0, double y1, bool selected, bool hovered)
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
            if (width <= 0) return; // Prevent ArgumentException

            var body = new Rect(x0 + 5, top, width, bottom - top);
            dc.DrawRectangle(_holdFill, _holdPen, body);
            if (selected) dc.DrawRectangle(_selectedFill, _selectedPen, body);
            else if (hovered) dc.DrawRectangle(_hoveredFill, null, body);

            DrawGroundTap(dc, ground, y0, item.Lane, item.Kind, selected, hovered);
        }

        private void DrawSkyFlick(DrawingContext dc, Rect sky, double y, RenderItem item, bool selected, bool hovered)
        {
            int den = Math.Max(1, item.Den);
            double cx = sky.Left + sky.Width * Math.Clamp(item.X0 / (double)den, 0, 1);
            double wPx = sky.Width * Math.Clamp(item.W0 / (double)den, 0, 1);
            double half = Math.Max(20, wPx * 0.5);
            const double triH = 45; // Increased thickness

            bool isLeft = item.Dir == 16;
            var geo = SpcGeometryBuilder.BuildFlickGeo(cx, y, half, triH, isLeft);
            
            var fill = isLeft ? _flickFillLeft : _flickFillRight;
            var pen = isLeft ? _flickPenLeft : _flickPenRight;

            dc.DrawGeometry(fill, pen, geo);
            if (selected) dc.DrawGeometry(_selectedFill, _selectedPen, geo);
            else if (hovered) dc.DrawGeometry(_hoveredFill, null, geo);
        }

        private void DrawSkyArea(DrawingContext dc, Rect sky, RenderItem item, double pxPerMs, double yStart, bool selected, bool hovered)
        {
            var geo = SpcGeometryBuilder.BuildSkyAreaGeo(sky, item, pxPerMs);
            dc.PushTransform(new TranslateTransform(0, yStart));
            dc.DrawGeometry(_skyAreaFill, _skyAreaPen, geo);
            if (selected) dc.DrawGeometry(_selectedFill, _selectedPen, geo);
            else if (hovered) dc.DrawGeometry(_hoveredFill, null, geo);
            dc.Pop();
        }

        private void DrawText(DrawingContext dc, string text, double x, double y, Brush brush, double size = 12)
        {
            var dpi = VisualTreeHelper.GetDpi(_owner).PixelsPerDip;
            var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _mono, size, brush, dpi);
            dc.DrawText(ft, new Point(x, y));
        }
    }
}