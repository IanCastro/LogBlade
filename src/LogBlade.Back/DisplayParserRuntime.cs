using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

internal sealed class DisplayParserRuntime
{
    private readonly CompiledDisplayParserStage[] _stages;
    private readonly int[] _filterStageIndices;

    public DisplayParserRuntime(DisplayParserRule? rule)
    {
        if (rule?.Stages is null || rule.Stages.Count == 0)
        {
            _stages = Array.Empty<CompiledDisplayParserStage>();
            _filterStageIndices = Array.Empty<int>();
            return;
        }

        _stages = new CompiledDisplayParserStage[rule.Stages.Count];
        List<int> filterStageIndices = new();
        for (int i = 0; i < _stages.Length; i++)
        {
            DisplayParserStage stage = rule.Stages[i];
            _stages[i] = new CompiledDisplayParserStage(stage);
            if (stage.Mode == DisplayParserMode.Filter)
            {
                filterStageIndices.Add(i);
            }
        }

        _filterStageIndices = filterStageIndices.ToArray();
    }

    public int StageCount => _stages.Length;
    public int FilterCount => _filterStageIndices.Length;

    public int GetFilterStageIndex(int filterIndex) => _filterStageIndices[filterIndex];

    public int FindFirstJsonStageIndex(int endIndexExclusive)
    {
        int end = Math.Clamp(endIndexExclusive, 0, _stages.Length);
        for (int i = 0; i < end; i++)
        {
            if (_stages[i].Mode == DisplayParserMode.Json)
            {
                return i;
            }
        }

        return -1;
    }

    public bool TryEvaluateStageRange(
        int startIndex,
        int endIndexExclusive,
        string input,
        out string parsed)
    {
        parsed = input;
        int start = Math.Clamp(startIndex, 0, _stages.Length);
        int end = Math.Clamp(endIndexExclusive, start, _stages.Length);
        string current = input;
        for (int i = start; i < end; i++)
        {
            if (!_stages[i].TryEvaluate(current, out string next))
            {
                parsed = input;
                return false;
            }

            current = next;
        }

        parsed = current;
        return true;
    }

    public string EvaluateStageRangeOrOriginal(
        int startIndex,
        int endIndexExclusive,
        string input)
    {
        int start = Math.Clamp(startIndex, 0, _stages.Length);
        int end = Math.Clamp(endIndexExclusive, start, _stages.Length);
        string current = input;
        string lastValid = input;
        for (int i = start; i < end; i++)
        {
            CompiledDisplayParserStage stage = _stages[i];
            if (stage.Mode == DisplayParserMode.Filter)
            {
                continue;
            }

            if (!stage.TryEvaluate(current, out string next))
            {
                return lastValid;
            }

            current = next;
            lastValid = current;
        }

        return lastValid;
    }

