using AffToSpcConverter.Convert;
using AffToSpcConverter.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AffToSpcConverter.Utils;

// SPC 合法性校验结果，区分 Error 与 Warning 两级。
public sealed class SpcValidationReport
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool HasErrors => Errors.Count > 0;
}

// SPC 合法性校验工具，提供保存前校验与转换后提示检查。
public static class ValidationUtil
{
    // 解析成功的事件与其来源行号，用于后续语义级校验。
    private sealed record ParsedSpcEvent(ISpcEvent Event, int LineNo);

    // 兼容旧调用：仅输出 Warning 级别校验结果（转换后提示用）。
    public static void Validate(List<ISpcEvent> events, ConverterOptions opt, List<string> warnings)
    {
        if (events == null) throw new ArgumentNullException(nameof(events));
        if (warnings == null) throw new ArgumentNullException(nameof(warnings));
        AppendWarningsFromEvents(events, warnings);
    }

    // 保存前对 SPC 文本做非法校验（Error/Warning 双级），Error 不为 0 时应阻止导出。
    public static SpcValidationReport ValidateForSave(string spcText)
    {
        var report = new SpcValidationReport();
        if (string.IsNullOrWhiteSpace(spcText))
        {
            report.Errors.Add("SPC 文本为空。");
            return report;
        }

        var events = new List<ISpcEvent>();
        var parsedEvents = new List<ParsedSpcEvent>();
        ParseAndValidateSpcText(spcText, events, parsedEvents, report.Errors);

        // 即使存在文本错误，也继续对已成功解析的事件做检查，便于一次性暴露更多问题。
        if (events.Count > 0)
        {
            AppendErrorsFromParsedEvents(parsedEvents, report.Errors);
            AppendWarningsFromParsedEvents(parsedEvents, report.Warnings);
        }

        return report;
    }

    // 逐行校验 SPC 文本格式并尽量解析出事件对象。
    private static void ParseAndValidateSpcText(
        string spcText,
        List<ISpcEvent> events,
        List<ParsedSpcEvent> parsedEvents,
        List<string> errors)
    {
        var ci = CultureInfo.InvariantCulture;
        string[] lines = spcText.Replace("\r\n", "\n").Split('\n');

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string raw = lines[lineIndex];
            string line = raw.Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            int lineNo = lineIndex + 1;

            int illegalPos = FindIllegalCharIndex(line);
            if (illegalPos >= 0)
            {
                errors.Add($"第 {lineNo} 行含非法字符：'{line[illegalPos]}'。");
                continue;
            }

            int openCount = line.Count(c => c == '(');
            int closeCount = line.Count(c => c == ')');
            if (openCount != 1 || closeCount != 1)
            {
                errors.Add($"第 {lineNo} 行括号不匹配：应包含且仅包含一对圆括号。");
                continue;
            }

            int parenStart = line.IndexOf('(');
            int parenEnd = line.LastIndexOf(')');
            if (parenStart <= 0 || parenEnd <= parenStart)
            {
                errors.Add($"第 {lineNo} 行格式错误：无法解析事件名与参数。");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(line[(parenEnd + 1)..]))
            {
                errors.Add($"第 {lineNo} 行格式错误：右括号后存在多余内容。");
                continue;
            }

            string type = line[..parenStart].Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(type))
            {
                errors.Add($"第 {lineNo} 行格式错误：缺少事件类型。");
                continue;
            }

            string argsStr = line.Substring(parenStart + 1, parenEnd - parenStart - 1);
            string[] args = argsStr.Split(',');
            for (int i = 0; i < args.Length; i++)
                args[i] = args[i].Trim();

            if (!TryParseEventLine(type, args, ci, out var ev, out string? err))
            {
                errors.Add($"第 {lineNo} 行{(string.IsNullOrWhiteSpace(err) ? "格式错误。" : $"错误：{err}")}");
                continue;
            }

