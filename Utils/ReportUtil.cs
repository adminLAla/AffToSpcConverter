using AffToSpcConverter.ViewModels;
using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AffToSpcConverter.Utils;

// 统计信息生成器，用于从 SPC 文本和当前状态生成摘要报告。
public static class ReportUtil
{
    // 生成转换结果的简要统计报告。
    public static string BuildSimpleReport(string spcText, MainViewModel vm)
    {
        var lines = spcText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        int Count(string key) => lines.Count(l => l.Trim().StartsWith(key, StringComparison.OrdinalIgnoreCase));

        // 解析 chart(bpm,beats)
        (double? bpm, double? beats) ParseChart()
        {
            var line = lines.Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("chart(", StringComparison.OrdinalIgnoreCase));
            if (line == null) return (null, null);

            // 示例：chart(180.00,4.00)
            int a = line.IndexOf('(');
            int b = line.LastIndexOf(')');
            if (a < 0 || b < 0 || b <= a) return (null, null);

            var inner = line.Substring(a + 1, b - a - 1);
            var parts = inner.Split(',');
            if (parts.Length < 2) return (null, null);

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var bpm)) return (null, null);
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var beats)) return (null, null);

            return (bpm, beats);
        }

        var (chartBpm, chartBeats) = ParseChart();

        var sb = new StringBuilder();
        sb.AppendLine("=== 统计概览 ===");
        sb.AppendLine($"chart: {Count("chart(")}");
        sb.AppendLine($"bpm-events: {Count("bpm(")}"); // 若以后增加 bpm(time,bpm,beats)，这里也能统计
        sb.AppendLine($"chart.bpm: {(chartBpm.HasValue ? chartBpm.Value.ToString("0.00", CultureInfo.InvariantCulture) : "N/A")}");
        sb.AppendLine($"chart.beats: {(chartBeats.HasValue ? chartBeats.Value.ToString("0.00", CultureInfo.InvariantCulture) : "N/A")}");
        sb.AppendLine($"lane: {Count("lane(")}");
        sb.AppendLine($"tap: {Count("tap(")}");
        sb.AppendLine($"hold: {Count("hold(")}");
        sb.AppendLine($"skyarea: {Count("skyarea(")}");
        sb.AppendLine($"flick: {Count("flick(")}");
        sb.AppendLine();

        sb.AppendLine("=== 配置参数（规则设置）===");
        sb.AppendLine("[基础参数]");
        sb.AppendLine($"分母（Denominator）={vm.Denominator}");
        sb.AppendLine($"Sky 宽度比例（SkyWidthRatio）={vm.SkyWidthRatio}");
        sb.AppendLine($"X 坐标映射（XMapping）={vm.XMapping}");
        sb.AppendLine();

        sb.AppendLine("[选项菜单]");
        sb.AppendLine($"推荐键位映射 (4轨→5轨)（RecommendedKeymap）={vm.RecommendedKeymap}");
        sb.AppendLine($"禁用轨道 (0 & 4)（DisableLanes）={vm.DisableLanes}");
        sb.AppendLine();

        sb.AppendLine("[难度 / 混音（可选）]");
        sb.AppendLine($"启用 Tap 宽度样式（TapWidthPatternEnabled）={vm.TapWidthPatternEnabled}");
        sb.AppendLine($"样式（TapWidthPattern）={vm.TapWidthPattern}");
        sb.AppendLine($"密集阻值（DenseTapThresholdMs）={vm.DenseTapThresholdMs}");
        sb.AppendLine($"随机化 Hold 宽度（HoldWidthRandomEnabled）={vm.HoldWidthRandomEnabled}");
        sb.AppendLine($"最大 Hold 宽度（HoldWidthRandomMax）={vm.HoldWidthRandomMax}");
        sb.AppendLine($"随机稿子（RandomSeed）={vm.RandomSeed}");
        sb.AppendLine();

        sb.AppendLine("[天空（可选）]");
        sb.AppendLine($"SkyArea 第二策略（表现型）（SkyareaStrategy2）={vm.SkyareaStrategy2}");

        return sb.ToString();
    }
}
