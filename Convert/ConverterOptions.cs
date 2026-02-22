namespace AffToSpcConverter.Convert;

public sealed class ConverterOptions
{
    // 基础
    public int Denominator { get; set; } = 24;
    public double SkyWidthRatio { get; set; } = 0.25;
    public string XMapping { get; set; } = "clamp01";

    public bool DisableLanes { get; set; } = false;
    public bool RecommendedKeymap { get; set; } = false;

    // 可选调整
    public bool TapWidthPatternEnabled { get; set; } = false;
    public string TapWidthPattern { get; set; } = "1,2";
    public int DenseTapThresholdMs { get; set; } = 0;

    public bool HoldWidthRandomEnabled { get; set; } = false;
    public int HoldWidthRandomMax { get; set; } = 2;
    public int RandomSeed { get; set; } = 12345;

    public bool SkyareaStrategy2 { get; set; } = false;

    // ---- 新增：可玩性修正 ----
    public bool MergeConcurrentSkyAreas { get; set; } = true;

    public bool ResolveSimultaneousFlicksToGround { get; set; } = true;

    // 滑键可读性
    public bool FlickAlternateDirectionWhenDense { get; set; } = true;
    public bool FlickDynamicWidthWhenDense { get; set; } = true;

    // 0 表示自动（从 bpm 推导 16 分音符）
    public int DenseFlickThresholdMs { get; set; } = 0;

    // 滑键基础宽度缩放（1.0 = 与天空宽度一致）
    public double FlickBaseWidthScale { get; set; } = 1.0;
}