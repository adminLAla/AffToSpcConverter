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
    // 转换IR->chart()
    public string ToSpcLine() => $"chart({Bpm:0.00},{Beats:0.00})";
}

public sealed record SpcBpm(int TimeMs, double Bpm, double Beats) : ISpcEvent
{
    
    public SpcEventType Type => SpcEventType.Bpm;
    // 转换IR->bpm()
    public string ToSpcLine() => $"bpm({TimeMs},{Bpm:0.00},{Beats:0.00})";
}

public sealed record SpcLane(int TimeMs, int LaneIndex, int Enable) : ISpcEvent
{
    
    public SpcEventType Type => SpcEventType.Lane;
    // 转换IR->lane()
    public string ToSpcLine() => $"lane({TimeMs},{LaneIndex},{Enable})";
}

public sealed record SpcTap(int TimeMs, int Kind, int LaneIndex) : ISpcEvent
{
    
    public SpcEventType Type => SpcEventType.Tap;
    // 转换IR->tap()
    public string ToSpcLine() => $"tap({TimeMs},{Kind},{LaneIndex})";
}

public sealed record SpcHold(int TimeMs, int LaneIndex, int Width, int DurationMs) : ISpcEvent
{
    
    public SpcEventType Type => SpcEventType.Hold;
    // 转换IR->hold()
    public string ToSpcLine() => $"hold({TimeMs},{LaneIndex},{Width},{DurationMs})";
}

public sealed record SpcFlick(int TimeMs, int PosNum, int Den, int WidthNum, int Dir) : ISpcEvent
{
    
    public SpcEventType Type => SpcEventType.Flick;
    // 转换IR->flick()
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
    // 转换IR->skyarea()
    public string ToSpcLine()
        => $"skyarea({TimeMs},{X1Num},{Den1},{W1Num},{X2Num},{Den2},{W2Num},{LeftEasing},{RightEasing},{DurationMs},{GroupId})";
}