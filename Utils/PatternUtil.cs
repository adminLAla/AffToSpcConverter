using AffToSpcConverter.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AffToSpcConverter.Utils;

public static class PatternUtils
{
    public static int DefaultDenseThresholdMs(double bpm)
    {
        // 16th note duration in ms: beat/4
        double beatMs = 60000.0 / Math.Max(1.0, bpm);
        return (int)Math.Round(beatMs / 4.0);
    }

    // ---------------------------------------
    // (1) Merge skyareas that share same window
    // ---------------------------------------
    public static List<SpcSkyArea> MergeSkyAreasBySameWindow(List<SpcSkyArea> skyAreas)
    {
        // Keyed by (startTime, duration)
        var grouped = skyAreas
            .GroupBy(s => (s.TimeMs, s.DurationMs))
            .OrderBy(g => g.Key.TimeMs)
            .ToList();

        var result = new List<SpcSkyArea>();
        int nextGroup = 1;

        foreach (var g in grouped)
        {
            var list = g.ToList();
            if (list.Count == 1)
            {
                var s = list[0] with { GroupId = nextGroup++ };
                result.Add(s);
                continue;
            }

            // Merge: average x, take max width, easing: if一致则保留，否则线性
            int den = list[0].Den1;

            double ax1 = list.Average(s => (double)s.X1Num / den);
            double ax2 = list.Average(s => (double)s.X2Num / den);

            int w1 = list.Max(s => s.W1Num);
            int w2 = list.Max(s => s.W2Num);

            int x1n = MathUtil.ClampInt((int)Math.Round(ax1 * den), 0, den);
            int x2n = MathUtil.ClampInt((int)Math.Round(ax2 * den), 0, den);

            int le = list.All(s => s.LeftEasing == list[0].LeftEasing) ? list[0].LeftEasing : 0;
            int re = list.All(s => s.RightEasing == list[0].RightEasing) ? list[0].RightEasing : 0;

            var merged = new SpcSkyArea(
                g.Key.TimeMs,
                x1n, den, w1,
                x2n, den, w2,
                le, re,
                g.Key.DurationMs,
                nextGroup++
            );

            result.Add(merged);
        }

        return result;
    }

    // ---------------------------------------------------
    // (1b) Multiple flick at same time -> keep one, others to ground
    // ---------------------------------------------------
    public static void ResolveSimultaneousFlicksToGround(
        List<SpcFlick> flicks,
        List<SpcTap> groundTaps,
        bool disableLanes)
    {
        var groups = flicks.GroupBy(f => f.TimeMs).ToList();
        flicks.Clear();

        int alt = 0;

        foreach (var g in groups.OrderBy(x => x.Key))
        {
            var list = g.ToList();
            if (list.Count <= 1)
            {
                flicks.AddRange(list);
                continue;
            }

            // keep the first flick (stable)
            flicks.Add(list[0]);

            // others -> ground lane 0/5 (or only 5 if disableLanes)
            for (int i = 1; i < list.Count; i++)
            {
                int lane;
                if (disableLanes)
                {
                    lane = 5;
                }
                else
                {
                    // pick by x side OR alternate
                    bool right = list[i].PosNum >= (list[i].Den / 2);
                    lane = right ? 5 : 0;

                    // also alternate to spread (optional)
                    if ((alt++ % 2) == 1) lane = (lane == 0) ? 5 : 0;
                }

                // Was: new SpcTap(list[i].TimeMs, lane, 1) -> (time, lane, kind/width)
                // Now: new SpcTap(list[i].TimeMs, 1, lane) -> (time, kind, lane)
                groundTaps.Add(new SpcTap(list[i].TimeMs, 1, lane));
            }
        }
    }

