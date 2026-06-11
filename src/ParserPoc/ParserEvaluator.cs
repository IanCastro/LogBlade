using System;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal enum ParserMode
{
    Regex,
    Json
}

internal static class ParserEvaluator
{
    public static string Evaluate(ParserMode mode, string rule, string input)
    {
        if (string.IsNullOrWhiteSpace(rule) || input.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return mode switch
            {
                ParserMode.Regex => EvaluateRegex(rule, template: null, input, requireMatch: false),
                ParserMode.Json => EvaluateJson(rule, input),
                _ => string.Empty
            };
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        {
            return "Erro: " + ex.Message;
        }
    }

    public static string EvaluateLines(ParserRule? rule, string input)
    {
        if (rule is null || input.Length == 0)
        {
            return string.Empty;
        }

        string normalized = input.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        string[] output = new string[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0)
            {
                output[i] = string.Empty;
                continue;
            }

            output[i] = TryEvaluateLine(rule, line, out string parsed)
                ? parsed
                : line;
        }

        return string.Join(Environment.NewLine, output);
    }

    private static bool TryEvaluateLine(ParserRule rule, string line, out string parsed)
    {
        try
        {
            parsed = rule.Mode switch
            {
                ParserMode.Regex => EvaluateRegex(rule.Rule, rule.Template, line, requireMatch: true),
                ParserMode.Json => EvaluateJson(rule.Rule, line),
                _ => line
            };
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException)
        {
            parsed = line;
            return false;
        }
    }

    private static string EvaluateRegex(string pattern, string? template, string input, bool requireMatch)
    {
        Regex regex = new(pattern, RegexOptions.CultureInvariant);
        Match match = regex.Match(input);
        if (!match.Success)
        {
            if (requireMatch)
            {
                throw new InvalidOperationException("Regex did not match.");
            }

            return string.Empty;
        }

        string displayTemplate = string.IsNullOrWhiteSpace(template) ? "{0}" : template;
        return RenderTemplate(displayTemplate, selector => ResolveRegexPlaceholder(regex, match, selector));
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
            throw new JsonException("Nenhum objeto ou array JSON encontrado na linha.");
        }

        int end = FindJsonEnd(input, start);
        if (end < 0)
        {
            throw new JsonException("JSON vazio ou incompleto.");
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