    public bool TryEvaluate(string input, out string parsed)
    {
        parsed = input;
        string current = input;
        string lastValid = input;
        bool hasValidStage = false;
        for (int i = 0; i < _stages.Length; i++)
        {
            if (!_stages[i].TryEvaluate(current, out string next))
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

    public bool TryIncludeFilter(
        int stageIndex,
        string text,
        out FilteredCaptureGroups? captureGroups) =>
        _stages[stageIndex].TryInclude(text, out captureGroups);

    private sealed class CompiledDisplayParserStage
    {
        private readonly string _rule;
        private readonly Regex? _regex;
        private readonly string _decodedOutput = string.Empty;
        private readonly bool _isValid = true;
        private readonly HashSet<string>? _regexGroupNames;
        private readonly SearchOptions _filterOptions;
        private readonly StringComparison _filterComparison;
        private readonly int[] _captureGroupNumbers = Array.Empty<int>();
        private readonly string[] _captureGroupHeaders = Array.Empty<string>();

        public CompiledDisplayParserStage(DisplayParserStage stage)
        {
            Mode = stage.Mode;
            _rule = stage.Rule ?? string.Empty;
            try
            {
                if (Mode is DisplayParserMode.Regex or DisplayParserMode.RegexReplace)
                {
                    _regex = LogRegex.Create(_rule);
                    _regexGroupNames = new HashSet<string>(_regex.GetGroupNames(), StringComparer.Ordinal);
                    _decodedOutput = DisplayParserEvaluator.DecodeOutputEscapes(stage.Template ?? string.Empty);
                }
                else if (Mode == DisplayParserMode.Json)
                {
                    _decodedOutput = DisplayParserEvaluator.DecodeOutputEscapes(_rule);
                }
                else if (Mode == DisplayParserMode.Filter)
                {
                    _filterOptions = new SearchOptions(
                        _rule,
                        stage.UseRegex,
                        stage.IgnoreCase,
                        stage.InvertMatch);
                    _filterComparison = stage.IgnoreCase
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal;
                    if (stage.UseRegex)
                    {
                        _regex = LogRegex.Create(_rule, stage.IgnoreCase);
                        int[] groupNumbers = _regex.GetGroupNumbers();
                        string[] groupNames = _regex.GetGroupNames();
                        List<int> captureNumbers = new();
                        List<string> captureHeaders = new();
                        int unnamedCaptureIndex = 0;
                        for (int i = 0; i < groupNumbers.Length; i++)
                        {
                            int groupNumber = groupNumbers[i];
                            if (groupNumber == 0)
                            {
                                continue;
                            }

                            captureNumbers.Add(groupNumber);
                            string groupName = i < groupNames.Length
                                ? groupNames[i]
                                : groupNumber.ToString();
                            if (int.TryParse(groupName, out _))
                            {
                                captureHeaders.Add(unnamedCaptureIndex.ToString());
                                unnamedCaptureIndex++;
                            }
                            else
                            {
                                captureHeaders.Add(groupName);
                            }
                        }

                        _captureGroupNumbers = captureNumbers.ToArray();
                        _captureGroupHeaders = captureHeaders.ToArray();
                    }
                }
            }
            catch (ArgumentException)
            {
                _isValid = false;
            }
        }

        public DisplayParserMode Mode { get; }

        public bool TryEvaluate(string input, out string parsed)
        {
            parsed = input;
            if (!_isValid)
            {
                return false;
            }

            try
            {
                parsed = Mode switch
                {
                    DisplayParserMode.Regex => EvaluateRegex(input),
                    DisplayParserMode.Json => DisplayParserEvaluator.EvaluateJson(_decodedOutput, input),
                    DisplayParserMode.RegexReplace => _regex!.Replace(input, _decodedOutput),
                    DisplayParserMode.Filter => input,
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

        public bool TryInclude(string text, out FilteredCaptureGroups? captureGroups)
        {
            captureGroups = null;
            if (Mode != DisplayParserMode.Filter || !_isValid)
            {
                return false;
            }

            bool matched;
            if (_regex is null)
            {
                matched = text.IndexOf(_filterOptions.Query, _filterComparison) >= 0;
            }
            else
            {
                Match match = _regex.Match(text);
                matched = match.Success;
                if (matched && !_filterOptions.InvertMatch && _captureGroupNumbers.Length > 0)
                {
                    string[] values = new string[_captureGroupNumbers.Length];
                    for (int i = 0; i < values.Length; i++)
                    {
                        Group group = match.Groups[_captureGroupNumbers[i]];
                        values[i] = group.Success ? group.Value : string.Empty;
                    }

                    captureGroups = new FilteredCaptureGroups(_captureGroupHeaders, values);
                }
            }

            return matched != _filterOptions.InvertMatch;
        }

        private string EvaluateRegex(string input)
        {
            Match match = _regex!.Match(input);
            if (!match.Success)
            {
                throw new InvalidOperationException("Regex did not match.");
            }

            string displayTemplate = string.IsNullOrEmpty(_decodedOutput) ? "{0}" : _decodedOutput;
            return DisplayParserEvaluator.RenderTemplate(
                displayTemplate,
                selector => ResolveRegexPlaceholder(match, selector));
        }

        private string? ResolveRegexPlaceholder(Match match, string selector)
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

            return _regexGroupNames is not null && _regexGroupNames.Contains(selector)
                ? GetGroupValue(match.Groups[selector])
                : null;
        }

        private static string GetGroupValue(Group group) =>
            group.Success ? group.Value : string.Empty;
    }
}
