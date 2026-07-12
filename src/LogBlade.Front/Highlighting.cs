using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;

internal sealed class HighlightRule
{
    public bool Enabled { get; set; } = true;

    public string Pattern { get; set; } = string.Empty;

    public bool IgnoreCase { get; set; }

    public bool InvertMatch { get; set; }

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public string BackgroundColor { get; set; } = "#FFF2A8";

    public string ForegroundColor { get; set; } = "#000000";

    public HighlightRule Clone()
    {
        return new HighlightRule
        {
            Enabled = Enabled,
            Pattern = Pattern,
            IgnoreCase = IgnoreCase,
            InvertMatch = InvertMatch,
            Bold = Bold,
            Italic = Italic,
            BackgroundColor = BackgroundColor,
            ForegroundColor = ForegroundColor
        };
    }
}

internal sealed class HighlightRulesExportPackage
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("app")]
    public string App { get; set; } = "LogBlade";

    [JsonPropertyName("highlightRules")]
    public List<HighlightRule> HighlightRules { get; set; } = new();
}

internal readonly record struct HighlightStyle(
    int BackgroundColor,
    int ForegroundColor,
    bool Bold,
    bool Italic);

internal sealed class CompiledHighlightRule
{
    private readonly string _pattern;
    private readonly StringComparison _comparison;
    private readonly bool _invertMatch;

    public CompiledHighlightRule(HighlightRule rule, HighlightStyle style)
    {
        _pattern = rule.Pattern;
        _comparison = rule.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        _invertMatch = rule.InvertMatch;
        Style = style;
    }

    public HighlightStyle Style { get; }

    public bool IsMatch(string text)
    {
        bool matched = text.IndexOf(_pattern, _comparison) >= 0;
        return _invertMatch ? !matched : matched;
    }
}

internal static class HighlightRuleCompiler
{
    public static IReadOnlyList<CompiledHighlightRule> Compile(IReadOnlyList<HighlightRule> rules)
    {
        List<CompiledHighlightRule> compiled = new(rules.Count);
        for (int i = 0; i < rules.Count; i++)
        {
            HighlightRule rule = rules[i];
            if (!rule.Enabled)
            {
                continue;
            }

            if (TryCompile(rule, out CompiledHighlightRule? compiledRule, out _))
            {
                compiled.Add(compiledRule);
            }
        }

        return compiled;
    }

    public static bool TryCompile(HighlightRule rule, out CompiledHighlightRule compiledRule, out string error)
    {
        compiledRule = null!;
        error = string.Empty;

        if (string.IsNullOrEmpty(rule.Pattern))
        {
            error = "Pattern is required.";
            return false;
        }

        if (!TryParseColor(rule.BackgroundColor, out int backgroundRed, out int backgroundGreen, out int backgroundBlue))
        {
            error = "Background color must use the #RRGGBB format.";
            return false;
        }

        if (!TryParseColor(rule.ForegroundColor, out int foregroundRed, out int foregroundGreen, out int foregroundBlue))
        {
            error = "Text color must use the #RRGGBB format.";
            return false;
        }

        int background = ToColorRef(backgroundRed, backgroundGreen, backgroundBlue);
        int foreground = ToColorRef(foregroundRed, foregroundGreen, foregroundBlue);
        compiledRule = new CompiledHighlightRule(
            rule,
            new HighlightStyle(background, foreground, rule.Bold, rule.Italic));
        return true;
    }

    public static bool TryMatch(IReadOnlyList<CompiledHighlightRule> rules, string text, out HighlightStyle style)
    {
        for (int i = 0; i < rules.Count; i++)
        {
            if (rules[i].IsMatch(text))
            {
                style = rules[i].Style;
                return true;
            }
        }

        style = default;
        return false;
    }

    public static bool TryParseColor(string? value, out int red, out int green, out int blue)
    {
        red = 0;
        green = 0;
        blue = 0;
        if (value is null || value.Length != 7 || value[0] != '#')
        {
            return false;
        }

        return int.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out red) &&
            int.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out green) &&
            int.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out blue);
    }

    public static string ToColorString(int colorRef)
    {
        int red = colorRef & 0xFF;
        int green = (colorRef >> 8) & 0xFF;
        int blue = (colorRef >> 16) & 0xFF;
        return $"#{red:X2}{green:X2}{blue:X2}";
    }

    public static int ToColorRef(int red, int green, int blue)
    {
        return red | (green << 8) | (blue << 16);
    }

}
