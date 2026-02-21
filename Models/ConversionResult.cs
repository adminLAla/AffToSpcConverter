using System.Collections.Generic;

namespace AffToSpcConverter.Models;

public record ConversionResult(List<ISpcEvent> Events, List<string> Warnings);
