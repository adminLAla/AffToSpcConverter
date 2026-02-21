using System;
using System.Collections.Generic;

namespace AffToSpcConverter.Convert.Preview
{
    public enum RenderItemType { GroundTap, GroundHold, SkyFlick, SkyArea }

    public sealed class RenderItem
    {
        public RenderItemType Type { get; init; }
        public int TimeMs { get; init; }          // 开始时间（判定线时刻）
        public int EndTimeMs { get; init; }       // hold/skyarea 用
        public float X0 { get; init; }            // 归一化/像素由 UI 决定，这里先用“归一化坐标”
        public float X1 { get; init; }
        public float W0 { get; init; }
        public float W1 { get; init; }
        public int Lane { get; init; }            // ground lane
        public int Kind { get; init; }            // ground kind (1..4)
        public int Den { get; init; }             // sky den
        public int Dir { get; init; }             // flick dir (4/16)
        public int LeftEase { get; init; }        // skyarea
        public int RightEase { get; init; }
        public int GroupId { get; init; }
    }

    public sealed class RenderModel
    {
        public double Bpm { get; init; }
        public double Beats { get; init; }
        public List<RenderItem> Items { get; } = new();
        public List<(int timeMs, double bpm, double beats)> BpmChanges { get; } = new();
    }
}