using System;

internal static class DisplayParserRuleProcessingComparer
{
    public static bool AreEquivalent(DisplayParserRule? left, DisplayParserRule? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left?.Stages is null || right?.Stages is null || left.Stages.Count != right.Stages.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Stages.Count; i++)
        {
            if (!AreEquivalent(left.Stages[i], right.Stages[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreEquivalent(DisplayParserStage? left, DisplayParserStage? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null ||
            left.Mode != right.Mode ||
            !AreEquivalentText(left.Rule, right.Rule))
        {
            return false;
        }

        return left.Mode switch
        {
            DisplayParserMode.Regex or DisplayParserMode.RegexReplace =>
                AreEquivalentText(left.Template, right.Template),
            DisplayParserMode.Json => true,
            DisplayParserMode.Filter =>
                left.UseRegex == right.UseRegex &&
                left.IgnoreCase == right.IgnoreCase &&
                left.InvertMatch == right.InvertMatch,
            _ => false
        };
    }

    private static bool AreEquivalentText(string? left, string? right) =>
        string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);
}
