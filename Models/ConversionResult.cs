using System.Collections.Generic;

namespace AffToSpcConverter.Models;

public record ConversionResult(
    // 转换后生成的 SPC 事件序列。
    List<ISpcEvent> Events,
    // 转换过程中收集到的警告信息。
    List<string> Warnings
);
