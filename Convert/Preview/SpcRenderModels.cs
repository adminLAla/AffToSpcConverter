using System;
using System.Collections.Generic;
using AffToSpcConverter.Models;

namespace AffToSpcConverter.Convert.Preview
{
    public enum RenderItemType { GroundTap, GroundHold, SkyFlick, SkyArea }

    // 单个渲染元素的数据结构，描述预览中的一个可绘制对象。
    public sealed class RenderItem
    {
        public RenderItemType Type { get; init; }
        public int TimeMs { get; init; }          // 开始时间（判定线时刻）
        public int EndTimeMs { get; init; }       // 长按/天空区域用
        public float X0 { get; init; }            // 归一化/像素由界面决定，这里先用“归一化坐标”
        public float X1 { get; init; }
        public float W0 { get; init; }
        public float W1 { get; init; }
        public int Lane { get; init; }            // 地面轨道
        public int Kind { get; init; }            // 地面宽度 (1..4)
        public int Den { get; init; }             // 天空分母
        public int Dir { get; init; }             // 滑键方向 (4/16)
        public int LeftEase { get; init; }        // 天空区域
        public int RightEase { get; init; }
        public int GroupId { get; init; }

        // 指向原始 ISpcEvent，便于编辑。
        public ISpcEvent? SourceEvent { get; init; }

        // 原始事件列表中的索引（写回用）。
        public int SourceIndex { get; init; } = -1;
    }

    // 预览渲染模型，包含已排序的渲染元素与时间范围信息。
    public sealed class RenderModel
    {
        public double Bpm { get; set; }
        public double Beats { get; set; }
        public List<RenderItem> Items { get; } = new();
        public List<(int timeMs, double bpm, double beats)> BpmChanges { get; } = new();
        public int MaxItemDurationMs { get; set; }
    }
}
