using InFalsusSongPackStudio.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace InFalsusSongPackStudio.Convert;

// osu!mania -> SPC 转换入口。
public static class OsuToSpcConverter
{
    public static ConversionResult Convert(OsuManiaChart chart, OsuToSpcOptions options)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(options);

        if (chart.Mode != 3)
            throw new InvalidOperationException($"仅支持 osu!mania（Mode=3），当前 Mode={chart.Mode}。");

        int keyCount = chart.KeyCount;
        if (keyCount < 4 || keyCount > 6)
            throw new InvalidOperationException($"当前仅支持 4K-6K，检测到 {keyCount}K。\n4K->轨道1-4，5K->轨道0-4，6K->轨道0-5。");

        if (chart.HitObjects.Count == 0)
            throw new InvalidOperationException("HitObjects 为空，无法转换。\n请检查谱面文件内容。");

        var warnings = new List<string>();
        warnings.AddRange(chart.Warnings);

        var events = new List<ISpcEvent>();

        var baseTiming = chart.TimingPoints
            .Where(t => t.TimingChange && t.BeatLength > 0)
            .OrderBy(t => t.TimeMs)
            .FirstOrDefault();

        if (baseTiming is null)
            throw new InvalidOperationException("未找到有效的基础 TimingPoint（timingChange=1 且 beatLength>0）。");

        double baseBpm = 60000.0 / baseTiming.BeatLength;
        double baseBeats = Math.Max(1, baseTiming.Meter);
        events.Add(new SpcChart(baseBpm, baseBeats));

        if (options.OutputBpmChanges)
        {
            foreach (var t in chart.TimingPoints
                         .Where(x => x.TimingChange && x.BeatLength > 0)
                         .OrderBy(x => x.TimeMs)
                         .Skip(1))
            {
                int bpmTime = Math.Max(0, t.TimeMs + options.GlobalTimeOffsetMs);
                double bpm = 60000.0 / t.BeatLength;
                events.Add(new SpcBpm(bpmTime, bpm, Math.Max(1, t.Meter)));
            }
        }

        int tapKind = Math.Clamp(options.TapKind, 1, 4);
        int holdWidth = Math.Clamp(options.HoldWidth, 1, 6);

        foreach (var obj in chart.HitObjects.OrderBy(x => x.TimeMs))
        {
            int lane = mapLane(obj.X, keyCount);
            int time = Math.Max(0, obj.TimeMs + options.GlobalTimeOffsetMs);

            bool isHold = (obj.TypeFlags & 128) != 0;
            bool isCircle = (obj.TypeFlags & 1) != 0;

            if (isHold)
            {
                int endTime = obj.EndTimeMs ?? obj.TimeMs;
                int duration = Math.Max(0, endTime - obj.TimeMs);
                events.Add(new SpcHold(time, lane, holdWidth, duration));
                continue;
            }

            if (isCircle)
            {
                events.Add(new SpcTap(time, tapKind, lane));
                continue;
            }

            warnings.Add($"已跳过不支持的 HitObject 类型: time={obj.TimeMs}, type={obj.TypeFlags}");
        }

        var ordered = events
            .OrderBy(e => e.TimeMs)
            .ThenBy(e => (int)e.Type)
            .ToList();

        return new ConversionResult(ordered, warnings);
    }

    private static int mapLane(int x, int keyCount)
    {
        int clampedX = Math.Clamp(x, 0, 512);
        int column = (int)Math.Floor(clampedX * keyCount / 512.0);
        column = Math.Clamp(column, 0, keyCount - 1);

        return keyCount switch
        {
            4 => column + 1,
            5 => column,
            6 => column,
            _ => throw new InvalidOperationException($"不支持的键数: {keyCount}")
        };
    }
}
