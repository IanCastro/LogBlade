using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

internal static class HighlightRuleStore
{
    public static string StorePath => Path.Combine(FindRepoRoot(), ".local", "highlighting-rules.json");

    public static List<HighlightRule> Load()
    {
        string path = StorePath;
        if (!File.Exists(path))
        {
            return new List<HighlightRule>();
        }

        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize(json, LogBladeJsonSerializerContext.Default.ListHighlightRule) ?? new List<HighlightRule>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new List<HighlightRule>();
        }
    }

    public static void Save(IReadOnlyList<HighlightRule> rules)
    {
        string path = StorePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        string json = JsonSerializer.Serialize(new List<HighlightRule>(rules), LogBladeJsonSerializerContext.Default.ListHighlightRule);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string FindRepoRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "build.ps1")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "build.ps1")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}
