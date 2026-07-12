using System.Collections.Generic;
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<DisplayParserRule>))]
[JsonSerializable(typeof(List<HighlightRule>))]
[JsonSerializable(typeof(DisplayParserRulesExportPackage))]
[JsonSerializable(typeof(HighlightRulesExportPackage))]
[JsonSerializable(typeof(WindowStateSettings))]
internal sealed partial class LogBladeJsonSerializerContext : JsonSerializerContext
{
}
