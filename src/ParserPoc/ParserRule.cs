using System.Text.Json.Serialization;

internal sealed class ParserRule
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("mode")]
    public ParserMode Mode { get; set; } = ParserMode.Json;

    [JsonPropertyName("rule")]
    public string Rule { get; set; } = string.Empty;

    [JsonPropertyName("template")]
    public string Template { get; set; } = string.Empty;

    [JsonPropertyName("sample")]
    public string Sample { get; set; } = string.Empty;
}
