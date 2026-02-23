using AffToSpcConverter.Models;
using System;

namespace AffToSpcConverter.Utils;

public static class EasingUtil
{
    // 将 AFF 弧线缓动标记转换为 SPC 左右边缘缓动代码。
    public static (int left, int right) SlideTokenToSpcEdgeCodes(string token)
    {
        token = (token ?? "s").Trim().ToLowerInvariant();

        // In Falsus：0=直线，1/2=曲线（两种方向）
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

    // 计算指定时间点弧线的 X 坐标。
    public static double EvalArcX(AffArc a, int tMs)
    {
        if (a.T2Ms <= a.T1Ms) return a.X2;

        double u = (tMs - a.T1Ms) / (double)(a.T2Ms - a.T1Ms);
        u = MathUtil.Clamp01(u);

        double eased = ApplyEasing(a.SlideEasing, u);
        return a.X1 + (a.X2 - a.X1) * eased;
    }

    // 根据弧线走势推断 Flick 方向。
    public static int DirectionFromArc(AffArc a, int tMs)
    {
        // 通过比较未来的 x 来判断方向
        int t2 = Math.Min(a.T2Ms, tMs + 8);
        double xNow = EvalArcX(a, tMs);
        double xNext = EvalArcX(a, t2);

        if (Math.Abs(xNext - xNow) < 1e-6)
            return 4; // 默认向右

        return (xNext > xNow) ? 4 : 16;
    }

    // 按弧线缓动标记计算插值曲线。
    private static double ApplyEasing(string token, double u)
    {
        token = (token ?? "s").Trim().ToLowerInvariant();

        return token switch
        {
            "s" => u,

            "si" => Math.Sin(u * Math.PI * 0.5),              // (was OutSine)
            "so" => 1.0 - Math.Cos(u * Math.PI * 0.5),        // (was InSine)

            "sisi" => Math.Sin(u * Math.PI * 0.5),
            "soso" => 1.0 - Math.Cos(u * Math.PI * 0.5),

            "siso" => SmoothStep(u),
            "sosi" => SmoothStep(u),

            _ => u,
        };
    }

    // 计算 SmoothStep 平滑曲线值。
    private static double SmoothStep(double u)
        => u * u * (3.0 - 2.0 * u);
}