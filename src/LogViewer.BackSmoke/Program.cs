using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

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
            RunLiteralInvertMatch(tempRoot);
            RunRegexInvertMatch(tempRoot);
            RunRegexCaptureGroupsInvertUsesPlainRows(tempRoot);
            RunWrappedLineCaptureGroups(tempRoot);
            RunFilteredLineStaleWhenStartMoves(tempRoot);
            RunFilteredLineStaleWhenEndMoves(tempRoot);
            RunFilteredLineValidationAcceptsLf(tempRoot);
            RunFilteredLineValidationAcceptsUtf16Le(tempRoot);
            RunFilteredLineValidationAcceptsUtf16Be(tempRoot);
            RunFilteredLineValidationAcceptsFinalLineWithoutBreak(tempRoot);
            RunFilteredLineValidationAcceptsEmptyLine(tempRoot);
            RunSearchRowOffsetSyncsMainReader(tempRoot);
            RunSearchRowOffsetDetectsStaleLine(tempRoot);
            RunVisualSelectionRangeAcrossViewport(tempRoot);
            RunVisualSelectionSelectsAllRows(tempRoot);
            RunVisualSelectionWrappedRowCopiesOriginalLine(tempRoot);
            RunVisualSelectionWrappedRangeDeduplicatesLine(tempRoot);
            RunVisualSelectionSelectAllUsesRealLines(tempRoot);
            RunVisualSelectionWrappedExclusionExcludesWholeLine(tempRoot);
            RunFilteredSelectionRangeAcrossResults(tempRoot);
            RunFilteredSelectionSelectsAllRows(tempRoot);
            RunFilteredSelectionCopiesCaptureCells(tempRoot);
            RunFilteredSelectionCopiesLiteralTextCell(tempRoot);
            RunInvalidRegexValidation();
            RunAppendSearchAddsMatches(tempRoot);
            RunAppendSearchWithoutMatchKeepsCount(tempRoot);
            RunAppendSearchRescansPartialLastLine(tempRoot);
            RunAppendSearchPreservesRegexCaptureGroups(tempRoot);
            RunAppendSearchInvertMatch(tempRoot);
            RunAppendSearchStalesWhenEarlierLineGrows(tempRoot);
            RunPageUpNearStartClampsToTop(tempRoot);
            RunPageUpInsideWrappedFirstLineClampsToTop(tempRoot);
            RunRefreshTailAtEndShowsAppendedRows(tempRoot);
            RunRefreshTailSmallInitialFileShowsAppendedRows(tempRoot);
            RunRefreshFileSizeAwayFromEndLetsJumpEndSeeAppendedRows(tempRoot);
            RunRefreshTailAwayFromEndDoesNotMove(tempRoot);
            RunRefreshTailAfterTruncateReloadsFromStart(tempRoot);
            RunRefreshTailAfterTruncateToEmptyClearsRows(tempRoot);
            RunRefreshFileSizeAfterTruncateLetsJumpEndSeeAppendedRows(tempRoot);

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

    private static void RunLiteralInvertMatch(string tempRoot)
    {
        string path = WriteLog(tempRoot, "literal-invert.log", "alpha\r\nplain\r\nbeta\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false, InvertMatch: true));

        reader.ReadFromPercentage(0d, 10);
        AssertEqual("literal invert headers", ((IColumnViewportReader)reader).ColumnHeaders.Count, 0);
        AssertSequence("literal invert rows", reader.CurrentRows, "plain", "beta");
    }

    private static void RunRegexInvertMatch(string tempRoot)
    {
        string path = WriteLog(tempRoot, "regex-invert.log", "alpha\r\nplain\r\nbeta\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("^a", UseRegex: true, IgnoreCase: false, InvertMatch: true));

        reader.ReadFromPercentage(0d, 10);
        AssertEqual("regex invert headers", ((IColumnViewportReader)reader).ColumnHeaders.Count, 0);
        AssertSequence("regex invert rows", reader.CurrentRows, "plain", "beta");
    }

    private static void RunRegexCaptureGroupsInvertUsesPlainRows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "regex-capture-invert.log", "aaabccc\r\nplain\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(a+)b(c+)", UseRegex: true, IgnoreCase: false, InvertMatch: true));

        reader.ReadFromPercentage(0d, 10);
        AssertEqual("regex capture invert headers", ((IColumnViewportReader)reader).ColumnHeaders.Count, 0);
        AssertSequence("regex capture invert rows", reader.CurrentRows, "plain");
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
        AssertEqual("wrapped row count", columns.CurrentCells.Count, 1);
        AssertEqual("wrapped text", columns.CurrentCells[0][0], longText);
        AssertEqual("wrapped first group 0", columns.CurrentCells[0][1], "aaa");
        AssertEqual("wrapped first group 1", columns.CurrentCells[0][2], "ccc");
    }

    private static void RunFilteredLineStaleWhenStartMoves(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-stale-start.log", "one\r\ntwo alpha\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));

        File.WriteAllText(path, "one extended\r\ntwo alpha\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        AssertThrows<FilteredLineStaleException>("filtered stale start", () => reader.ReadFromPercentage(0d, 10));
    }

    private static void RunFilteredLineStaleWhenEndMoves(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-stale-end.log", "alpha\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));

        File.WriteAllText(path, "alphaz\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        AssertThrows<FilteredLineStaleException>("filtered stale end", () => reader.ReadFromPercentage(0d, 10));
    }

    private static void RunFilteredLineValidationAcceptsLf(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-lf.log", "alpha\nbeta\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("beta", UseRegex: false, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        AssertSequence("filtered lf rows", reader.CurrentRows, "beta");
    }

    private static void RunFilteredLineValidationAcceptsUtf16Le(string tempRoot)
    {
        string path = Path.Combine(tempRoot, "filtered-utf16-le.log");
        File.WriteAllText(path, "alpha\r\nbeta\r\n", Encoding.Unicode);

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.Unicode,
            dataOffset: 2,
            new SearchOptions("beta", UseRegex: false, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        AssertSequence("filtered utf16 le rows", reader.CurrentRows, "beta");
    }

    private static void RunFilteredLineValidationAcceptsUtf16Be(string tempRoot)
    {
        string path = Path.Combine(tempRoot, "filtered-utf16-be.log");
        File.WriteAllText(path, "alpha\r\nbeta\r\n", Encoding.BigEndianUnicode);

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.BigEndianUnicode,
            dataOffset: 2,
            new SearchOptions("beta", UseRegex: false, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        AssertSequence("filtered utf16 be rows", reader.CurrentRows, "beta");
    }

    private static void RunFilteredLineValidationAcceptsFinalLineWithoutBreak(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-no-final-break.log", "plain\r\nalpha");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        AssertSequence("filtered no final break rows", reader.CurrentRows, "alpha");
    }

    private static void RunFilteredLineValidationAcceptsEmptyLine(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-empty-line.log", "alpha\r\n\r\nbeta\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("^$", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        AssertSequence("filtered empty line rows", reader.CurrentRows, string.Empty);
    }

    private static void RunSearchRowOffsetSyncsMainReader(string tempRoot)
    {
        string path = WriteLog(tempRoot, "search-row-offset-sync.log", "zero\r\none alpha\r\ntwo beta alpha\r\nthree\r\n");

        using FilteredVisualRowReader filtered = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));

        AssertEqual("search row offset available", ((IFileOffsetViewportReader)filtered).TryGetRowStartOffset(1, out long startOffset), true);

        using VisualRowReader main = new(path, Encoding.UTF8, dataOffset: 0);
        main.ReadFromOffset(startOffset, 2);

        AssertSequence("search row offset main rows", main.CurrentRows, "two beta alpha", "three");
    }

    private static void RunSearchRowOffsetDetectsStaleLine(string tempRoot)
    {
        string path = WriteLog(tempRoot, "search-row-offset-stale.log", "one\r\ntwo alpha\r\n");

        using FilteredVisualRowReader filtered = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));

        File.WriteAllText(path, "one extended\r\ntwo alpha\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        AssertThrows<FilteredLineStaleException>(
            "search row offset stale",
            () => ((IFileOffsetViewportReader)filtered).TryGetRowStartOffset(0, out _));
    }

    private static void RunVisualSelectionRangeAcrossViewport(string tempRoot)
    {
        string path = WriteLog(tempRoot, "visual-selection-range.log", "line-0\r\nline-1\r\nline-2\r\nline-3\r\n");

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(0d, 2);
        ISelectableViewportReader selectable = reader;
        ViewportRowSelectionKey first = selectable.CurrentRowSelectionKeys[0];
        reader.ReadFromPercentage(100d, 2);
        ViewportRowSelectionKey last = selectable.CurrentRowSelectionKeys[1];

        IReadOnlyList<ViewportSelectedRow> rows = selectable.ReadSelectedRows(
            selectAll: false,
            new[] { new ViewportRowSelectionRange(first, last) },
            Array.Empty<ViewportRowSelectionKey>());

        AssertSelectedRows("visual selection range", rows, "line-0", "line-1", "line-2", "line-3");
    }

    private static void RunVisualSelectionSelectsAllRows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "visual-selection-all.log", "line-0\r\nline-1\r\nline-2\r\n");

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        IReadOnlyList<ViewportSelectedRow> rows = ((ISelectableViewportReader)reader).ReadSelectedRows(
            selectAll: true,
            Array.Empty<ViewportRowSelectionRange>(),
            Array.Empty<ViewportRowSelectionKey>());

        AssertSelectedRows("visual selection all", rows, "line-0", "line-1", "line-2");
    }

    private static void RunVisualSelectionWrappedRowCopiesOriginalLine(string tempRoot)
    {
        string wrappedLine = new string('a', VisualRowReader.VisibleSegmentChars) + "tail";
        string path = WriteLog(tempRoot, "visual-selection-wrapped-line.log", wrappedLine + "\r\nnext\r\n");

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(0d, 3);
        ISelectableViewportReader selectable = reader;
        ViewportRowSelectionKey secondSegment = selectable.CurrentRowSelectionKeys[1];

        IReadOnlyList<ViewportSelectedRow> rows = selectable.ReadSelectedRows(
            selectAll: false,
            new[] { new ViewportRowSelectionRange(secondSegment, secondSegment) },
            Array.Empty<ViewportRowSelectionKey>());

        AssertSelectedRows("visual selection wrapped line", rows, wrappedLine);
    }

    private static void RunVisualSelectionWrappedRangeDeduplicatesLine(string tempRoot)
    {
        string wrappedLine = new string('b', VisualRowReader.VisibleSegmentChars) + "tail";
        string path = WriteLog(tempRoot, "visual-selection-wrapped-range.log", wrappedLine + "\r\nnext\r\n");

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(0d, 3);
        ISelectableViewportReader selectable = reader;
        ViewportRowSelectionKey secondSegment = selectable.CurrentRowSelectionKeys[1];
        ViewportRowSelectionKey nextLine = selectable.CurrentRowSelectionKeys[2];

        IReadOnlyList<ViewportSelectedRow> rows = selectable.ReadSelectedRows(
            selectAll: false,
            new[] { new ViewportRowSelectionRange(secondSegment, nextLine) },
            Array.Empty<ViewportRowSelectionKey>());

        AssertSelectedRows("visual selection wrapped range", rows, wrappedLine, "next");
    }

    private static void RunVisualSelectionSelectAllUsesRealLines(string tempRoot)
    {
        string wrappedLine = new string('c', VisualRowReader.VisibleSegmentChars) + "tail";
        string path = WriteLog(tempRoot, "visual-selection-all-wrapped.log", wrappedLine + "\r\nnext\r\n");

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        IReadOnlyList<ViewportSelectedRow> rows = ((ISelectableViewportReader)reader).ReadSelectedRows(
            selectAll: true,
            Array.Empty<ViewportRowSelectionRange>(),
            Array.Empty<ViewportRowSelectionKey>());

        AssertSelectedRows("visual selection all wrapped", rows, wrappedLine, "next");
    }

    private static void RunVisualSelectionWrappedExclusionExcludesWholeLine(string tempRoot)
    {
        string wrappedLine = new string('d', VisualRowReader.VisibleSegmentChars) + "tail";
        string path = WriteLog(tempRoot, "visual-selection-wrapped-excluded.log", wrappedLine + "\r\nnext\r\n");

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(0d, 3);
        ISelectableViewportReader selectable = reader;
        ViewportRowSelectionKey secondSegment = selectable.CurrentRowSelectionKeys[1];

        IReadOnlyList<ViewportSelectedRow> rows = selectable.ReadSelectedRows(
            selectAll: true,
            Array.Empty<ViewportRowSelectionRange>(),
            new[] { secondSegment });

        AssertSelectedRows("visual selection wrapped excluded", rows, "next");
    }

    private static void RunFilteredSelectionRangeAcrossResults(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-selection-range.log", "alpha-0\r\nskip\r\nalpha-1\r\nalpha-2\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 2);
        ISelectableViewportReader selectable = reader;
        ViewportRowSelectionKey first = selectable.CurrentRowSelectionKeys[0];
        reader.ReadFromPercentage(100d, 2);
        ViewportRowSelectionKey last = selectable.CurrentRowSelectionKeys[1];
        IReadOnlyList<ViewportSelectedRow> rows = selectable.ReadSelectedRows(
            selectAll: false,
            new[] { new ViewportRowSelectionRange(first, last) },
            Array.Empty<ViewportRowSelectionKey>());

        AssertSelectedRows("filtered selection range", rows, "alpha-0", "alpha-1", "alpha-2");
    }

    private static void RunFilteredSelectionSelectsAllRows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-selection-all.log", "alpha-0\r\nskip\r\nalpha-1\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));

        IReadOnlyList<ViewportSelectedRow> rows = ((ISelectableViewportReader)reader).ReadSelectedRows(
            selectAll: true,
            Array.Empty<ViewportRowSelectionRange>(),
            Array.Empty<ViewportRowSelectionKey>());

        AssertSelectedRows("filtered selection all", rows, "alpha-0", "alpha-1");
    }

    private static void RunFilteredSelectionCopiesCaptureCells(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-selection-cells.log", "aaabccc xx aabcc\r\nplain\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(a+)b(c+)", UseRegex: true, IgnoreCase: false));

        IReadOnlyList<ViewportSelectedRow> rows = ((ISelectableViewportReader)reader).ReadSelectedRows(
            selectAll: true,
            Array.Empty<ViewportRowSelectionRange>(),
            Array.Empty<ViewportRowSelectionKey>());

        AssertEqual("filtered selection cells row count", rows.Count, 1);
        AssertSequence("filtered selection cells", rows[0].Cells ?? Array.Empty<string>(), "aaabccc xx aabcc", "aaa", "ccc");
    }

    private static void RunFilteredSelectionCopiesLiteralTextCell(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-selection-literal-cell.log", "line.with.dot\r\nplain\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions(".", UseRegex: false, IgnoreCase: false));

        IReadOnlyList<ViewportSelectedRow> rows = ((ISelectableViewportReader)reader).ReadSelectedRows(
            selectAll: true,
            Array.Empty<ViewportRowSelectionRange>(),
            Array.Empty<ViewportRowSelectionKey>());

        AssertEqual("filtered selection literal cell row count", rows.Count, 1);
        AssertSequence("filtered selection literal cell", rows[0].Cells ?? Array.Empty<string>(), "line.with.dot");
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

    private static void RunAppendSearchAddsMatches(string tempRoot)
    {
        string path = WriteLog(tempRoot, "append-search-match.log", "alpha\r\nplain\r\n");

        using FilteredVisualRowReader initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));

        File.AppendAllText(path, "new alpha\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using FilteredVisualRowReader appended = BuildAppendedReader(initial, new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));
        appended.ReadFromPercentage(0d, 10);

        AssertEqual("append match count", appended.MatchedLineCount, 2L);
        AssertSequence("append match rows", appended.CurrentRows, "alpha", "new alpha");
    }

    private static void RunAppendSearchWithoutMatchKeepsCount(string tempRoot)
    {
        string path = WriteLog(tempRoot, "append-search-no-match.log", "alpha\r\nplain\r\n");

        using FilteredVisualRowReader initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));

        File.AppendAllText(path, "new plain\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using FilteredVisualRowReader appended = BuildAppendedReader(initial, new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));
        appended.ReadFromPercentage(0d, 10);

        AssertEqual("append no-match count", appended.MatchedLineCount, 1L);
        AssertSequence("append no-match rows", appended.CurrentRows, "alpha");
    }

    private static void RunAppendSearchRescansPartialLastLine(string tempRoot)
    {
        string path = WriteLog(tempRoot, "append-search-partial.log", "prefix alp");

        using FilteredVisualRowReader initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));

        File.AppendAllText(path, "ha\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using FilteredVisualRowReader appended = BuildAppendedReader(initial, new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));
        appended.ReadFromPercentage(0d, 10);

        AssertEqual("append partial count", appended.MatchedLineCount, 1L);
        AssertSequence("append partial rows", appended.CurrentRows, "prefix alpha");
    }

    private static void RunAppendSearchPreservesRegexCaptureGroups(string tempRoot)
    {
        string path = WriteLog(tempRoot, "append-search-captures.log", "aabcc\r\nplain\r\n");
        SearchOptions options = new("(a+)b(c+)", UseRegex: true, IgnoreCase: false);

        using FilteredVisualRowReader initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            options);

        File.AppendAllText(path, "aaabccc\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using FilteredVisualRowReader appended = BuildAppendedReader(initial, options);
        appended.ReadFromPercentage(0d, 10);
        IColumnViewportReader columns = appended;

        AssertSequence("append capture headers", columns.ColumnHeaders, "Text", "0", "1");
        AssertEqual("append capture row count", columns.CurrentCells.Count, 2);
        AssertEqual("append capture text", columns.CurrentCells[1][0], "aaabccc");
        AssertEqual("append capture group 0", columns.CurrentCells[1][1], "aaa");
        AssertEqual("append capture group 1", columns.CurrentCells[1][2], "ccc");
    }

    private static void RunAppendSearchInvertMatch(string tempRoot)
    {
        string path = WriteLog(tempRoot, "append-search-invert.log", "alpha\r\nplain\r\n");
        SearchOptions options = new("alpha", UseRegex: false, IgnoreCase: false, InvertMatch: true);

        using FilteredVisualRowReader initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            options);

        File.AppendAllText(path, "beta\r\nalpha again\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using FilteredVisualRowReader appended = BuildAppendedReader(initial, options);
        appended.ReadFromPercentage(0d, 10);

        AssertEqual("append invert count", appended.MatchedLineCount, 2L);
        AssertSequence("append invert rows", appended.CurrentRows, "plain", "beta");
    }

    private static void RunAppendSearchStalesWhenEarlierLineGrows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "append-search-stale-earlier.log", "one\r\ntwo alpha\r\n");

        using FilteredVisualRowReader initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));

        File.WriteAllText(path, "one extended\r\ntwo alpha\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        AssertThrows<FilteredLineStaleException>(
            "append search stale earlier line grows",
            () =>
            {
                using FilteredVisualRowReader appended = BuildAppendedReader(initial, new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));
            });
    }

    private static FilteredVisualRowReader BuildAppendedReader(FilteredVisualRowReader initial, SearchOptions options)
    {
        FilteredVisualRowReader? latest = null;
        LogSearchBuilder.BuildAppendedFilteredReaderIncremental(
            initial,
            options,
            new FileInfo(initial.FilePath).Length,
            preloadedVisibleLines: 10,
            update =>
            {
                if (update.Reader is not null)
                {
                    latest?.Dispose();
                    latest = update.Reader;
                }
            },
            CancellationToken.None);

        return latest ?? throw new InvalidOperationException("Append search did not publish a reader.");
    }

    private static void RunPageUpNearStartClampsToTop(string tempRoot)
    {
        string path = WriteLog(tempRoot, "page-up-near-start.log", "line-0\r\nline-1\r\nline-2\r\nline-3\r\nline-4\r\n");

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadNext(3);
        reader.ReadNext(1);
        reader.ReadPrevious(3);

        AssertSequence("page up near start", reader.CurrentRows, "line-0", "line-1", "line-2");
        AssertEqual("page up near start offset", reader.TopOffset, 0L);
    }

    private static void RunPageUpInsideWrappedFirstLineClampsToTop(string tempRoot)
    {
        string firstSegment = new('a', VisualRowReader.VisibleSegmentChars);
        string secondSegmentPrefix = "second-segment";
        string firstLine = firstSegment + secondSegmentPrefix;
        string path = WriteLog(tempRoot, "page-up-wrapped-first-line.log", firstLine + "\r\nline-1\r\n");

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadNext(2);
        reader.ReadNext(1);
        reader.ReadPrevious(3);

        AssertEqual("page up wrapped first row count", reader.CurrentRows.Count, 2);
        AssertEqual("page up wrapped first segment", reader.CurrentRows[0], firstSegment);
        AssertEqual("page up wrapped second segment", reader.CurrentRows[1], secondSegmentPrefix);
        AssertEqual("page up wrapped first offset", reader.TopOffset, 0L);
    }

    private static void RunRefreshTailAtEndShowsAppendedRows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "tail-at-end.log", "line-0\r\nline-1\r\n");

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(100d, 2);
        File.AppendAllText(path, "line-2\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshTail(2);

        AssertSequence("tail at end rows", reader.CurrentRows, "line-1", "line-2");
    }

    private static void RunRefreshTailSmallInitialFileShowsAppendedRows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "tail-small-initial.log", "line-0\r\nline-1\r\n");

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(0d, 20);
        AssertEqual("tail small initial starts at end", reader.IsAtKnownEnd, true);
        File.AppendAllText(path, "line-2\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshTail(20);

        AssertSequence("tail small initial rows", reader.CurrentRows, "line-0", "line-1", "line-2");
        AssertEqual("tail small initial at end", reader.IsAtKnownEnd, true);
    }

    private static void RunRefreshFileSizeAwayFromEndLetsJumpEndSeeAppendedRows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "tail-away-then-end.log", "line-0\r\nline-1\r\nline-2\r\nline-3\r\n");

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(0d, 2);
        File.AppendAllText(path, "line-4\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        AssertEqual("tail away file size changed", reader.RefreshFileSize(), true);
        reader.ReadFromPercentage(100d, 2);

        AssertSequence("tail away then end rows", reader.CurrentRows, "line-3", "line-4");
        AssertEqual("tail away then end at end", reader.IsAtKnownEnd, true);
    }

    private static void RunRefreshTailAwayFromEndDoesNotMove(string tempRoot)
    {
        string path = WriteLog(tempRoot, "tail-away-from-end.log", "line-0\r\nline-1\r\nline-2\r\nline-3\r\n");

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(0d, 2);
        File.AppendAllText(path, "line-4\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshTail(2);

        AssertSequence("tail away rows", reader.CurrentRows, "line-0", "line-1");
    }

    private static void RunRefreshTailAfterTruncateReloadsFromStart(string tempRoot)
    {
        string path = WriteLog(tempRoot, "tail-truncate.log", "line-0\r\nline-1\r\nline-2\r\nline-3\r\n");

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(100d, 2);
        File.WriteAllText(path, "new-0\r\nnew-1\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshTail(2);

        AssertSequence("tail truncate rows", reader.CurrentRows, "new-0", "new-1");
    }

    private static void RunRefreshTailAfterTruncateToEmptyClearsRows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "tail-truncate-empty.log", "line-0\r\nline-1\r\nline-2\r\n");

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(100d, 2);
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _ = reader.IsAtKnownEnd;
        reader.RefreshTail(2);

        AssertEqual("tail truncate empty count", reader.CurrentRows.Count, 0);
        AssertEqual("tail truncate empty size", reader.FileSize, 0L);
    }

    private static void RunRefreshFileSizeAfterTruncateLetsJumpEndSeeAppendedRows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "tail-truncate-append.log", "line-0\r\nline-1\r\nline-2\r\n");

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(100d, 2);
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshTail(2);
        File.AppendAllText(path, "after-0\r\nafter-1\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        AssertEqual("tail truncate append file size changed", reader.RefreshFileSize(), true);
        reader.ReadFromPercentage(100d, 2);

        AssertSequence("tail truncate append rows", reader.CurrentRows, "after-0", "after-1");
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

    private static void AssertSelectedRows(string name, IReadOnlyList<ViewportSelectedRow> actual, params string[] expected)
    {
        AssertEqual(name + " count", actual.Count, expected.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            AssertEqual(name + " [" + i + "]", actual[i].Text, expected[i]);
        }
    }

    private static void AssertEqual<T>(string name, T actual, T expected)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            throw new InvalidOperationException($"{name}: expected '{expected}', got '{actual}'.");
        }
    }

    private static void AssertThrows<TException>(string name, Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{name}: expected exception {typeof(TException).Name}.");
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
