using AffToSpcConverter.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AffToSpcConverter.Utils;

// 谱面模式工具，生成或处理常见的音符分布模式。
public static class PatternUtils
{
    // 根据 BPM 计算默认密集段阈值。
    public static int DefaultDenseThresholdMs(double bpm)
    {
        // 16 分音符时长（ms）：拍长/4
        double beatMs = 60000.0 / Math.Max(1.0, bpm);
        return (int)Math.Round(beatMs / 4.0);
    }

    // 合并同时间窗口内的天空区域。
    public static List<SpcSkyArea> MergeSkyAreasBySameWindow(List<SpcSkyArea> skyAreas)
    {
        // 按 (startTime, duration) 分组
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

            // 合并：x 取平均，宽度取最大，缓动一致则保留，否则用线性
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

    // 处理同时刻滑键并将多余项落地。
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

            // 保留第一个滑键（稳定）
            flicks.Add(list[0]);

            // 其余 -> 地面 0/5 轨（禁用时仅 5）
            for (int i = 1; i < list.Count; i++)
            {
                int lane;
                if (disableLanes)
                {
                    lane = 5;
                }
                else
                {
                    // 根据 x 方向或交替分配
                    bool right = list[i].PosNum >= (list[i].Den / 2);
                    lane = right ? 5 : 0;

                    // 交替分散（可选）
                    if ((alt++ % 2) == 1) lane = (lane == 0) ? 5 : 0;
                }

                // new SpcTap(list[i].TimeMs, 1, lane) -> (time, kind, lane)
                groundTaps.Add(new SpcTap(list[i].TimeMs, 1, lane));
            }
        }
    }

    // 优化滑键宽度与方向以提升可读性。
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

        int lastDir = 4; // 向右
        for (int i = 0; i < flicks.Count; i++)
        {
            var f = flicks[i];

            int dtPrev = (i > 0) ? f.TimeMs - flicks[i - 1].TimeMs : int.MaxValue;
            int dtNext = (i + 1 < flicks.Count) ? flicks[i + 1].TimeMs - f.TimeMs : int.MaxValue;

            bool dense = dtPrev <= denseMs || dtNext <= denseMs;

            int width = baseFlickWidth;

            if (options.FlickDynamicWidthWhenDense && dense)
            {
                // 基于 den：密集段加宽
                // 略密集 -> x2，非常密集 -> x3
                int factor = (Math.Min(dtPrev, dtNext) <= denseMs / 2) ? 3 : 2;
                width = MathUtil.ClampInt(baseFlickWidth * factor, 1, den);
            }

            int dir = f.Dir;

            if (options.FlickAlternateDirectionWhenDense && dense)
            {
                // 密集时交替方向
                dir = (lastDir == 4) ? 16 : 4;
            }

            lastDir = dir;

            flicks[i] = f with { WidthNum = width, Dir = dir };
        }
    }

    // 在安全条件下应用点按宽度模式。
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

        // 构建 hold 区间，避免 “tap + hold 混读谱”
        // 构建长按区间，避免 “点按 + 长按 混读谱”
        var holdIntervals = holds
            .Select(h => (start: h.TimeMs, end: h.TimeMs + Math.Max(0, h.DurationMs)))
            .ToList();

        bool InAnyHold(int t)
        {
            // 小谱面用线性扫描即可；大谱面可换扫描线/区间树
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

        // 仅在密集点按段内应用
        // 仅在密集 tap 段内应用
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
                // 任一长按激活时保持简单
                // 任一 hold 激活时保持简单
                taps[i] = t with { Kind = 1 };
                continue;
            }

            int dt = (lastTapTime == int.MinValue) ? int.MaxValue : (t.TimeMs - lastTapTime);
            bool dense = dt <= denseThresholdMs;

            if (!dense)
            {
                // 离开密集段时重置模式
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

    // 按随机规则原地调整长按宽度。
    public static void RandomizeHoldWidthInPlace(List<SpcHold> holds, Random rng, int maxWidth)
    {
        maxWidth = Math.Max(1, maxWidth);

        for (int i = 0; i < holds.Count; i++)
        {
            // 大部分保持宽度=1，偶尔加宽
            int roll = rng.Next(0, 100);
            int w = 1;

            if (roll < 15) w = 2;
            if (roll < 5) w = Math.Min(maxWidth, 3);

            holds[i] = holds[i] with { Width = w };
        }
    }

    // 解析相关数据并返回结果。
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
