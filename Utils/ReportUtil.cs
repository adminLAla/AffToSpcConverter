using AffToSpcConverter.ViewModels;
using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AffToSpcConverter.Utils;

public static class ReportUtil
{
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
        sb.AppendLine("=== Report ===");
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

        sb.AppendLine("=== Options ===");
        sb.AppendLine($"Denominator={vm.Denominator}");
        sb.AppendLine($"SkyWidthRatio={vm.SkyWidthRatio}");
        sb.AppendLine($"XMapping={vm.XMapping}");
        sb.AppendLine($"RecommendedKeymap={vm.RecommendedKeymap}");
        sb.AppendLine($"DisableLanes={vm.DisableLanes}");
        sb.AppendLine($"TapWidthPatternEnabled={vm.TapWidthPatternEnabled}, Pattern={vm.TapWidthPattern}, DenseT={vm.DenseTapThresholdMs}");
        sb.AppendLine($"HoldWidthRandomEnabled={vm.HoldWidthRandomEnabled}, Max={vm.HoldWidthRandomMax}, Seed={vm.RandomSeed}");
        sb.AppendLine($"SkyareaStrategy2={vm.SkyareaStrategy2}");

        return sb.ToString();
    }
}