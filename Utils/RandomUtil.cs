using System;

namespace AffToSpcConverter.Utils;

public static class RandomUtil
{
    // 创建随机数生成器实例。
    public static Random Create(int seed) => new Random(seed);
}