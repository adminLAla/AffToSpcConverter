namespace InFalsusSongPackStudio.Convert;

// OSU -> SPC 转换选项（与 AFF 选项独立）。
public sealed class OsuToSpcOptions
{
    public int GlobalTimeOffsetMs { get; set; } = 0;
    public bool OutputBpmChanges { get; set; } = false;

    public int TapKind { get; set; } = 1;
    public int HoldWidth { get; set; } = 1;
}
