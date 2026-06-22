using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

public enum DisplayParserMode
{
    Regex,
    Json,
    RegexReplace
}

public sealed class DisplayParserRule
{
    public string Name { get; set; } = string.Empty;
    public List<DisplayParserStage> Stages { get; set; } = new();
    public string Sample { get; set; } = string.Empty;

    public DisplayParserRule Clone() => new()
    {
        Name = Name,
        Stages = Stages is null ? new List<DisplayParserStage>() : Stages.ConvertAll(stage => stage.Clone()),
        Sample = Sample
    };
}

public sealed class DisplayParserStage
{
    public DisplayParserMode Mode { get; set; } = DisplayParserMode.Json;
    public string Rule { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;

    public DisplayParserStage Clone() => new()
    {
        Mode = Mode,
        Rule = Rule,
        Template = Template
    };
}

public static class DisplayParserEvaluator
{
    public static DisplayParserRule? CloneRule(DisplayParserRule? rule)
    {
        if (rule is null || rule.Stages is null || rule.Stages.Count == 0)
        {
            return null;
        }

        return rule.Clone();
    }

    public static void ValidateRule(DisplayParserRule rule)
    {
        if (rule.Stages is null || rule.Stages.Count == 0)
        {
            throw new ArgumentException("At least one parser stage is required.", nameof(rule));
        }

        for (int i = 0; i < rule.Stages.Count; i++)
        {
            try
            {
                ValidateStage(rule.Stages[i]);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"Stage {i + 1}: {ex.Message}", nameof(rule), ex);
            }
        }
    }

    public static void ValidateStage(DisplayParserStage stage)
    {
        if (stage.Mode is DisplayParserMode.Regex or DisplayParserMode.RegexReplace)
        {
            if (string.IsNullOrEmpty(stage.Rule))
            {
                throw new ArgumentException("Rule is required.", nameof(stage));
            }

            _ = new Regex(stage.Rule, RegexOptions.CultureInvariant);
            return;
        }

        if (string.IsNullOrWhiteSpace(stage.Rule))
        {
            throw new ArgumentException("Rule is required.", nameof(stage));
        }
    }

    public static string EvaluateOrOriginal(DisplayParserRule? rule, string input)
    {
        if (rule is null || rule.Stages is null || rule.Stages.Count == 0 || input.Length == 0)
        {
            return input;
        }

        return TryEvaluate(rule, input, out string parsed)
            ? parsed
            : input;
    }

    public static bool TryEvaluate(DisplayParserRule rule, string input, out string parsed)
    {
        parsed = input;
        string current = input;
        string lastValid = input;
        bool hasValidStage = false;

        for (int i = 0; i < rule.Stages.Count; i++)
        {
            if (!TryEvaluateStage(rule.Stages[i], current, out string next))
            {
                parsed = lastValid;
                return hasValidStage;
            }

            current = next;
            lastValid = current;
            hasValidStage = true;
        }

        parsed = lastValid;
        return hasValidStage;
    }

    private static bool TryEvaluateStage(DisplayParserStage stage, string input, out string parsed)
    {
        parsed = input;
        try
        {
            parsed = stage.Mode switch
            {
                DisplayParserMode.Regex => EvaluateRegex(stage.Rule, stage.Template, input),
                DisplayParserMode.Json => EvaluateJson(stage.Rule, input),
                DisplayParserMode.RegexReplace => EvaluateRegexReplace(stage.Rule, stage.Template, input),
                _ => input
            };
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException)
        {
            parsed = input;
            return false;
        }
    }

    private static string EvaluateRegex(string pattern, string? template, string input)
    {
        Regex regex = new(pattern, RegexOptions.CultureInvariant);
        Match match = regex.Match(input);
        if (!match.Success)
        {
            throw new InvalidOperationException("Regex did not match.");
        }

        string displayTemplate = string.IsNullOrWhiteSpace(template) ? "{0}" : template;
        return RenderTemplate(displayTemplate, selector => ResolveRegexPlaceholder(regex, match, selector));
    }

    private static string EvaluateRegexReplace(string pattern, string? replacement, string input)
    {
        Regex regex = new(pattern, RegexOptions.CultureInvariant);
        return regex.Replace(input, replacement ?? string.Empty);
    }

