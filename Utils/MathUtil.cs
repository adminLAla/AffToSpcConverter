using System;

namespace AffToSpcConverter.Utils;

public static class MathUtil
{
    public static double Clamp(double v, double lo, double hi)
        => (v < lo) ? lo : (v > hi) ? hi : v;

    public static double Clamp01(double v)
        => Clamp(v, 0.0, 1.0);

    public static int ClampInt(int v, int lo, int hi)
        => (v < lo) ? lo : (v > hi) ? hi : v;
}