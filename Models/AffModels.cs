using System.Collections.Generic;

namespace AffToSpcConverter.Models;

public sealed record AffTiming(
    // 该速度段开始生效的时间偏移（毫秒）。
    int OffsetMs,
    // 当前速度段的 BPM。
    double Bpm,
    // 每小节拍数（如 4 表示 4/4）。
    double Beats
);

public sealed record AffNote(
    // Tap 音符的判定时间（毫秒）。
    int TimeMs,
    // Tap 所在轨道编号。
    int Lane
);

public sealed record AffHold(
    // Hold 起始时间（毫秒）。
    int T1Ms,
    // Hold 结束时间（毫秒）。
    int T2Ms,
    // Hold 所在轨道编号。
    int Lane
);

public sealed record AffArc(
    // Arc 起始时间（毫秒）。
    int T1Ms, int T2Ms,
    // Arc 起点与终点的横向坐标（AFF 归一化坐标）。
    double X1, double X2,
    // Arc 使用的滑动缓动类型字符串（如 s、b、si 等）。
    string SlideEasing,
    // Arc 起点与终点的纵向坐标（AFF 归一化坐标）。
    double Y1, double Y2,
    // Arc 颜色通道编号。
    int Color,
    // Arc 特效标记（如 none、fx）。
    string Fx,
    // 是否为 Skyline（仅轨迹线，不生成实体 Arc）。
    bool Skyline,
    // ArcTap 的触发时间列表（毫秒）。
    List<int> ArcTapTimesMs
);

public sealed class AffChart
{
    // 谱面中的 timing 段列表。
    public List<AffTiming> Timings { get; } = new();
    // 谱面中的 Tap 音符列表。
    public List<AffNote> Notes { get; } = new();
    // 谱面中的 Hold 音符列表。
    public List<AffHold> Holds { get; } = new();
    // 谱面中的 Arc 音符列表。
    public List<AffArc> Arcs { get; } = new();
}
