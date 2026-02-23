namespace AffToSpcConverter.Models;

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

public interface ISpcEvent
{
    int TimeMs { get; }
    SpcEventType Type { get; }
    string ToSpcLine();
}

public sealed record SpcChart(double Bpm, double Beats) : ISpcEvent
{
    public int TimeMs => 0;
    public SpcEventType Type => SpcEventType.Chart;
    // 将事件转换为一行 SPC 文本。
    public string ToSpcLine() => $"chart({Bpm:0.00},{Beats:0.00})";
}

public sealed record SpcBpm(int TimeMs, double Bpm, double Beats) : ISpcEvent
{
    
    public SpcEventType Type => SpcEventType.Bpm;
    // 将事件转换为一行 SPC 文本。
    public string ToSpcLine() => $"bpm({TimeMs},{Bpm:0.00},{Beats:0.00})";
}

public sealed record SpcLane(int TimeMs, int LaneIndex, int Enable) : ISpcEvent
{
    
    public SpcEventType Type => SpcEventType.Lane;
    // 将事件转换为一行 SPC 文本。
    public string ToSpcLine() => $"lane({TimeMs},{LaneIndex},{Enable})";
}

public sealed record SpcTap(int TimeMs, int Kind, int LaneIndex) : ISpcEvent
{
    
    public SpcEventType Type => SpcEventType.Tap;
    // 将事件转换为一行 SPC 文本。
    public string ToSpcLine() => $"tap({TimeMs},{Kind},{LaneIndex})";
}

public sealed record SpcHold(int TimeMs, int LaneIndex, int Width, int DurationMs) : ISpcEvent
{
    
    public SpcEventType Type => SpcEventType.Hold;
    // 将事件转换为一行 SPC 文本。
    public string ToSpcLine() => $"hold({TimeMs},{LaneIndex},{Width},{DurationMs})";
}

public sealed record SpcFlick(int TimeMs, int PosNum, int Den, int WidthNum, int Dir) : ISpcEvent
{
    
    public SpcEventType Type => SpcEventType.Flick;
    // 将事件转换为一行 SPC 文本。
    public string ToSpcLine() => $"flick({TimeMs},{PosNum},{Den},{WidthNum},{Dir})";
}

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
    // 将事件转换为一行 SPC 文本。
    public string ToSpcLine()
        => $"skyarea({TimeMs},{X1Num},{Den1},{W1Num},{X2Num},{Den2},{W2Num},{LeftEasing},{RightEasing},{DurationMs},{GroupId})";
}