            events.Add(ev!);
            parsedEvents.Add(new ParsedSpcEvent(ev!, lineNo));
        }
    }

    // 按事件类型与参数列表解析单行事件。
    private static bool TryParseEventLine(string type, string[] args, CultureInfo ci, out ISpcEvent? ev, out string? error)
    {
        ev = null;
        error = null;
        string? localError = null;

        bool NeedArgCount(int expected)
        {
            if (args.Length == expected) return true;
            localError = $"参数数量不正确：{type} 期望 {expected} 个，实际 {args.Length} 个。";
            return false;
        }

        bool TryIntAt(int index, out int value)
        {
            if (index < 0 || index >= args.Length)
            {
                value = 0;
                localError = $"缺少第 {index + 1} 个参数。";
                return false;
            }
            if (int.TryParse(args[index], NumberStyles.Integer, ci, out value))
                return true;

            localError = $"第 {index + 1} 个参数不是合法整数：{args[index]}";
            return false;
        }

        bool TryDoubleAt(int index, out double value)
        {
            if (index < 0 || index >= args.Length)
            {
                value = 0;
                localError = $"缺少第 {index + 1} 个参数。";
                return false;
            }
            if (double.TryParse(args[index], NumberStyles.Float, ci, out value))
                return true;

            localError = $"第 {index + 1} 个参数不是合法数字：{args[index]}";
            return false;
        }

        try
        {
            switch (type)
            {
                case "chart":
                    if (!NeedArgCount(2)) return false;
                    if (!TryDoubleAt(0, out double chartBpm) || !TryDoubleAt(1, out double chartBeats)) return false;
                    ev = new SpcChart(chartBpm, chartBeats);
                    return true;

                case "bpm":
                    if (!NeedArgCount(3)) return false;
                    if (!TryIntAt(0, out int bpmTime) || !TryDoubleAt(1, out double bpm) || !TryDoubleAt(2, out double beats)) return false;
                    ev = new SpcBpm(bpmTime, bpm, beats);
                    return true;

                case "lane":
                    if (!NeedArgCount(3)) return false;
                    if (!TryIntAt(0, out int laneTime) || !TryIntAt(1, out int laneIndex) || !TryIntAt(2, out int enable)) return false;
                    ev = new SpcLane(laneTime, laneIndex, enable);
                    return true;

                case "tap":
                    if (!NeedArgCount(3)) return false;
                    if (!TryIntAt(0, out int tapTime) || !TryIntAt(1, out int kind) || !TryIntAt(2, out int lane)) return false;
                    ev = new SpcTap(tapTime, kind, lane);
                    return true;

                case "hold":
                    if (!NeedArgCount(4)) return false;
                    if (!TryIntAt(0, out int holdTime) || !TryIntAt(1, out int holdLane) || !TryIntAt(2, out int width) || !TryIntAt(3, out int duration)) return false;
                    ev = new SpcHold(holdTime, holdLane, width, duration);
                    return true;

                case "flick":
                    if (!NeedArgCount(5)) return false;
                    if (!TryIntAt(0, out int flickTime) || !TryIntAt(1, out int posNum) || !TryIntAt(2, out int den) ||
                        !TryIntAt(3, out int widthNum) || !TryIntAt(4, out int dir)) return false;
                    ev = new SpcFlick(flickTime, posNum, den, widthNum, dir);
                    return true;

                case "skyarea":
                    if (!NeedArgCount(11)) return false;
                    int[] vals = new int[11];
                    for (int i = 0; i < 11; i++)
                    {
                        if (!TryIntAt(i, out vals[i]))
                            return false;
                    }
                    ev = new SpcSkyArea(
                        vals[0], vals[1], vals[2], vals[3], vals[4], vals[5], vals[6], vals[7], vals[8], vals[9], vals[10]);
                    return true;

                default:
                    localError = $"未知事件类型：{type}";
                    return false;
            }
        }
        catch (Exception ex)
        {
            localError = ex.Message;
            return false;
        }
        finally
        {
            error = localError;
        }
    }

    // 返回首个非法字符位置；-1 表示未发现非法字符。
    private static int FindIllegalCharIndex(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
                continue;

            if (c is '(' or ')' or ',' or '.' or '-' or '+' or '_')
                continue;

            return i;
        }
        return -1;
    }

    // 保存前的 Error 级别校验（应阻止导出）。
    private static void AppendErrorsFromEvents(List<ISpcEvent> events, List<string> errors)
    {
        if (events.OfType<SpcChart>().Count() != 1)
            errors.Add("chart() 事件数量不为 1，游戏可能无法读取谱面。");

        foreach (var h in events.OfType<SpcHold>())
        {
            if (h.DurationMs <= 0)
                errors.Add($"Hold duration<=0：t={h.TimeMs}, dur={h.DurationMs}");
        }

        foreach (var s in events.OfType<SpcSkyArea>())
        {
            if (s.DurationMs <= 1)
                errors.Add($"SkyArea duration<=1（高风险崩溃）：t={s.TimeMs}, dur={s.DurationMs}");

            if (s.Den1 <= 0 || s.Den2 <= 0)
                errors.Add($"SkyArea 分母必须大于 0：t={s.TimeMs}, den1={s.Den1}, den2={s.Den2}");
        }

        foreach (var f in events.OfType<SpcFlick>())
        {
            if (f.Den <= 0)
                errors.Add($"Flick 分母必须大于 0：t={f.TimeMs}, den={f.Den}");
            if (f.WidthNum <= 0)
                errors.Add($"Flick WidthNum 必须大于 0：t={f.TimeMs}, width={f.WidthNum}");
        }
    }

    // 保存前的 Error 级别校验（带行号版本，用于点击定位）。
    private static void AppendErrorsFromParsedEvents(List<ParsedSpcEvent> parsedEvents, List<string> errors)
    {
        if (parsedEvents.Count == 0) return;

        int chartCount = parsedEvents.Count(x => x.Event is SpcChart);
        if (chartCount != 1)
            errors.Add($"chart() 事件数量不为 1（实际 {chartCount}），游戏可能无法读取谱面。");

        foreach (var h in parsedEvents.Where(x => x.Event is SpcHold).Select(x => (LineNo: x.LineNo, Event: (SpcHold)x.Event)))
        {
            if (h.Event.DurationMs <= 0)
                errors.Add($"第 {h.LineNo} 行：Hold duration<=0：t={h.Event.TimeMs}, dur={h.Event.DurationMs}");
        }

        foreach (var s in parsedEvents.Where(x => x.Event is SpcSkyArea).Select(x => (LineNo: x.LineNo, Event: (SpcSkyArea)x.Event)))
        {
            if (s.Event.DurationMs <= 1)
                errors.Add($"第 {s.LineNo} 行：SkyArea duration<=1（高风险崩溃）：t={s.Event.TimeMs}, dur={s.Event.DurationMs}");

            if (s.Event.Den1 <= 0 || s.Event.Den2 <= 0)
                errors.Add($"第 {s.LineNo} 行：SkyArea 分母必须大于 0：t={s.Event.TimeMs}, den1={s.Event.Den1}, den2={s.Event.Den2}");
        }

        foreach (var f in parsedEvents.Where(x => x.Event is SpcFlick).Select(x => (LineNo: x.LineNo, Event: (SpcFlick)x.Event)))
        {
            if (f.Event.Den <= 0)
                errors.Add($"第 {f.LineNo} 行：Flick 分母必须大于 0：t={f.Event.TimeMs}, den={f.Event.Den}");
            if (f.Event.WidthNum <= 0)
                errors.Add($"第 {f.LineNo} 行：Flick WidthNum 必须大于 0：t={f.Event.TimeMs}, width={f.Event.WidthNum}");
        }
    }

    // Warning 级别校验：不会直接阻止导出，但提示用户注意。
    private static void AppendWarningsFromEvents(List<ISpcEvent> events, List<string> warnings)
    {
        // chart 必须恰好 1 个（在保存前会被视为 Error，这里保留 Warning 兼容旧转换提示）。
        if (events.OfType<SpcChart>().Count() != 1)
            warnings.Add("Chart count is not exactly 1. In Falsus may reject the file.");

        // lane/width 合法性
        foreach (var t in events.OfType<SpcTap>())
        {
            if (t.LaneIndex < 0 || t.LaneIndex > 5) warnings.Add($"Tap lane out of range: t={t.TimeMs}, lane={t.LaneIndex}");
            if (t.Kind < 1 || t.Kind > 4) warnings.Add($"Tap kind out of range: t={t.TimeMs}, k={t.Kind}");
            if (t.LaneIndex + t.Kind - 1 > 5) warnings.Add($"Tap exceeds lane bound: t={t.TimeMs}, lane={t.LaneIndex}, k={t.Kind}");
        }

        foreach (var h in events.OfType<SpcHold>())
        {
            if (h.DurationMs <= 0) warnings.Add($"Hold duration<=0: t={h.TimeMs}, dur={h.DurationMs}");
            if (h.LaneIndex < 0 || h.LaneIndex > 5) warnings.Add($"Hold lane out of range: t={h.TimeMs}, lane={h.LaneIndex}");
            if (h.Width < 1 || h.Width > 4) warnings.Add($"Hold width out of range: t={h.TimeMs}, w={h.Width}");
            if (h.LaneIndex + h.Width - 1 > 5) warnings.Add($"Hold exceeds lane bound: t={h.TimeMs}, lane={h.LaneIndex}, w={h.Width}");
        }

        foreach (var f in events.OfType<SpcFlick>())
        {
            if (f.Dir != 4 && f.Dir != 16) warnings.Add($"Flick dir invalid: t={f.TimeMs}, dir={f.Dir}");
            if (f.WidthNum > f.Den && f.Den > 0) warnings.Add($"Flick width exceeds denominator: t={f.TimeMs}, width={f.WidthNum}, den={f.Den}");
        }

        foreach (var l in events.OfType<SpcLane>())
        {
            if (l.Enable is not 0 and not 1)
                warnings.Add($"Lane enable is not 0/1: t={l.TimeMs}, lane={l.LaneIndex}, enable={l.Enable}");
        }
    }

    // Warning 级别校验（带行号版本，用于保存前提示时辅助定位）。
    private static void AppendWarningsFromParsedEvents(List<ParsedSpcEvent> parsedEvents, List<string> warnings)
    {
        if (parsedEvents.Count == 0) return;

        int chartCount = parsedEvents.Count(x => x.Event is SpcChart);
        if (chartCount != 1)
            warnings.Add($"chart() 事件数量不为 1（实际 {chartCount}）。");

        foreach (var t in parsedEvents.Where(x => x.Event is SpcTap).Select(x => (LineNo: x.LineNo, Event: (SpcTap)x.Event)))
        {
            if (t.Event.LaneIndex < 0 || t.Event.LaneIndex > 5) warnings.Add($"第 {t.LineNo} 行：Tap lane out of range: t={t.Event.TimeMs}, lane={t.Event.LaneIndex}");
            if (t.Event.Kind < 1 || t.Event.Kind > 4) warnings.Add($"第 {t.LineNo} 行：Tap kind out of range: t={t.Event.TimeMs}, k={t.Event.Kind}");
            if (t.Event.LaneIndex + t.Event.Kind - 1 > 5) warnings.Add($"第 {t.LineNo} 行：Tap exceeds lane bound: t={t.Event.TimeMs}, lane={t.Event.LaneIndex}, k={t.Event.Kind}");
        }

        foreach (var h in parsedEvents.Where(x => x.Event is SpcHold).Select(x => (LineNo: x.LineNo, Event: (SpcHold)x.Event)))
        {
            if (h.Event.DurationMs <= 0) warnings.Add($"第 {h.LineNo} 行：Hold duration<=0: t={h.Event.TimeMs}, dur={h.Event.DurationMs}");
            if (h.Event.LaneIndex < 0 || h.Event.LaneIndex > 5) warnings.Add($"第 {h.LineNo} 行：Hold lane out of range: t={h.Event.TimeMs}, lane={h.Event.LaneIndex}");
            if (h.Event.Width < 1 || h.Event.Width > 4) warnings.Add($"第 {h.LineNo} 行：Hold width out of range: t={h.Event.TimeMs}, w={h.Event.Width}");
            if (h.Event.LaneIndex + h.Event.Width - 1 > 5) warnings.Add($"第 {h.LineNo} 行：Hold exceeds lane bound: t={h.Event.TimeMs}, lane={h.Event.LaneIndex}, w={h.Event.Width}");
        }

        foreach (var f in parsedEvents.Where(x => x.Event is SpcFlick).Select(x => (LineNo: x.LineNo, Event: (SpcFlick)x.Event)))
        {
            if (f.Event.Dir != 4 && f.Event.Dir != 16) warnings.Add($"第 {f.LineNo} 行：Flick dir invalid: t={f.Event.TimeMs}, dir={f.Event.Dir}");
            if (f.Event.WidthNum > f.Event.Den && f.Event.Den > 0) warnings.Add($"第 {f.LineNo} 行：Flick width exceeds denominator: t={f.Event.TimeMs}, width={f.Event.WidthNum}, den={f.Event.Den}");
        }

        foreach (var l in parsedEvents.Where(x => x.Event is SpcLane).Select(x => (LineNo: x.LineNo, Event: (SpcLane)x.Event)))
        {
            if (l.Event.Enable is not 0 and not 1)
                warnings.Add($"第 {l.LineNo} 行：Lane enable is not 0/1: t={l.Event.TimeMs}, lane={l.Event.LaneIndex}, enable={l.Event.Enable}");
        }
    }
}
