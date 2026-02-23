using AffToSpcConverter.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AffToSpcConverter.IO;

public static class SpcWriter
{
    // 将 SPC 事件列表写出为文本内容。
    public static string Write(IEnumerable<ISpcEvent> events)
    {
        var sb = new StringBuilder();

        var chart = events.OfType<SpcChart>().FirstOrDefault();
        if (chart != null)
            sb.Append(chart.ToSpcLine()).Append('\n');

        foreach (var e in events.Where(x => x is not SpcChart))
            sb.Append(e.ToSpcLine()).Append('\n');

        return sb.ToString();
    }
}