    private static string EvaluateJson(string template, string input)
    {
        string json = ExtractFirstJsonValue(input);
        using JsonDocument document = JsonDocument.Parse(json);
        return RenderTemplate(template, selector => ResolveJsonPlaceholder(document.RootElement, selector));
    }

    private static string RenderTemplate(string template, Func<string, string?> resolveValue)
    {
        StringBuilder output = new();
        for (int i = 0; i < template.Length; i++)
        {
            char current = template[i];
            if (current == '{')
            {
                if (i + 1 < template.Length && template[i + 1] == '{')
                {
                    output.Append('{');
                    i++;
                    continue;
                }

                int end = FindPlaceholderEnd(template, i + 1);
                if (end < 0)
                {
                    output.Append(current);
                    continue;
                }

                string expression = template.Substring(i + 1, end - i - 1).Trim();
                output.Append(EvaluateTemplatePlaceholder(expression, resolveValue));
                i = end;
                continue;
            }

            if (current == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                output.Append('}');
                i++;
                continue;
            }

            output.Append(current);
        }

        return output.ToString();
    }

    private static string EvaluateTemplatePlaceholder(string expression, Func<string, string?> resolveValue)
    {
        if (expression.Length == 0)
        {
            return string.Empty;
        }

        string transform = string.Empty;
        string selector = expression;
        int separator = expression.IndexOf(':');
        if (separator > 0)
        {
            string candidate = expression.Substring(0, separator).Trim();
            if (IsSupportedTransform(candidate))
            {
                transform = candidate.ToLowerInvariant();
                selector = expression.Substring(separator + 1).Trim();
            }
        }

        string? formatted = resolveValue(selector);
        if (formatted is null)
        {
            return string.Empty;
        }

        return transform switch
        {
            "upper" => formatted.ToUpperInvariant(),
            "lower" => formatted.ToLowerInvariant(),
            _ => formatted
        };
    }

    private static string? ResolveJsonPlaceholder(JsonElement root, string selector)
    {
        return TryGetJsonValue(root, selector, out JsonElement value)
            ? FormatJsonValue(value)
            : null;
    }

    private static string? ResolveRegexPlaceholder(Regex regex, Match match, string selector)
    {
        if (selector.Length == 0)
        {
            return null;
        }

        if (int.TryParse(selector, out int groupNumber))
        {
            return groupNumber >= 0 && groupNumber < match.Groups.Count
                ? GetGroupValue(match.Groups[groupNumber])
                : null;
        }

        string[] groupNames = regex.GetGroupNames();
        for (int i = 0; i < groupNames.Length; i++)
        {
            if (string.Equals(groupNames[i], selector, StringComparison.Ordinal))
            {
                return GetGroupValue(match.Groups[selector]);
            }
        }

        return null;
    }

    private static bool TryGetJsonValue(JsonElement root, string selector, out JsonElement value)
    {
        value = root;
        string[] parts = selector.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        foreach (string part in parts)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (!TryGetPropertyIgnoreCase(value, part, out value))
                {
                    return false;
                }

                continue;
            }

            if (value.ValueKind == JsonValueKind.Array && int.TryParse(part, out int index))
            {
                if (index < 0 || index >= value.GetArrayLength())
                {
                    return false;
                }

                value = value[index];
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string FormatJsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Undefined => string.Empty,
            _ => value.GetRawText()
        };
    }

    private static string ExtractFirstJsonValue(string input)
    {
        int start = FindJsonStart(input);
        if (start < 0)
        {
            throw new JsonException("No JSON object or array found in line.");
        }

        int end = FindJsonEnd(input, start);
        if (end < 0)
        {
            throw new JsonException("JSON is empty or incomplete.");
        }

        return input.Substring(start, end - start + 1);
    }

    private static int FindJsonStart(string input)
    {
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] is '{' or '[')
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindJsonEnd(string input, int start)
    {
        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = start; i < input.Length; i++)
        {
            char current = input[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current is '{' or '[')
            {
                depth++;
                continue;
            }

            if (current is '}' or ']')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }

                if (depth < 0)
                {
                    return -1;
                }
            }
        }

        return -1;
    }

    private static int FindPlaceholderEnd(string template, int start)
    {
        for (int i = start; i < template.Length; i++)
        {
            if (template[i] == '}')
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsSupportedTransform(string value)
    {
        return string.Equals(value, "upper", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "lower", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetGroupValue(Group group)
    {
        return group.Success ? group.Value : string.Empty;
    }
}
