using InFalsusSongPackStudio.Models;
using System;
using System.Globalization;

namespace InFalsusSongPackStudio.Parsing;

// osu!mania .osu 文本解析器。
public static class OsuManiaParser
{
    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    public static OsuManiaChart Parse(string osuText)
    {
        if (string.IsNullOrWhiteSpace(osuText))
            throw new ArgumentException("输入的 .osu 文本为空。", nameof(osuText));

        var chart = new OsuManiaChart();
        string section = string.Empty;

        var lines = osuText.Replace("\r\n", "\n").Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (line.StartsWith("osu file format v", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(line.Substring("osu file format v".Length).Trim(), out int ver))
                    chart.FormatVersion = ver;
                continue;
            }

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                section = line;
                continue;
            }

            switch (section)
            {
                case "[General]":
                    parseGeneralLine(chart, line);
                    break;
                case "[Metadata]":
                    parseMetadataLine(chart, line);
                    break;
                case "[Difficulty]":
                    parseDifficultyLine(chart, line);
                    break;
                case "[TimingPoints]":
                    parseTimingPointLine(chart, line);
                    break;
                case "[HitObjects]":
                    parseHitObjectLine(chart, line);
                    break;
            }
        }

        return chart;
    }

    private static void parseGeneralLine(OsuManiaChart chart, string line)
    {
        var (key, value) = splitKeyValue(line);
        if (key.Equals("Mode", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int mode))
            chart.Mode = mode;
    }

    private static void parseMetadataLine(OsuManiaChart chart, string line)
    {
        var (key, value) = splitKeyValue(line);
        switch (key)
        {
            case "Title":
                chart.Title = value;
                break;
            case "Artist":
                chart.Artist = value;
                break;
            case "Creator":
                chart.Creator = value;
                break;
            case "Version":
                chart.Version = value;
                break;
        }
    }

    private static void parseDifficultyLine(OsuManiaChart chart, string line)
    {
        var (key, value) = splitKeyValue(line);
        if (key.Equals("CircleSize", StringComparison.OrdinalIgnoreCase) && double.TryParse(value, NumberStyles.Float, CI, out double cs))
            chart.CircleSize = cs;
    }

    private static void parseTimingPointLine(OsuManiaChart chart, string line)
    {
        var split = line.Split(',');
        if (split.Length < 2)
            return;

        if (!double.TryParse(split[0].Trim(), NumberStyles.Float, CI, out double t))
            return;
        if (!double.TryParse(split[1].Trim(), NumberStyles.Float, CI, out double beatLen))
            return;

        int meter = parseIntOrDefault(split, 2, 4);
        int sampleSet = parseIntOrDefault(split, 3, 1);
        int sampleIndex = parseIntOrDefault(split, 4, 0);
        int volume = parseIntOrDefault(split, 5, 100);
        bool timingChange = parseIntOrDefault(split, 6, 1) == 1;
        int effects = parseIntOrDefault(split, 7, 0);

        chart.TimingPoints.Add(new OsuTimingPoint(
            TimeMs: (int)Math.Round(t),
            BeatLength: beatLen,
            Meter: meter,
            SampleSet: sampleSet,
            SampleIndex: sampleIndex,
            Volume: volume,
            TimingChange: timingChange,
            Effects: effects
        ));
    }

    private static void parseHitObjectLine(OsuManiaChart chart, string line)
    {
        var split = line.Split(',');
        if (split.Length < 5)
            return;

        if (!int.TryParse(split[0].Trim(), out int x) ||
            !int.TryParse(split[1].Trim(), out int y) ||
            !int.TryParse(split[2].Trim(), out int t) ||
            !int.TryParse(split[3].Trim(), out int typeFlags) ||
            !int.TryParse(split[4].Trim(), out int hitSound))
        {
            chart.Warnings.Add($"跳过无法解析的 HitObject: {line}");
            return;
        }

        int? endTime = null;
        if ((typeFlags & 128) != 0 && split.Length > 5)
        {
            var holdPart = split[5];
            int colon = holdPart.IndexOf(':');
            var endText = colon >= 0 ? holdPart[..colon] : holdPart;
            if (int.TryParse(endText.Trim(), out int end))
                endTime = end;
            else
                chart.Warnings.Add($"长按结束时间解析失败: {line}");
        }

        string? objectParams = split.Length > 5 ? split[5] : null;
        chart.HitObjects.Add(new OsuManiaHitObject(x, y, t, typeFlags, hitSound, endTime, objectParams));
    }

    private static (string key, string value) splitKeyValue(string line)
    {
        int idx = line.IndexOf(':');
        if (idx < 0)
            return (line.Trim(), string.Empty);

        return (line[..idx].Trim(), line[(idx + 1)..].Trim());
    }

    private static int parseIntOrDefault(string[] split, int index, int fallback)
    {
        if (index < 0 || index >= split.Length)
            return fallback;

        return int.TryParse(split[index].Trim(), out int value) ? value : fallback;
    }
}
