using AffToSpcConverter.Models;
using System;

namespace AffToSpcConverter.Utils;

public static class EasingUtil
{
    // spc: 0 line, 11 / 22 are curve codes (as you inferred)
    public static (int left, int right) SlideTokenToSpcEdgeCodes(string token)
    {
        token = (token ?? "s").Trim().ToLowerInvariant();

        // In Falsus: 0 = line, 1/2 = curve (two directions)
        return token switch
        {
            "s" => (0, 0),
            "si" => (1, 1),
            "so" => (2, 2),

            "sisi" => (1, 1),
            "soso" => (2, 2),

            "siso" => (1, 2),
            "sosi" => (2, 1),

            _ => (0, 0),
        };
    }

    public static double EvalArcX(AffArc a, int tMs)
    {
        if (a.T2Ms <= a.T1Ms) return a.X2;

        double u = (tMs - a.T1Ms) / (double)(a.T2Ms - a.T1Ms);
        u = MathUtil.Clamp01(u);

        double eased = ApplyEasing(a.SlideEasing, u);
        return a.X1 + (a.X2 - a.X1) * eased;
    }

    public static int DirectionFromArc(AffArc a, int tMs)
    {
        // compare near-future x to decide direction
        int t2 = Math.Min(a.T2Ms, tMs + 8);
        double xNow = EvalArcX(a, tMs);
        double xNext = EvalArcX(a, t2);

        if (Math.Abs(xNext - xNow) < 1e-6)
            return 4; // default right

        return (xNext > xNow) ? 4 : 16;
    }

    private static double ApplyEasing(string token, double u)
    {
        token = (token ?? "s").Trim().ToLowerInvariant();

        return token switch
        {
            "s" => u,
            "si" => u * u,
            "so" => 1.0 - (1.0 - u) * (1.0 - u),

            // For mixed / complex: use smoothstep so it stays nice & monotonic
            "sisi" => u * u,
            "soso" => 1.0 - (1.0 - u) * (1.0 - u),
            "siso" => SmoothStep(u),
            "sosi" => SmoothStep(u),

            _ => u,
        };
    }

    private static double SmoothStep(double u)
        => u * u * (3.0 - 2.0 * u);
}