    // ---------------------------------------
    // (2) Flick readability: dynamic width + alternating direction
    // ---------------------------------------
    public static void ApplyFlickReadabilityStyle(
        List<SpcFlick> flicks,
        double baseBpm,
        int den,
        int baseSkyWidthNum,
        Convert.ConverterOptions options)
    {
        if (flicks.Count == 0) return;

        int denseMs = options.DenseFlickThresholdMs > 0
            ? options.DenseFlickThresholdMs
            : DefaultDenseThresholdMs(baseBpm);

        int baseFlickWidth = MathUtil.ClampInt(
            (int)Math.Round(baseSkyWidthNum * Math.Max(0.2, options.FlickBaseWidthScale)),
            1, den
        );

        flicks.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));

        int lastDir = 4; // right
        for (int i = 0; i < flicks.Count; i++)
        {
            var f = flicks[i];

            int dtPrev = (i > 0) ? f.TimeMs - flicks[i - 1].TimeMs : int.MaxValue;
            int dtNext = (i + 1 < flicks.Count) ? flicks[i + 1].TimeMs - f.TimeMs : int.MaxValue;

            bool dense = dtPrev <= denseMs || dtNext <= denseMs;

            int width = baseFlickWidth;

            if (options.FlickDynamicWidthWhenDense && dense)
            {
                // den-based: widen in dense clusters
                // mildly dense -> x2, very dense -> x3
                int factor = (Math.Min(dtPrev, dtNext) <= denseMs / 2) ? 3 : 2;
                width = MathUtil.ClampInt(baseFlickWidth * factor, 1, den);
            }

            int dir = f.Dir;

            if (options.FlickAlternateDirectionWhenDense && dense)
            {
                // alternate direction when dense
                dir = (lastDir == 4) ? 16 : 4;
            }

            lastDir = dir;

            flicks[i] = f with { WidthNum = width, Dir = dir };
        }
    }

    // ---------------------------------------
    // (3) Tap width pattern, safely (avoid hold overlap + only in dense tap runs)
    // ---------------------------------------
    public static void ApplyTapWidthPatternSafely(
        List<SpcTap> taps,
        List<SpcHold> holds,
        int denseThresholdMs,
        string patternCsv,
        IEnumerable<int> allowedLanes)
    {
        var pattern = ParsePattern(patternCsv);
        if (pattern.Count == 0) return;

        var allowed = new HashSet<int>(allowedLanes);

        // Build hold active intervals (global) to avoid “tap + hold 混读谱”
        var holdIntervals = holds
            .Select(h => (start: h.TimeMs, end: h.TimeMs + Math.Max(0, h.DurationMs)))
            .ToList();

        bool InAnyHold(int t)
        {
            // small chart -> linear scan ok; if large, switch to sweep/interval tree later
            foreach (var it in holdIntervals)
            {
                if (t >= it.start && t <= it.end) return true;
            }
            return false;
        }

        taps.Sort((a, b) =>
        {
            int c = a.TimeMs.CompareTo(b.TimeMs);
            if (c != 0) return c;
            return a.LaneIndex.CompareTo(b.LaneIndex);
        });

        // Apply only inside dense runs of taps (by time adjacency)
        int patIdx = 0;
        int lastTapTime = int.MinValue;

        for (int i = 0; i < taps.Count; i++)
        {
            var t = taps[i];

            if (!allowed.Contains(t.LaneIndex))
            {
                taps[i] = t with { Kind = 1 };
                continue;
            }

            if (InAnyHold(t.TimeMs))
            {
                // Always keep simple when any hold is active
                taps[i] = t with { Kind = 1 };
                continue;
            }

            int dt = (lastTapTime == int.MinValue) ? int.MaxValue : (t.TimeMs - lastTapTime);
            bool dense = dt <= denseThresholdMs;

            if (!dense)
            {
                // reset pattern outside dense segments
                patIdx = 0;
                taps[i] = t with { Kind = 1 };
            }
            else
            {
                int w = pattern[patIdx % pattern.Count];
                patIdx++;
                taps[i] = t with { Kind = MathUtil.ClampInt(w, 1, 4) };
            }

            lastTapTime = t.TimeMs;
        }
    }

    // ---------------------------------------
    // (legacy) Randomize hold width
    // ---------------------------------------
    public static void RandomizeHoldWidthInPlace(List<SpcHold> holds, Random rng, int maxWidth)
    {
        maxWidth = Math.Max(1, maxWidth);

        for (int i = 0; i < holds.Count; i++)
        {
            // Keep majority width=1; occasionally widen
            int roll = rng.Next(0, 100);
            int w = 1;

            if (roll < 15) w = 2;
            if (roll < 5) w = Math.Min(maxWidth, 3);

            holds[i] = holds[i] with { Width = w };
        }
    }

    private static List<int> ParsePattern(string csv)
    {
        var res = new List<int>();
        if (string.IsNullOrWhiteSpace(csv)) return res;

        foreach (var part in csv.Split(','))
        {
            if (int.TryParse(part.Trim(), out int v))
                res.Add(v);
        }

        return res;
    }
}