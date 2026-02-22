namespace AffToSpcConverter.Convert;

public sealed class ConverterOptions
{
    // 映射规则
    public string MappingRule { get; set; } = "自建规则";

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

    // ---- 可玩性修正 ----
    public bool MergeConcurrentSkyAreas { get; set; } = true;

    public bool ResolveSimultaneousFlicksToGround { get; set; } = true;

    // 滑键可读性
    public bool FlickAlternateDirectionWhenDense { get; set; } = true;
    public bool FlickDynamicWidthWhenDense { get; set; } = true;

    // 0 表示自动（从 bpm 推导 16 分音符）
    public int DenseFlickThresholdMs { get; set; } = 0;

    // 滑键基础宽度缩放（1.0 = 与天空宽度一致）
    public double FlickBaseWidthScale { get; set; } = 1.0;

    // ---- 自建规则：参数映射 ----
    // 地面 note 轨道映射
    public string NoteLaneMapping { get; set; } = "direct";        // direct / 4kTo6k
    // 地面 note 默认 kind(width)
    public int NoteDefaultKind { get; set; } = 1;

    // hold 轨道映射
    public string HoldLaneMapping { get; set; } = "direct";        // direct / 4kTo6k
    // hold 默认 width
    public int HoldDefaultWidth { get; set; } = 1;
    // hold 允许负时长
    public bool HoldAllowNegativeDuration { get; set; } = false;

    // arc(skyline=false) → skyarea 的 X 坐标映射
    public string ArcXMapping { get; set; } = "clamp01";           // clamp01 / compress / raw
    // arc → skyarea 是否忽略 Y
    public bool ArcIgnoreY { get; set; } = true;

    // arc(skyline=true) arctap → flick 方向推导
    public string FlickDirectionMode { get; set; } = "auto";      // auto / alwaysRight / alwaysLeft
    // flick 默认方向（当 mode = fixed 时）
    public int FlickFixedDir { get; set; } = 4;                    // 4=右, 16=左

    // flick 宽度模式：default=跟随天空宽度, fixed=固定值, random=随机
    public string FlickWidthMode { get; set; } = "default";        // default / fixed / random
    // flick 固定宽度 WidthNum（FlickWidthMode == "fixed" 时使用）
    public int FlickFixedWidthNum { get; set; } = 6;
    // flick 随机宽度最大值（FlickWidthMode == "random" 时，从 FlickFixedWidthNum 到此值随机）
    public int FlickWidthRandomMax { get; set; } = 12;

    // ---- 高级设置 ----
    // 全局时间偏移（ms）
    public int GlobalTimeOffsetMs { get; set; } = 0;
    // 最小 hold 时长（ms），短于此的 hold 转为 tap
    public int MinHoldDurationMs { get; set; } = 0;
    // 最小 skyarea 时长（ms），短于此的 skyarea 丢弃
    public int MinSkyAreaDurationMs { get; set; } = 0;
    // 是否输出 BPM 变速事件
    public bool OutputBpmChanges { get; set; } = false;
    // 是否合并极近时刻的 tap（去重阈值 ms，0=不合并）
    public int DeduplicateTapThresholdMs { get; set; } = 0;
    // 排序稳定性：按类型优先还是时间优先
    public string SortMode { get; set; } = "timeFirst";            // timeFirst / typeFirst
}