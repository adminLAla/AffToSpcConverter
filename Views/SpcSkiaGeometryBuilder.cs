using System;
using System.Collections.Generic;
using AffToSpcConverter.Convert.Preview;
using SkiaSharp;

namespace AffToSpcConverter.Views
{
    public static class SpcSkiaGeometryBuilder
    {
        private const int MaxSkyAreaCacheEntries = 512;
        private static readonly Dictionary<int, (double pxPerMs, SKPath path, LinkedListNode<int> node)> _skyAreaGeoCache = new();
        private static readonly LinkedList<int> _skyAreaCacheOrder = new();

        public static void ClearCache()
        {
            foreach (var entry in _skyAreaGeoCache.Values)
            {
                entry.path.Dispose();
            }
            _skyAreaGeoCache.Clear();
            _skyAreaCacheOrder.Clear();
        }

        public static SKPath BuildFlickPath(float cx, float y, float half, float triH, bool leftDir)
        {
            var path = new SKPath();
            if (leftDir)
            {
                var ptLeftBottom = new SKPoint(cx - half, y);
                var ptRight = new SKPoint(cx + half, y);
                var ptLeftTop = new SKPoint(cx - half, y - triH);
                var cp = new SKPoint(cx - half * 0.6f, y);

                path.MoveTo(ptRight);
                path.LineTo(ptLeftBottom);
                path.LineTo(ptLeftTop);
                path.QuadTo(cp, ptRight);
            }
            else
            {
                var ptLeft = new SKPoint(cx - half, y);
                var ptRightBottom = new SKPoint(cx + half, y);
                var ptRightTop = new SKPoint(cx + half, y - triH);
                var cp = new SKPoint(cx + half * 0.6f, y);

                path.MoveTo(ptLeft);
                path.LineTo(ptRightBottom);
                path.LineTo(ptRightTop);
                path.QuadTo(cp, ptLeft);
            }
            path.Close();
            return path;
        }

        public static SKPath BuildSkyAreaPath(SKRect sky, RenderItem item, double pxPerMs)
        {
            int itemIdx = item.GetHashCode();
            if (_skyAreaGeoCache.TryGetValue(itemIdx, out var cached) && Math.Abs(cached.pxPerMs - pxPerMs) < 1e-6)
            {
                _skyAreaCacheOrder.Remove(cached.node);
                _skyAreaCacheOrder.AddFirst(cached.node);
                return cached.path;
            }

            int den = Math.Max(1, item.Den);
            double x0 = Math.Clamp(item.X0 / (double)den, 0, 1);
            double x1 = Math.Clamp(item.X1 / (double)den, 0, 1);
            double w0 = Math.Clamp(item.W0 / (double)den, 0, 1);
            double w1 = Math.Clamp(item.W1 / (double)den, 0, 1);

            double durMs = Math.Max(1, item.EndTimeMs - item.TimeMs);
            double pxLen = durMs * pxPerMs;
            int steps = pxLen >= 900 ? 64 : pxLen >= 450 ? 40 : pxLen >= 220 ? 28 : 18;

            var path = new SKPath();
            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps;
                double yy = -pxLen * t;
                double cL = SmoothEase(LerpEase(x0, x1, t, item.LeftEase), t);
                double ww = Lerp(w0, w1, t);
                double px = sky.Left + sky.Width * Math.Clamp(cL - ww * 0.5, 0, 1);
                if (i == 0)
                {
                    path.MoveTo((float)px, (float)yy);
                }
                else
                {
                    path.LineTo((float)px, (float)yy);
                }
            }

            for (int i = steps; i >= 0; i--)
            {
                double t = i / (double)steps;
                double yy = -pxLen * t;
                double cR = SmoothEase(LerpEase(x0, x1, t, item.RightEase), t);
                double ww = Lerp(w0, w1, t);
                double px = sky.Left + sky.Width * Math.Clamp(cR + ww * 0.5, 0, 1);
                path.LineTo((float)px, (float)yy);
            }

            path.Close();

            if (_skyAreaGeoCache.TryGetValue(itemIdx, out var old))
            {
                old.path.Dispose();
                _skyAreaCacheOrder.Remove(old.node);
            }

            var node = new LinkedListNode<int>(itemIdx);
            _skyAreaGeoCache[itemIdx] = (pxPerMs, path, node);
            _skyAreaCacheOrder.AddFirst(node);

            while (_skyAreaGeoCache.Count > MaxSkyAreaCacheEntries)
            {
                var last = _skyAreaCacheOrder.Last;
                if (last == null) break;
                int key = last.Value;
                if (_skyAreaGeoCache.TryGetValue(key, out var evicted))
                {
                    evicted.path.Dispose();
                    _skyAreaGeoCache.Remove(key);
                }
                _skyAreaCacheOrder.RemoveLast();
            }

            return path;
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
