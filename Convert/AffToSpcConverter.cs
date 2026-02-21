using AffToSpcConverter.Models;
using AffToSpcConverter.Parsing;
using AffToSpcConverter.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AffToSpcConverter.Convert;

public static class AffToSpcConverter
{
    public static ConversionResult Convert(AffChart chart, ConverterOptions options)
    {
        // ---- Parse ----
        // var chart = AffParser.Parse(affText); // Removed re-parsing

        // ---- Base timing ----
        var baseTiming = chart.Timings.FirstOrDefault() ?? new AffTiming(0, 120.0, 4.0);
        double baseBpm = baseTiming.Bpm;

        var events = new List<ISpcEvent>
        {
            new SpcChart(baseTiming.Bpm, baseTiming.Beats)
        };

        // Optional: disable lanes 0 & 4 (your older feature)
        if (options.DisableLanes)
        {
            events.Add(new SpcLane(0, 0, 0));
            events.Add(new SpcLane(0, 4, 0));
        }

        // ---- Ground notes ----
        var taps = new List<SpcTap>();
        var holds = new List<SpcHold>();

        foreach (var n in chart.Notes)
        {
            int lane = MapLane(n.Lane, options);
            // Was: new SpcTap(n.TimeMs, lane, 1) -> (time, lane, kind/width)
            // Now: new SpcTap(n.TimeMs, 1, lane) -> (time, kind, lane)
            // Assuming "1" is the default kind (width?)
            taps.Add(new SpcTap(n.TimeMs, 1, lane));
        }

        foreach (var h in chart.Holds)
        {
            int lane = MapLane(h.Lane, options);
            int dur = Math.Max(0, h.T2Ms - h.T1Ms);
            holds.Add(new SpcHold(h.T1Ms, lane, 1, dur));
        }

        // ---- Sky notes ----
        // We separate raw lists, then do post-process passes.
        var skyAreas = new List<SpcSkyArea>();
        var flicks = new List<SpcFlick>();

        int den = Math.Max(1, options.Denominator);
        int baseSkyWidthNum = MathUtil.ClampInt((int)Math.Round(options.SkyWidthRatio * den), 1, den);

        int nextGroupId = 1;

        foreach (var a in chart.Arcs)
        {
            // Arcaea arc -> InFalsus skyarea OR flick group (by Skyline flag per your rule)
            if (a.Skyline)
            {
                // Skyline arc contains arctaps -> flick(s)
                foreach (var tn in a.ArcTapTimesMs)
                {
                    double x = EasingUtil.EvalArcX(a, tn);
                    x = MapX(x, options.XMapping);

                    // Clamp by width so it always stays visible
                    int widthNum = baseSkyWidthNum;
                    int posNum = QuantizeXClampedByWidth(x, widthNum, den);

                    int dir = EasingUtil.DirectionFromArc(a, tn);
                    flicks.Add(new SpcFlick(tn, posNum, den, widthNum, dir));
                }
            }
            else
            {
                // Normal arc -> skyarea segment (single segment per arc)
                double x1 = MapX(a.X1, options.XMapping);
                double x2 = MapX(a.X2, options.XMapping);

                int w1 = baseSkyWidthNum;
                int w2 = baseSkyWidthNum;

                int x1n = QuantizeXClampedByWidth(x1, w1, den);
                int x2n = QuantizeXClampedByWidth(x2, w2, den);

                var (le, re) = EasingUtil.SlideTokenToSpcEdgeCodes(a.SlideEasing);

                int dur = Math.Max(0, a.T2Ms - a.T1Ms);
                skyAreas.Add(new SpcSkyArea(
                    a.T1Ms,
                    x1n, den, w1,
                    x2n, den, w2,
                    le, re,
                    dur,
                    nextGroupId++
                ));
            }
        }

        // -------------------------
        // Post-process passes (核心优化都在这里)
        // -------------------------

        // (1) 合并同一时间段的多条 skyarea（双手蛇）
        if (options.MergeConcurrentSkyAreas)
        {
            skyAreas = PatternUtils.MergeSkyAreasBySameWindow(skyAreas);
        }

        // (1b) 同时刻多 flick（双 arctap）处理：保留一个 flick，其他落地 0/5 轨
        if (options.ResolveSimultaneousFlicksToGround)
        {
            PatternUtils.ResolveSimultaneousFlicksToGround(
                flicks,
                taps,
                options.DisableLanes
            );
        }

        // (2) flick 的宽度/方向优化：密集段加宽 + 方向交替
        PatternUtils.ApplyFlickReadabilityStyle(
            flicks,
            baseBpm,
            den,
            baseSkyWidthNum,
            options
        );

        // (3) 大小键：只在“纯 tap 密集段”启用，并避开 hold 活动区间
        if (options.TapWidthPatternEnabled)
        {
            int denseMs = options.DenseTapThresholdMs > 0
                ? options.DenseTapThresholdMs
                : PatternUtils.DefaultDenseThresholdMs(baseBpm);

            PatternUtils.ApplyTapWidthPatternSafely(
                taps,
                holds,
                denseMs,
                options.TapWidthPattern,
                allowedLanes: options.DisableLanes ? new[] { 1, 2, 3, 5 } : new[] { 1, 2, 3, 4, 0, 5 }
            );
        }

        // (3b) hold width random（保留你原功能）
        if (options.HoldWidthRandomEnabled)
        {
            var rng = RandomUtil.Create(options.RandomSeed);
            PatternUtils.RandomizeHoldWidthInPlace(holds, rng, options.HoldWidthRandomMax);
        }

        // ---- Collect ----
        events.AddRange(taps);
        events.AddRange(holds);
        events.AddRange(skyAreas);
        events.AddRange(flicks);

        // Stable ordering
        var ordered = events
            .OrderBy(e => e.TimeMs)
            .ThenBy(e => (int)e.Type)
            .ToList();

        return new ConversionResult(ordered, new List<string>());
    }

    private static int MapLane(int affLane, ConverterOptions options)
    {
        // Aff lane: 1..4
        // Spc lane: 0..5
        // Your optional keymap: lane4 -> lane5 (space)
        if (options.RecommendedKeymap && affLane == 4) return 5;
        return affLane;
    }

    private static double MapX(double x, string mapping)
    {
        // arcaea x is normally [0,1], but we guard anyway
        x = MathUtil.Clamp01(x);

        return mapping switch
        {
            "compress" => 0.5 + (x - 0.5) * 0.9,
            _ => x, // "clamp01"
        };
    }

    private static int QuantizeXClampedByWidth(double x01, int widthNum, int den)
    {
        // key fix: keep center within [w/2, 1-w/2]
        double w01 = (double)widthNum / den;
        double lo = w01 * 0.5;
        double hi = 1.0 - w01 * 0.5;

        x01 = MathUtil.Clamp(x01, lo, hi);

        int n = (int)Math.Round(x01 * den);
        return MathUtil.ClampInt(n, 0, den);
    }
}