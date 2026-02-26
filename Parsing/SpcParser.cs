using AffToSpcConverter.Models;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace AffToSpcConverter.Parsing;

// SPC 文本解析器，负责把 .spc 文本解析为事件对象集合。
public static class SpcParser
{
    // 解析输入文本并生成Spc数据模型。
    public static List<ISpcEvent> Parse(string spcText)
    {
        var events = new List<ISpcEvent>();
        var lines = spcText.Replace("\r\n", "\n").Split('\n');
        var ci = CultureInfo.InvariantCulture;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            int parenStart = line.IndexOf('(');
            int parenEnd = line.LastIndexOf(')');
            if (parenStart < 0 || parenEnd < 0 || parenEnd < parenStart) continue;

            string type = line.Substring(0, parenStart).Trim().ToLowerInvariant();
            string argsStr = line.Substring(parenStart + 1, parenEnd - parenStart - 1);
            var args = argsStr.Split(',');

            try
            {
                switch (type)
                {
                    case "chart":
                        events.Add(new SpcChart(double.Parse(args[0], ci), double.Parse(args[1], ci)));
                        break;
                    case "bpm":
                        events.Add(new SpcBpm(int.Parse(args[0]), double.Parse(args[1], ci), double.Parse(args[2], ci)));
                        break;
                    case "lane":
                        events.Add(new SpcLane(int.Parse(args[0]), int.Parse(args[1]), int.Parse(args[2])));
                        break;
                    case "tap":
                        events.Add(new SpcTap(int.Parse(args[0]), int.Parse(args[1]), int.Parse(args[2])));
                        break;
                    case "hold":
                        events.Add(new SpcHold(int.Parse(args[0]), int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3])));
                        break;
                    case "flick":
                        events.Add(new SpcFlick(int.Parse(args[0]), int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4])));
                        break;
                    case "skyarea":
                        events.Add(new SpcSkyArea(
                            int.Parse(args[0]),
                            int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]),
                            int.Parse(args[4]), int.Parse(args[5]), int.Parse(args[6]),
                            int.Parse(args[7]), int.Parse(args[8]),
                            int.Parse(args[9]), int.Parse(args[10])
                        ));
                        break;
                }
            }
            catch { /* 忽略格式不正确的行 */ }
        }
        return events;
    }
}
