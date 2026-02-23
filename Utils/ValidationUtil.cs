using AffToSpcConverter.Convert;
using AffToSpcConverter.Models;
using System.Collections.Generic;
using System.Linq;

namespace AffToSpcConverter.Utils;

public static class ValidationUtil
{
    // 校验转换后的 SPC 事件是否合法。
    public static void Validate(List<ISpcEvent> events, ConverterOptions opt, List<string> warnings)
    {
        // chart 必须恰好 1 个
        if (events.OfType<SpcChart>().Count() != 1)
            warnings.Add("Chart count is not exactly 1. In Falsus may reject the file.");

        // skyarea duration=1 会崩：已经在转换阶段过滤，但这里再兜底
        foreach (var s in events.OfType<SpcSkyArea>())
        {
            if (s.DurationMs <= 1)
                warnings.Add($"Skyarea duration<=1 detected at t={s.TimeMs}. This may crash the game.");
        }

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
        }
    }
}