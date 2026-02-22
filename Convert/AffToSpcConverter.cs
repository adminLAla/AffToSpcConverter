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
        if (options.MappingRule == "暴力映射(不推荐)")
            return ConvertBruteForce(chart, options);

        return ConvertCustom(chart, options);
    }

    /// <summary>
    /// 暴力映射：不做 clamp、不做合法性校验、不做后处理。
    /// </summary>
    private static ConversionResult ConvertBruteForce(AffChart chart, ConverterOptions options)
    {
        // 0) 全局基础映射：忽略 Offset，只取第一个 timing
        var baseTiming = chart.Timings.FirstOrDefault() ?? new AffTiming(0, 120.0, 4.0);

        var events = new List<ISpcEvent>
        {
            new SpcChart(baseTiming.Bpm, baseTiming.Beats)
        };

        int den = Math.Max(1, options.Denominator);
        int baseSkyWidthNum = (int)Math.Round(options.SkyWidthRatio * den);

        // 1) 地面音符映射
        // note(t, lane) → tap(t, 1, lane)
        foreach (var n in chart.Notes)
        {
            events.Add(new SpcTap(n.TimeMs, 1, n.Lane));
        }

        // hold(start, end, lane) → hold(start, lane, 1, end - start)
        foreach (var h in chart.Holds)
        {
            int dur = h.T2Ms - h.T1Ms; // 暴力模式：允许 0 或负数，不修正
            events.Add(new SpcHold(h.T1Ms, h.Lane, 1, dur));
        }

        // 2) 空中音符映射
        int nextGroupId = 1;

        foreach (var a in chart.Arcs)
        {
            if (a.Skyline)
            {
                // 2.2 skylineBoolean = true → flick（天空 tap：arctap）
                foreach (var tn in a.ArcTapTimesMs)
                {
                    // 在 arctap 时刻对弧线做线性插值取 x
                    double x = EasingUtil.EvalArcX(a, tn);
                    int xNum = (int)Math.Round(x * den);
                    int wNum = baseSkyWidthNum;
                    int dir = EasingUtil.DirectionFromArc(a, tn);
                    events.Add(new SpcFlick(tn, xNum, den, wNum, dir));
                }
            }
            else
            {
                // 2.1 skylineBoolean = false → skyarea（蛇）
                int x1Num = (int)Math.Round(a.X1 * den);
                int x2Num = (int)Math.Round(a.X2 * den);
                int w1Num = baseSkyWidthNum;
                int w2Num = baseSkyWidthNum;

                var (le, re) = EasingUtil.SlideTokenToSpcEdgeCodes(a.SlideEasing);

                int dur = a.T2Ms - a.T1Ms; // 暴力模式：允许 0 或负数
                events.Add(new SpcSkyArea(
                    a.T1Ms,
                    x1Num, den, w1Num,
                    x2Num, den, w2Num,
                    le, re,
                    dur,
                    nextGroupId++
                ));
            }
        }

        // 暴力模式不做后处理，只做稳定排序
        var ordered = events
            .OrderBy(e => e.TimeMs)
            .ThenBy(e => (int)e.Type)
            .ToList();

        return new ConversionResult(ordered, new List<string>());
    }

    /// <summary>
    /// 自建规则：原有完整转换逻辑（含 clamp、后处理等）。
    /// </summary>
    private static ConversionResult ConvertCustom(AffChart chart, ConverterOptions options)
    {
        // ---- 基础节拍 ----
        var baseTiming = chart.Timings.FirstOrDefault() ?? new AffTiming(0, 120.0, 4.0);
        double baseBpm = baseTiming.Bpm;

        var events = new List<ISpcEvent>
        {
            new SpcChart(baseTiming.Bpm, baseTiming.Beats)
        };

        // 输出 BPM 变速事件（高级设置）
        if (options.OutputBpmChanges && chart.Timings.Count > 1)
        {
            foreach (var t in chart.Timings.Skip(1))
            {
                events.Add(new SpcBpm(Math.Max(0, t.OffsetMs + options.GlobalTimeOffsetMs), t.Bpm, t.Beats));
            }
        }

        // 可选：禁用 0/4 轨（旧功能）
        if (options.DisableLanes)
        {
            events.Add(new SpcLane(0, 0, 0));
            events.Add(new SpcLane(0, 4, 0));
        }

        int timeOffset = options.GlobalTimeOffsetMs;

        // ---- 地面音符 ----
        var taps = new List<SpcTap>();
        var holds = new List<SpcHold>();

        foreach (var n in chart.Notes)
        {
            int lane = MapLaneCustom(n.Lane, options.NoteLaneMapping, options);
            int kind = Math.Max(1, Math.Min(4, options.NoteDefaultKind));
            int t = Math.Max(0, n.TimeMs + timeOffset);
            taps.Add(new SpcTap(t, kind, lane));
        }

        foreach (var h in chart.Holds)
        {
            int lane = MapLaneCustom(h.Lane, options.HoldLaneMapping, options);
            int width = Math.Max(1, Math.Min(6, options.HoldDefaultWidth));
            int t1 = Math.Max(0, h.T1Ms + timeOffset);
            int dur = h.T2Ms - h.T1Ms;
            if (!options.HoldAllowNegativeDuration)
                dur = Math.Max(0, dur);

            // 高级设置：最小 hold 时长，短于此转为 tap
            if (options.MinHoldDurationMs > 0 && dur < options.MinHoldDurationMs)
            {
                taps.Add(new SpcTap(t1, Math.Max(1, Math.Min(4, width)), lane));
                continue;
            }

            holds.Add(new SpcHold(t1, lane, width, dur));
        }

        // ---- 天空音符 ----
        var skyAreas = new List<SpcSkyArea>();
        var flicks = new List<SpcFlick>();

        int den = Math.Max(1, options.Denominator);
        int baseSkyWidthNum = MathUtil.ClampInt((int)Math.Round(options.SkyWidthRatio * den), 1, den);
        int flickFixedWidthNum = MathUtil.ClampInt(options.FlickFixedWidthNum, 1, den);
        int flickRandomMax = MathUtil.ClampInt(options.FlickWidthRandomMax, flickFixedWidthNum, den);
        var flickWidthRng = options.FlickWidthMode == "random"
            ? RandomUtil.Create(options.RandomSeed)
            : null;

        int nextGroupId = 1;

        foreach (var a in chart.Arcs)
        {
            if (a.Skyline)
            {
                foreach (var tn in a.ArcTapTimesMs)
                {
                    double x = EasingUtil.EvalArcX(a, tn);
                    x = MapXCustom(x, options.ArcXMapping);

                    int widthNum = options.FlickWidthMode switch
                    {
                        "fixed" => flickFixedWidthNum,
                        "random" => flickWidthRng == null || flickRandomMax <= flickFixedWidthNum
                            ? flickFixedWidthNum
                            : flickWidthRng.Next(flickFixedWidthNum, flickRandomMax + 1),
                        _ => baseSkyWidthNum
                    };
                    int posNum = QuantizeXClampedByWidth(x, widthNum, den);

                    int dir = ResolveFlickDirection(a, tn, options);
                    int t = Math.Max(0, tn + timeOffset);
                    flicks.Add(new SpcFlick(t, posNum, den, widthNum, dir));
                }
            }
            else
            {
                double x1 = MapXCustom(a.X1, options.ArcXMapping);
                double x2 = MapXCustom(a.X2, options.ArcXMapping);

                int w1 = baseSkyWidthNum;
                int w2 = baseSkyWidthNum;

                int x1n = QuantizeXClampedByWidth(x1, w1, den);
                int x2n = QuantizeXClampedByWidth(x2, w2, den);

                var (le, re) = EasingUtil.SlideTokenToSpcEdgeCodes(a.SlideEasing);

                int dur = Math.Max(0, a.T2Ms - a.T1Ms);

                // 高级设置：最小 skyarea 时长
                if (options.MinSkyAreaDurationMs > 0 && dur < options.MinSkyAreaDurationMs)
                    continue;

                int t = Math.Max(0, a.T1Ms + timeOffset);
                skyAreas.Add(new SpcSkyArea(
                    t,
                    x1n, den, w1,
                    x2n, den, w2,
                    le, re,
                    dur,
                    nextGroupId++
                ));
            }
        }

        // -------------------------
        // 后处理（核心优化）
        // -------------------------

        // (1) 合并同一时间段的多条天空区域（双手蛇）
        if (options.MergeConcurrentSkyAreas)
        {
            skyAreas = PatternUtils.MergeSkyAreasBySameWindow(skyAreas);
        }

        // (1b) 同时刻多滑键（双 arctap）处理：保留一个滑键，其他落地 0/5 轨
        if (options.ResolveSimultaneousFlicksToGround)
        {
            PatternUtils.ResolveSimultaneousFlicksToGround(
                flicks,
                taps,
                options.DisableLanes
            );
        }

        // (2) 滑键的宽度/方向优化：密集段加宽 + 方向交替
        PatternUtils.ApplyFlickReadabilityStyle(
            flicks,
            baseBpm,
            den,
            baseSkyWidthNum,
            options
        );

        // (3) 大小键：只在"纯点按密集段"启用，并避开长按活动区间
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

        // (3b) 长按宽度随机（保留原功能）
        if (options.HoldWidthRandomEnabled)
        {
            var rng = RandomUtil.Create(options.RandomSeed);
            PatternUtils.RandomizeHoldWidthInPlace(holds, rng, options.HoldWidthRandomMax);
        }

        // ---- 高级设置：Tap 去重 ----
        if (options.DeduplicateTapThresholdMs > 0)
        {
            taps.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
            for (int i = taps.Count - 1; i > 0; i--)
            {
                if (taps[i].TimeMs - taps[i - 1].TimeMs <= options.DeduplicateTapThresholdMs
                    && taps[i].LaneIndex == taps[i - 1].LaneIndex)
                {
                    taps.RemoveAt(i);
                }
            }
        }

        // ---- 收集 ----
        events.AddRange(taps);
        events.AddRange(holds);
        events.AddRange(skyAreas);
        events.AddRange(flicks);

        // 稳定排序
        IEnumerable<ISpcEvent> ordered;
        if (options.SortMode == "typeFirst")
        {
            ordered = events
                .OrderBy(e => (int)e.Type)
                .ThenBy(e => e.TimeMs);
        }
        else
        {
            ordered = events
                .OrderBy(e => e.TimeMs)
                .ThenBy(e => (int)e.Type);
        }

        return new ConversionResult(ordered.ToList(), new List<string>());
    }

    private static int MapLaneCustom(int affLane, string laneMapping, ConverterOptions options)
    {
        if (laneMapping == "4kTo6k")
        {
            // 1→1, 2→2, 3→3, 4→5
            if (affLane == 4) return 5;
            return affLane;
        }

        // "direct" — also respect legacy RecommendedKeymap
        if (options.RecommendedKeymap && affLane == 4) return 5;
        return affLane;
    }

    private static double MapXCustom(double x, string mapping)
    {
        return mapping switch
        {
            "raw" => x,
            "compress" => 0.5 + (MathUtil.Clamp01(x) - 0.5) * 0.9,
            _ => MathUtil.Clamp01(x), // clamp01
        };
    }

    private static int ResolveFlickDirection(AffArc a, int tMs, ConverterOptions options)
    {
        return options.FlickDirectionMode switch
        {
            "alwaysRight" => 4,
            "alwaysLeft" => 16,
            _ => EasingUtil.DirectionFromArc(a, tMs), // auto
        };
    }

    private static int MapLane(int affLane, ConverterOptions options)
    {
        // Aff 轨道：1..4
        // Spc 轨道：0..5
        // 可选映射：4 -> 5
        if (options.RecommendedKeymap && affLane == 4) return 5;
        return affLane;
    }

    private static double MapX(double x, string mapping)
    {
        // Arcaea x 通常为 [0,1]，这里做保护
        x = MathUtil.Clamp01(x);

        return mapping switch
        {
            "compress" => 0.5 + (x - 0.5) * 0.9,
            _ => x, // clamp01
        };
    }

    private static int QuantizeXClampedByWidth(double x01, int widthNum, int den)
    {
        // 关键修正：中心必须落在 [w/2, 1-w/2]
        double w01 = (double)widthNum / den;
        double lo = w01 * 0.5;
        double hi = 1.0 - w01 * 0.5;

        x01 = MathUtil.Clamp(x01, lo, hi);

        int n = (int)Math.Round(x01 * den);
        return MathUtil.ClampInt(n, 0, den);
    }
}