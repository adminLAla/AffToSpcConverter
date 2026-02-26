using System.Collections.Generic;

namespace AffToSpcConverter.Models;

// 转换结果模型，包含导出事件与警告信息。
public record ConversionResult(
    // 转换后生成的 SPC 事件序列。
    List<ISpcEvent> Events,
    // 转换过程中收集到的警告信息。
    List<string> Warnings
);
