using System.Collections.Generic;

namespace AffToSpcConverter.Models;

public sealed record AffTiming(int OffsetMs, double Bpm, double Beats);
public sealed record AffNote(int TimeMs, int Lane);
public sealed record AffHold(int T1Ms, int T2Ms, int Lane);

public sealed record AffArc(
    int T1Ms, int T2Ms,
    double X1, double X2,
    string SlideEasing,
    double Y1, double Y2,
    int Color,
    string Fx,
    bool Skyline,
    List<int> ArcTapTimesMs
);

public sealed class AffChart
{
    public List<AffTiming> Timings { get; } = new();
    public List<AffNote> Notes { get; } = new();
    public List<AffHold> Holds { get; } = new();
    public List<AffArc> Arcs { get; } = new();
}