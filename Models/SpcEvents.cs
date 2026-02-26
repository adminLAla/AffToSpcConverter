namespace AffToSpcConverter.Models;

// SPC 事件类型枚举。
public enum SpcEventType
{
    Chart = 0,
    Bpm = 1,
    Lane = 2,
    Hold = 3,
    Tap = 4,
    SkyArea = 5,
    Flick = 6
}

// 所有 SPC 事件的统一接口。
public interface ISpcEvent
{
    int TimeMs { get; }
    SpcEventType Type { get; }
    string ToSpcLine();
}

// chart() 头事件，定义谱面初始 BPM 与拍号。
public sealed record SpcChart(double Bpm, double Beats) : ISpcEvent
{
    public int TimeMs => 0;
    public SpcEventType Type => SpcEventType.Chart;
    // 按 SPC 语法输出 chart() 行文本。
    public string ToSpcLine() => $"chart({Bpm:0.00},{Beats:0.00})";
}

// bpm() 变速事件。
public sealed record SpcBpm(int TimeMs, double Bpm, double Beats) : ISpcEvent
{
    public SpcEventType Type => SpcEventType.Bpm;
    // 按 SPC 语法输出 bpm() 行文本。
    public string ToSpcLine() => $"bpm({TimeMs},{Bpm:0.00},{Beats:0.00})";
}

// lane() 轨道开关事件。
public sealed record SpcLane(int TimeMs, int LaneIndex, int Enable) : ISpcEvent
{
    public SpcEventType Type => SpcEventType.Lane;
    // 按 SPC 语法输出 lane() 行文本。
    public string ToSpcLine() => $"lane({TimeMs},{LaneIndex},{Enable})";
}

// tap() 点击音符事件。
public sealed record SpcTap(int TimeMs, int Kind, int LaneIndex) : ISpcEvent
{
    public SpcEventType Type => SpcEventType.Tap;
    // 按 SPC 语法输出 tap() 行文本。
    public string ToSpcLine() => $"tap({TimeMs},{Kind},{LaneIndex})";
}

// hold() 长按音符事件。
public sealed record SpcHold(int TimeMs, int LaneIndex, int Width, int DurationMs) : ISpcEvent
{
    public SpcEventType Type => SpcEventType.Hold;
    // 按 SPC 语法输出 hold() 行文本。
    public string ToSpcLine() => $"hold({TimeMs},{LaneIndex},{Width},{DurationMs})";
}

// flick() 滑键事件。
public sealed record SpcFlick(int TimeMs, int PosNum, int Den, int WidthNum, int Dir) : ISpcEvent
{
    public SpcEventType Type => SpcEventType.Flick;
    // 按 SPC 语法输出 flick() 行文本。
    public string ToSpcLine() => $"flick({TimeMs},{PosNum},{Den},{WidthNum},{Dir})";
}

// skyarea() 天空区域事件。
public sealed record SpcSkyArea(
    int TimeMs,
    int X1Num, int Den1, int W1Num,
    int X2Num, int Den2, int W2Num,
    int LeftEasing, int RightEasing,
    int DurationMs,
    int GroupId
) : ISpcEvent
{
    public SpcEventType Type => SpcEventType.SkyArea;
    // 按 SPC 语法输出 skyarea() 行文本。
    public string ToSpcLine()
        => $"skyarea({TimeMs},{X1Num},{Den1},{W1Num},{X2Num},{Den2},{W2Num},{LeftEasing},{RightEasing},{DurationMs},{GroupId})";
}
