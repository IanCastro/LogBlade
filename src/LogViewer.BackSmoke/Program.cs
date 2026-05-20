using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

internal static class Program
{
    private static int Main()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "LogViewerBackSmoke", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempRoot);

            RunRegexCaptureGroups(tempRoot);
            RunRegexWithoutGroupsUsesPlainRows(tempRoot);
            RunLiteralSearchUsesPlainRows(tempRoot);
            RunWrappedLineCaptureGroups(tempRoot);
            RunInvalidRegexValidation();

            Console.WriteLine("Back smoke tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Back smoke tests failed.");
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static void RunRegexCaptureGroups(string tempRoot)
    {
        string path = WriteLog(tempRoot, "capture-groups.log", "aaabccc xx aabcc\r\nplain\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(a+)b(c+)", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        IColumnViewportReader columns = reader;

        AssertSequence("capture headers", columns.ColumnHeaders, "Text", "0", "1");
        AssertEqual("capture row count", columns.CurrentCells.Count, 1);
        AssertEqual("capture text", columns.CurrentCells[0][0], "aaabccc xx aabcc");
        AssertEqual("first match group 0", columns.CurrentCells[0][1], "aaa");
        AssertEqual("first match group 1", columns.CurrentCells[0][2], "ccc");
    }

    private static void RunRegexWithoutGroupsUsesPlainRows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "regex-no-groups.log", "aaabccc\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("a+b", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        AssertEqual("regex no-group headers", ((IColumnViewportReader)reader).ColumnHeaders.Count, 0);
        AssertSequence("regex no-group rows", reader.CurrentRows, "aaabccc");
    }

    private static void RunLiteralSearchUsesPlainRows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "literal.log", "line.with.dot\r\nplain\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions(".", UseRegex: false, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        AssertEqual("literal headers", ((IColumnViewportReader)reader).ColumnHeaders.Count, 0);
        AssertSequence("literal rows", reader.CurrentRows, "line.with.dot");
    }

    private static void RunWrappedLineCaptureGroups(string tempRoot)
    {
        string longText = "aaabccc" + new string('x', VisualRowReader.VisibleSegmentChars + 16);
        string path = WriteLog(tempRoot, "wrapped-captures.log", longText + "\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(a+)b(c+)", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 3);
        IColumnViewportReader columns = reader;

        AssertSequence("wrapped headers", columns.ColumnHeaders, "Text", "0", "1");
        AssertEqual("wrapped row count", columns.CurrentCells.Count, 2);
        AssertEqual("wrapped first group 0", columns.CurrentCells[0][1], "aaa");
        AssertEqual("wrapped first group 1", columns.CurrentCells[0][2], "ccc");
        AssertEqual("wrapped continuation group 0", columns.CurrentCells[1][1], string.Empty);
        AssertEqual("wrapped continuation group 1", columns.CurrentCells[1][2], string.Empty);
    }

    private static void RunInvalidRegexValidation()
    {
        try
        {
            LogSearchBuilder.ValidateOptions(new SearchOptions("[", UseRegex: true, IgnoreCase: false));
        }
        catch (ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException("Invalid regex did not fail validation.");
    }

    private static string WriteLog(string tempRoot, string name, string content)
    {
        string path = Path.Combine(tempRoot, name);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static void AssertSequence(string name, IReadOnlyList<string> actual, params string[] expected)
    {
        AssertEqual(name + " count", actual.Count, expected.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            AssertEqual(name + " [" + i + "]", actual[i], expected[i]);
        }
    }

    private static void AssertEqual<T>(string name, T actual, T expected)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            throw new InvalidOperationException($"{name}: expected '{expected}', got '{actual}'.");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
