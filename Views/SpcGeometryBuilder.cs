using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using AffToSpcConverter.Convert.Preview;

namespace AffToSpcConverter.Views
{
    public static class SpcGeometryBuilder
    {
        public static StreamGeometry BuildFlickGeo(double cx, double y, double half, double triH, bool leftDir)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                if (leftDir)
                {
                    // Vertical edge on the LEFT (Yellow)
                    var ptLeftBottom = new Point(cx - half, y);
                    var ptRight = new Point(cx + half, y);
                    var ptLeftTop = new Point(cx - half, y - triH);
                    var cp = new Point(cx - half * 0.6, y); // Pull towards bottom-left
                    
                    g.BeginFigure(ptRight, true, true);
                    g.LineTo(ptLeftBottom, true, false);
                    g.LineTo(ptLeftTop, true, false);
                    g.QuadraticBezierTo(cp, ptRight, true, false);
                }
                else
                {
                    // Vertical edge on the RIGHT (Green)
                    var ptLeft = new Point(cx - half, y);
                    var ptRightBottom = new Point(cx + half, y);
                    var ptRightTop = new Point(cx + half, y - triH);
                    var cp = new Point(cx + half * 0.6, y); // Pull towards bottom-right
                    
                    g.BeginFigure(ptLeft, true, true);
                    g.LineTo(ptRightBottom, true, false);
                    g.LineTo(ptRightTop, true, false);
                    g.QuadraticBezierTo(cp, ptLeft, true, false);
                }
            }
            geo.Freeze();
            return geo;
        }

        // Cache for SkyArea geometries based on item and PxPerMs
        private static readonly Dictionary<int, (double pxPerMs, StreamGeometry geo)> _skyAreaGeoCache = new();

        public static void ClearCache()
        {
            _skyAreaGeoCache.Clear();
        }

        public static StreamGeometry BuildSkyAreaGeo(Rect sky, RenderItem item, double pxPerMs)
        {
            int itemIdx = item.GetHashCode(); // Using hashcode as a simple ID, or we can pass an ID
            // Actually, since we rebuild everything when PxPerMs changes, we can just use the object reference hash
            
            if (_skyAreaGeoCache.TryGetValue(itemIdx, out var cached) && Math.Abs(cached.pxPerMs - pxPerMs) < 1e-6)
            {
                return cached.geo;
            }

            int den = Math.Max(1, item.Den);
            double x0 = Math.Clamp(item.X0 / (double)den, 0, 1);
            double x1 = Math.Clamp(item.X1 / (double)den, 0, 1);
            double w0 = Math.Clamp(item.W0 / (double)den, 0, 1);
            double w1 = Math.Clamp(item.W1 / (double)den, 0, 1);

            double durMs = Math.Max(1, item.EndTimeMs - item.TimeMs);
            double pxLen = durMs * pxPerMs;
            int steps = pxLen >= 900 ? 64 : pxLen >= 450 ? 40 : pxLen >= 220 ? 28 : 18;

            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                // left edge
                for (int i = 0; i <= steps; i++)
                {
                    double t = i / (double)steps;
                    double yy = -pxLen * t;
                    double cL = SmoothEase(LerpEase(x0, x1, t, item.LeftEase), t);
                    double ww = Lerp(w0, w1, t);
                    double px = sky.Left + sky.Width * Math.Clamp(cL - ww * 0.5, 0, 1);
                    if (i == 0) g.BeginFigure(new Point(px, yy), true, true);
                    else g.LineTo(new Point(px, yy), true, false);
                }

                // right edge
                for (int i = steps; i >= 0; i--)
                {
                    double t = i / (double)steps;
                    double yy = -pxLen * t;
                    double cR = SmoothEase(LerpEase(x0, x1, t, item.RightEase), t);
                    double ww = Lerp(w0, w1, t);
                    double px = sky.Left + sky.Width * Math.Clamp(cR + ww * 0.5, 0, 1);
                    g.LineTo(new Point(px, yy), true, false);
                }
            }
            geo.Freeze();
            _skyAreaGeoCache[itemIdx] = (pxPerMs, geo);
            return geo;
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
        private static double LerpEase(double a, double b, double t, int ease)
        {
            t = ease switch { 1 => t * t, 2 => 1 - (1 - t) * (1 - t), _ => t };
            return a + (b - a) * t;
        }
        private static double SmoothEase(double x, double t)
        {
            return x;
        }
    }
}