using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class DisplayParserRuleStore
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

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
            return JsonSerializer.Deserialize<List<DisplayParserRule>>(json, s_jsonOptions) ?? new List<DisplayParserRule>();
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
        string json = JsonSerializer.Serialize(rules, s_jsonOptions);
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
