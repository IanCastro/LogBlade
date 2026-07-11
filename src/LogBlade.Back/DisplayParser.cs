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
    internal static int FindFirstJsonStageIndex(DisplayParserRule? rule)
    {
        if (rule?.Stages is null)
        {
            return -1;
        }

        for (int i = 0; i < rule.Stages.Count; i++)
        {
            if (rule.Stages[i].Mode == DisplayParserMode.Json)
            {
                return i;
            }
        }

        return -1;
    }

    internal static bool TryEvaluateStageRange(
        DisplayParserRule rule,
        int startIndex,
        int endIndexExclusive,
        string input,
        out string parsed)
    {
        parsed = input;
        int start = Math.Clamp(startIndex, 0, rule.Stages.Count);
        int end = Math.Clamp(endIndexExclusive, start, rule.Stages.Count);
        string current = input;
        for (int i = start; i < end; i++)
        {
            if (!TryEvaluateStage(rule.Stages[i], current, out string next))
            {
                parsed = input;
                return false;
            }

            current = next;
        }

        parsed = current;
        return true;
    }

    public static string GenerateJsonTemplateFromSample(string sample)
    {
        if (string.IsNullOrEmpty(sample))
        {
            return string.Empty;
        }

        List<string> paths = new();
        HashSet<string> distinctPaths = new(StringComparer.OrdinalIgnoreCase);
        string normalized = sample.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        StringBuilder? pendingJson = null;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0)
            {
                continue;
            }

            bool hadPendingJson = pendingJson is not null;
            string candidate = pendingJson is null
                ? line
                : pendingJson.Append(line).ToString();
            JsonAssemblyState state = DisplayParserRecordSequence.GetJsonAssemblyState(candidate);
            if (state == JsonAssemblyState.Incomplete)
            {
                pendingJson ??= new StringBuilder(line);
                continue;
            }

            if (hadPendingJson && state is JsonAssemblyState.None or JsonAssemblyState.Invalid)
            {
                pendingJson = null;
                candidate = line;
                state = DisplayParserRecordSequence.GetJsonAssemblyState(candidate);
                if (state == JsonAssemblyState.Incomplete)
                {
                    pendingJson = new StringBuilder(line);
                    continue;
                }
            }

            try
            {
                string json = ExtractFirstJsonValue(candidate);
                using JsonDocument document = JsonDocument.Parse(json);
                CollectJsonLeafPaths(document.RootElement, string.Empty, paths, distinctPaths);
            }
            catch (JsonException)
            {
            }

            pendingJson = null;
        }

        if (paths.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder template = new();
        for (int i = 0; i < paths.Count; i++)
        {
            if (i > 0)
            {
                template.Append(", ");
            }

            string path = paths[i];
            template.Append(path);
            template.Append(" - {");
            template.Append(path);
            template.Append('}');
        }

        return template.ToString();
    }

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

            try
            {
                _ = new Regex(stage.Rule, RegexOptions.CultureInvariant);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException("Regex error: " + ex.Message, nameof(stage), ex);
            }

            if (stage.Mode == DisplayParserMode.RegexReplace)
            {
                _ = DecodeRegexReplacementEscapes(stage.Template);
            }

            return;
        }

        if (string.IsNullOrEmpty(stage.Rule))
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

    public static string EvaluateLinesOrOriginal(DisplayParserRule? rule, string input)
    {
        if (rule is null || rule.Stages is null || rule.Stages.Count == 0 || input.Length == 0)
        {
            return input;
        }

        string normalized = input.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        List<RealLineData> source = new(lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            source.Add(new RealLineData(i, i, lines[i], i + 1, i + 1));
        }

        List<string> output = new();
        foreach (DisplayParserRecord record in DisplayParserRecordEvaluator.Enumerate(source, rule))
        {
            output.Add(record.Text);
        }

        return string.Join(Environment.NewLine, output);
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

        string displayTemplate = string.IsNullOrEmpty(template) ? "{0}" : template;
        return RenderTemplate(displayTemplate, selector => ResolveRegexPlaceholder(regex, match, selector));
    }

    private static string EvaluateRegexReplace(string pattern, string? replacement, string input)
    {
        Regex regex = new(pattern, RegexOptions.CultureInvariant);
        return regex.Replace(input, DecodeRegexReplacementEscapes(replacement ?? string.Empty));
    }

    private static string DecodeRegexReplacementEscapes(string replacement)
    {
        if (replacement.IndexOf('\\') < 0)
        {
            return replacement;
        }

        StringBuilder output = new(replacement.Length);
        for (int i = 0; i < replacement.Length; i++)
        {
            char current = replacement[i];
            if (current != '\\' || i + 1 >= replacement.Length)
            {
                output.Append(current);
                continue;
            }

            char escaped = replacement[++i];
            switch (escaped)
            {
                case 'r':
                    output.Append('\r');
                    break;
                case 'n':
                    output.Append('\n');
                    break;
                case 't':
                    output.Append('\t');
                    break;
                case '\\':
                    output.Append('\\');
                    break;
                case 'u':
                    if (i + 4 >= replacement.Length)
                    {
                        throw new ArgumentException("Replacement Unicode escape must use four hex digits.");
                    }

                    int value = 0;
                    for (int digit = 0; digit < 4; digit++)
                    {
                        int hex = GetHexValue(replacement[i + 1 + digit]);
                        if (hex < 0)
                        {
                            throw new ArgumentException("Replacement Unicode escape contains invalid hex digits.");
                        }

                        value = (value << 4) | hex;
                    }

                    output.Append((char)value);
                    i += 4;
                    break;
                default:
                    output.Append('\\');
                    output.Append(escaped);
                    break;
            }
        }

        return output.ToString();
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

    private static void CollectJsonLeafPaths(
        JsonElement element,
        string currentPath,
        List<string> paths,
        HashSet<string> distinctPaths)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!IsCompatibleJsonPathPart(property.Name))
                {
                    continue;
                }

                string propertyPath = currentPath.Length == 0
                    ? property.Name
                    : currentPath + "." + property.Name;
                CollectJsonLeafPaths(property.Value, propertyPath, paths, distinctPaths);
            }

            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                string itemPath = currentPath.Length == 0
                    ? index.ToString()
                    : currentPath + "." + index;
                CollectJsonLeafPaths(item, itemPath, paths, distinctPaths);
                index++;
            }

            return;
        }

        if (currentPath.Length > 0 && distinctPaths.Add(currentPath))
        {
            paths.Add(currentPath);
        }
    }

    private static bool IsCompatibleJsonPathPart(string value) =>
        value.Length > 0 &&
        value.IndexOf('.') < 0 &&
        value.IndexOf('{') < 0 &&
        value.IndexOf('}') < 0;

    private static string ExtractFirstJsonValue(string input)
    {
        int searchStart = 0;
        while (searchStart < input.Length)
        {
            int start = FindJsonStart(input, searchStart);
            if (start < 0)
            {
                break;
            }

            int end = FindJsonEnd(input, start);
            if (end >= 0)
            {
                string candidate = input.Substring(start, end - start + 1);
                try
                {
                    using JsonDocument document = JsonDocument.Parse(candidate);
                    return candidate;
                }
                catch (JsonException)
                {
                }
            }

            searchStart = start + 1;
        }

        throw new JsonException("No valid JSON object or array found in line.");
    }

    private static int FindJsonStart(string input, int start)
    {
        for (int i = Math.Max(0, start); i < input.Length; i++)
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

    private static int GetHexValue(char value)
    {
        if (value is >= '0' and <= '9')
        {
            return value - '0';
        }

        if (value is >= 'a' and <= 'f')
        {
            return value - 'a' + 10;
        }

        if (value is >= 'A' and <= 'F')
        {
            return value - 'A' + 10;
        }

        return -1;
    }

    private static string GetGroupValue(Group group)
    {
        return group.Success ? group.Value : string.Empty;
    }
}
