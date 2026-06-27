using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

internal enum HighlightMatchMode
{
    Text,
    Regex
}

internal sealed class HighlightRule
{
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public HighlightMatchMode Mode { get; set; } = HighlightMatchMode.Text;

    public string Pattern { get; set; } = string.Empty;

    public bool IgnoreCase { get; set; }

    public string Color { get; set; } = "#FFF2A8";

    public HighlightRule Clone()
    {
        return new HighlightRule
        {
            Name = Name,
            Enabled = Enabled,
            Mode = Mode,
            Pattern = Pattern,
            IgnoreCase = IgnoreCase,
            Color = Color
        };
    }
}

internal readonly record struct HighlightStyle(int BackgroundColor, int ForegroundColor);

internal sealed class CompiledHighlightRule
{
    private readonly string _pattern;
    private readonly StringComparison _comparison;
    private readonly Regex? _regex;

    public CompiledHighlightRule(HighlightRule rule, Regex? regex, HighlightStyle style)
    {
        _pattern = rule.Pattern;
        _comparison = rule.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        _regex = regex;
        Style = style;
    }

    public HighlightStyle Style { get; }

    public bool IsMatch(string text)
    {
        return _regex?.IsMatch(text) ?? text.IndexOf(_pattern, _comparison) >= 0;
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

        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            error = "Name is required.";
            return false;
        }

        if (string.IsNullOrEmpty(rule.Pattern))
        {
            error = "Pattern is required.";
            return false;
        }

        if (!TryParseColor(rule.Color, out int red, out int green, out int blue))
        {
            error = "Color must use the #RRGGBB format.";
            return false;
        }

        Regex? regex = null;
        if (rule.Mode == HighlightMatchMode.Regex)
        {
            RegexOptions options = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
            if (rule.IgnoreCase)
            {
                options |= RegexOptions.IgnoreCase;
            }

            try
            {
                regex = new Regex(rule.Pattern, options);
            }
            catch (ArgumentException ex)
            {
                error = "Invalid Regex: " + ex.Message;
                return false;
            }
            catch (NotSupportedException ex)
            {
                error = "Regex is not compatible with NonBacktracking: " + ex.Message;
                return false;
            }
        }

        int background = ToColorRef(red, green, blue);
        int foreground = GetLuminance(red, green, blue) > 0.179d
            ? ToColorRef(0, 0, 0)
            : ToColorRef(255, 255, 255);
        compiledRule = new CompiledHighlightRule(rule, regex, new HighlightStyle(background, foreground));
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

    private static double GetLuminance(int red, int green, int blue)
    {
        static double Linearize(int component)
        {
            double value = component / 255d;
            return value <= 0.03928d
                ? value / 12.92d
                : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
        }

        return (0.2126d * Linearize(red)) + (0.7152d * Linearize(green)) + (0.0722d * Linearize(blue));
    }
}
