using System;

namespace AffToSpcConverter.Utils;

// 随机数工具，统一项目内随机选择与随机分布逻辑。
public static class RandomUtil
{
    // 创建随机数生成器实例。
    public static Random Create(int seed) => new Random(seed);
}
