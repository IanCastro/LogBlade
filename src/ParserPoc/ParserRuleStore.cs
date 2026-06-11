using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class ParserRuleStore
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string StorePath => Path.Combine(FindRepoRoot(), ".local", "parser-poc-rules.json");

    public static List<ParserRule> Load()
    {
        string path = StorePath;
        if (!File.Exists(path))
        {
            return new List<ParserRule>();
        }

        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<ParserRule>>(json, s_jsonOptions) ?? new List<ParserRule>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new List<ParserRule>();
        }
    }

    public static void Save(IReadOnlyList<ParserRule> rules)
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
            if (File.Exists(Path.Combine(current, "build-parser-poc.ps1")))
            {
                return current;
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            current = parent?.FullName;
        }

        current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "build-parser-poc.ps1")))
            {
                return current;
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            current = parent?.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}
