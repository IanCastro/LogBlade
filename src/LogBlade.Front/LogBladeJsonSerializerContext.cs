using System.Collections.Generic;
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<DisplayParserRule>))]
[JsonSerializable(typeof(List<HighlightRule>))]
internal sealed partial class LogBladeJsonSerializerContext : JsonSerializerContext
{
}
