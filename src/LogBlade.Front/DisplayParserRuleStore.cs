using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

internal static class DisplayParserRuleStore
{
    public static string StorePath => Path.Combine(FindRepoRoot(), ".local", "parser-rules.json");

    public static List<DisplayParserRule> Load()
    {
        string path = StorePath;
        if (!File.Exists(path))
        {
            return new List<DisplayParserRule>();
        }

        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            List<DisplayParserRule> rules = JsonSerializer.Deserialize(json, LogBladeJsonSerializerContext.Default.ListDisplayParserRule) ?? new List<DisplayParserRule>();
            rules.RemoveAll(rule => rule.Stages is null || rule.Stages.Count == 0);
            return rules;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new List<DisplayParserRule>();
        }
    }

    public static void Save(IReadOnlyList<DisplayParserRule> rules)
    {
        string path = StorePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        string json = JsonSerializer.Serialize(new List<DisplayParserRule>(rules), LogBladeJsonSerializerContext.Default.ListDisplayParserRule);
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

            DirectoryInfo? parent = Directory.GetParent(current);
            current = parent?.FullName;
        }

        current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "build.ps1")))
            {
                return current;
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            current = parent?.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}
