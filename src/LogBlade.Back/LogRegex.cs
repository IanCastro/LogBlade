using System;
using System.Text.RegularExpressions;

internal static class LogRegex
{
    public static Regex Create(string pattern, bool ignoreCase = false)
    {
        RegexOptions options = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
        if (ignoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        try
        {
            return new Regex(pattern, options);
        }
        catch (NotSupportedException ex)
        {
            throw new ArgumentException(
                "Regex is not supported by the non-backtracking engine: " + ex.Message,
                nameof(pattern),
                ex);
        }
    }
}
