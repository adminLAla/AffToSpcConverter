using System.Collections.Generic;

namespace InFalsusSongPackStudio.Models;

// osu!mania TimingPoints 事件模型。
public sealed record OsuTimingPoint(
    int TimeMs,
    double BeatLength,
    int Meter,
    int SampleSet,
    int SampleIndex,
    int Volume,
    bool TimingChange,
    int Effects
);

// osu!mania HitObject 事件模型（仅保留转换所需字段）。
public sealed record OsuManiaHitObject(
    int X,
    int Y,
    int TimeMs,
    int TypeFlags,
    int HitSound,
    int? EndTimeMs,
    string? ObjectParams
);

// osu!mania 谱面根模型。
public sealed class OsuManiaChart
{
    public int FormatVersion { get; set; } = 14;
    public int Mode { get; set; }
    public double CircleSize { get; set; }
    public int KeyCount => System.Math.Max(1, (int)System.Math.Round(CircleSize));

    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;

    public List<OsuTimingPoint> TimingPoints { get; } = new();
    public List<OsuManiaHitObject> HitObjects { get; } = new();
    public List<string> Warnings { get; } = new();
}
