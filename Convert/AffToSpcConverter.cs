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
        // ---- 解析 ----
        // var chart = AffParser.Parse(affText); // 已移除重复解析

        // ---- 基础节拍 ----
        var baseTiming = chart.Timings.FirstOrDefault() ?? new AffTiming(0, 120.0, 4.0);
        double baseBpm = baseTiming.Bpm;

        var events = new List<ISpcEvent>
        {
            new SpcChart(baseTiming.Bpm, baseTiming.Beats)
        };

        // 可选：禁用 0/4 轨（旧功能）
        if (options.DisableLanes)
        {
            events.Add(new SpcLane(0, 0, 0));
            events.Add(new SpcLane(0, 4, 0));
        }

        // ---- 地面音符 ----
        var taps = new List<SpcTap>();
        var holds = new List<SpcHold>();

        foreach (var n in chart.Notes)
        {
            int lane = MapLane(n.Lane, options);
            // 原：new SpcTap(n.TimeMs, lane, 1) -> (时间, 轨道, 宽度)
            // 现：new SpcTap(n.TimeMs, 1, lane) -> (时间, 宽度, 轨道)
            // 这里默认 kind=1
            taps.Add(new SpcTap(n.TimeMs, 1, lane));
        }

        foreach (var h in chart.Holds)
        {
            int lane = MapLane(h.Lane, options);
            int dur = Math.Max(0, h.T2Ms - h.T1Ms);
            holds.Add(new SpcHold(h.T1Ms, lane, 1, dur));
        }

        // ---- 天空音符 ----
        // 先分离列表，再做后处理
        var skyAreas = new List<SpcSkyArea>();
        var flicks = new List<SpcFlick>();

        int den = Math.Max(1, options.Denominator);
        int baseSkyWidthNum = MathUtil.ClampInt((int)Math.Round(options.SkyWidthRatio * den), 1, den);

        int nextGroupId = 1;

        foreach (var a in chart.Arcs)
        {
            // Arcaea 弧线 -> InFalsus 天空区域或滑键（按 Skyline 标记）
            if (a.Skyline)
            {
                // Skyline 弧线包含 arctap -> 滑键
                foreach (var tn in a.ArcTapTimesMs)
                {
                    double x = EasingUtil.EvalArcX(a, tn);
                    x = MapX(x, options.XMapping);

                    // 按宽度夹取，保证可见
                    int widthNum = baseSkyWidthNum;
                    int posNum = QuantizeXClampedByWidth(x, widthNum, den);

                    int dir = EasingUtil.DirectionFromArc(a, tn);
                    flicks.Add(new SpcFlick(tn, posNum, den, widthNum, dir));
                }
            }
            else
            {
                // 普通弧线 -> 天空区域单段
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

        // (3) 大小键：只在“纯点按密集段”启用，并避开长按活动区间
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

        // ---- 收集 ----
        events.AddRange(taps);
        events.AddRange(holds);
        events.AddRange(skyAreas);
        events.AddRange(flicks);

        // 稳定排序
        var ordered = events
            .OrderBy(e => e.TimeMs)
            .ThenBy(e => (int)e.Type)
            .ToList();

        return new ConversionResult(ordered, new List<string>());
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