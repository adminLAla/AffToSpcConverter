using AffToSpcConverter.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AffToSpcConverter.Parsing;

public static class AffParser
{
    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    private static readonly Regex RxTiming = new(@"^\s*timing\(([-]?\d+),\s*([0-9.]+),\s*([0-9.]+)\)\s*;\s*$", RegexOptions.Compiled);
    private static readonly Regex RxNote = new(@"^\s*\((\d+),\s*(\d+)\)\s*;\s*$", RegexOptions.Compiled);
    private static readonly Regex RxHold = new(@"^\s*hold\((\d+),\s*(\d+),\s*(\d+)\)\s*;\s*$", RegexOptions.Compiled);

    private static readonly Regex RxArc = new(
        @"^\s*arc\(" +
        @"(\d+)\s*,\s*(\d+)\s*," +
        @"([-]?[0-9.]+)\s*,\s*([-]?[0-9.]+)\s*," +
        @"([a-zA-Z]+)\s*," +
        @"([-]?[0-9.]+)\s*,\s*([-]?[0-9.]+)\s*," +
        @"(\d+)\s*," +
        @"([a-zA-Z0-9_]+)\s*," +
        @"(true|false)\s*\)" +
        @"(\s*\[(.*?)\])?\s*;\s*$",
        RegexOptions.Compiled);

    private static readonly Regex RxArcTap = new(@"arctap\((\d+)\)", RegexOptions.Compiled);

    public static AffChart Parse(string affText)
    {
        var chart = new AffChart();
        var lines = affText.Replace("\r\n", "\n").Split('\n');

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var mT = RxTiming.Match(line);
            if (mT.Success)
            {
                chart.Timings.Add(new AffTiming(
                    int.Parse(mT.Groups[1].Value),
                    double.Parse(mT.Groups[2].Value, CI),
                    double.Parse(mT.Groups[3].Value, CI)
                ));
                continue;
            }

            var mN = RxNote.Match(line);
            if (mN.Success)
            {
                chart.Notes.Add(new AffNote(
                    int.Parse(mN.Groups[1].Value),
                    int.Parse(mN.Groups[2].Value)
                ));
                continue;
            }

            var mH = RxHold.Match(line);
            if (mH.Success)
            {
                chart.Holds.Add(new AffHold(
                    int.Parse(mH.Groups[1].Value),
                    int.Parse(mH.Groups[2].Value),
                    int.Parse(mH.Groups[3].Value)
                ));
                continue;
            }

            var mA = RxArc.Match(line);
            if (mA.Success)
            {
                int t1 = int.Parse(mA.Groups[1].Value);
                int t2 = int.Parse(mA.Groups[2].Value);
                double x1 = double.Parse(mA.Groups[3].Value, CI);
                double x2 = double.Parse(mA.Groups[4].Value, CI);
                string easing = mA.Groups[5].Value;
                double y1 = double.Parse(mA.Groups[6].Value, CI);
                double y2 = double.Parse(mA.Groups[7].Value, CI);
                int color = int.Parse(mA.Groups[8].Value);
                string fx = mA.Groups[9].Value;
                bool skyline = bool.Parse(mA.Groups[10].Value);

                var taps = new List<int>();
                var bracket = mA.Groups[12].Value;
                if (!string.IsNullOrWhiteSpace(bracket))
                {
                    foreach (Match mt in RxArcTap.Matches(bracket))
                        taps.Add(int.Parse(mt.Groups[1].Value));
                }

                chart.Arcs.Add(new AffArc(t1, t2, x1, x2, easing, y1, y2, color, fx, skyline, taps));
                continue;
            }

            // ignore other lines (timinggroup/scenecontrol/etc.)
        }

        return chart;
    }
}