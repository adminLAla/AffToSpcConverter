using System;

namespace AffToSpcConverter.Utils;

public static class RandomUtil
{
    public static Random Create(int seed) => new Random(seed);
}