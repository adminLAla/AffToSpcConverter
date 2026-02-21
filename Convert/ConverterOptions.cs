namespace AffToSpcConverter.Convert;

public sealed class ConverterOptions
{
    // Base
    public int Denominator { get; set; } = 24;
    public double SkyWidthRatio { get; set; } = 0.25;
    public string XMapping { get; set; } = "clamp01";

    public bool DisableLanes { get; set; } = false;
    public bool RecommendedKeymap { get; set; } = false;

    // Optional remix
    public bool TapWidthPatternEnabled { get; set; } = false;
    public string TapWidthPattern { get; set; } = "1,2";
    public int DenseTapThresholdMs { get; set; } = 0;

    public bool HoldWidthRandomEnabled { get; set; } = false;
    public int HoldWidthRandomMax { get; set; } = 2;
    public int RandomSeed { get; set; } = 12345;

    public bool SkyareaStrategy2 { get; set; } = false;

    // ---- NEW: playability fixes ----
    public bool MergeConcurrentSkyAreas { get; set; } = true;

    public bool ResolveSimultaneousFlicksToGround { get; set; } = true;

    // Flick readability
    public bool FlickAlternateDirectionWhenDense { get; set; } = true;
    public bool FlickDynamicWidthWhenDense { get; set; } = true;

    // 0 => auto from bpm (16th)
    public int DenseFlickThresholdMs { get; set; } = 0;

    // width scaling for flick base (1.0 = same as sky width)
    public double FlickBaseWidthScale { get; set; } = 1.0;
}