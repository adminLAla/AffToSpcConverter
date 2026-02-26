using System;

namespace AffToSpcConverter.Utils;

// 数学辅助工具，封装常用数值计算与容差判断。
public static class MathUtil
{
    // 将数值限制在指定范围内。
    public static double Clamp(double v, double lo, double hi)
        => (v < lo) ? lo : (v > hi) ? hi : v;

    // 将数值限制在 0 到 1 范围内。
    public static double Clamp01(double v)
        => Clamp(v, 0.0, 1.0);

    // 将整数限制在指定范围内。
    public static int ClampInt(int v, int lo, int hi)
        => (v < lo) ? lo : (v > hi) ? hi : v;
}
