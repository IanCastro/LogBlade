using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

internal static class Program
{
    private static int Main()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "LogBladeBackSmoke", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempRoot);

            RunDisplayParserJsonTemplate();
            RunDisplayParserJsonWithTrailingText();
            RunDisplayParserJsonSkipsInvalidPrefixCandidate();
            RunDisplayParserGeneratesJsonTemplateFromSample();
            RunDisplayParserGeneratesJsonTemplateFromMultipleSamples();
            RunDisplayParserGeneratesNestedJsonTemplate();
            RunDisplayParserIgnoresInvalidJsonTemplatePaths();
            RunDisplayParserGeneratesEmptyTemplateWithoutJson();
            RunDisplayParserRegexNamedDisplay();
            RunDisplayParserRegexDefaultFullMatch();
            RunDisplayParserRegexPreservesPatternSpaces();
            RunDisplayParserRegexInvalidRegexValidation();
            RunDisplayParserFallbackOriginal();
            RunDisplayParserRegexThenJsonTemplate();
            RunDisplayParserSecondStageFailureReturnsFirstOutput();
            RunDisplayParserRegexReplaceSimple();
            RunDisplayParserRegexReplaceGlobal();
            RunDisplayParserRegexReplaceEmptyReplacement();
            RunDisplayParserRegexReplacePreservesSpaces();
            RunDisplayParserRegexReplaceAllowsSpacePattern();
            RunDisplayParserRegexReplaceGroups();
            RunDisplayParserRegexReplaceNoMatchAllowsNextStage();
            RunDisplayParserRegexReplaceThenJsonTemplate();
            RunDisplayParserRegexReplaceInvalidRegexValidation();
            RunSearchUsesDisplayParserLiteral(tempRoot);
            RunSearchUsesDisplayParserRegexCaptures(tempRoot);
            RunAppendSearchUsesDisplayParser(tempRoot);
            RunSearchUsesCascadedDisplayParserLiteral(tempRoot);
            RunSearchUsesCascadedDisplayParserRegexCaptures(tempRoot);
            RunAppendSearchUsesCascadedDisplayParser(tempRoot);
            RunRegexCaptureGroups(tempRoot);
            RunRegexNamedCaptureGroups(tempRoot);
            RunRegexMixedNamedAndUnnamedCaptureGroups(tempRoot);
            RunRegexNonCapturingGroupsDoNotCreateColumns(tempRoot);
            RunRegexOnlyNonCapturingGroupsUsesPlainRows(tempRoot);
            RunRegexOptionalCaptureGroupUsesEmptyCell(tempRoot);
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
            RunSearchRowOrdinalLookup(tempRoot);
            RunDisplayParserMainViewerParsesWholeWrappedJsonLine(tempRoot);
            RunDisplayParserMainViewerSkipsRawSegmentsAfterShortParse(tempRoot);
            RunDisplayParserMainViewerWrapsParsedOutput(tempRoot);
            RunDisplayParserMainViewerSelectionCopiesParsedLine(tempRoot);
            RunDisplayParserMainViewerSplitsParsedNewlines(tempRoot);
            RunDisplayParserMainViewerPreservesEmptyParsedLines(tempRoot);
            RunDisplayParserMainViewerWrapsParsedLinesIndependently(tempRoot);
            RunDisplayParserMainViewerSelectionPreservesParsedNewlines(tempRoot);
            RunSearchFindsTextOnSecondParsedLine(tempRoot);
            RunSearchRepeatsCapturesAcrossParsedLines(tempRoot);
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
            RunNonBacktrackingRegexRejectsUnsupportedPattern();
            RunNonBacktrackingRegexLeadingDotStarNoMatchOnLongLine(tempRoot);
            RunNonBacktrackingRegexLeadingDotStarMatchesLongLine(tempRoot);
            RunCascadedLiteralSearchFiltersPrevious(tempRoot);
            RunChangedCascadedSearchFiltersPreviousReader(tempRoot);
            RunCascadedInvertMatch(tempRoot);
            RunCascadedInvalidRegexValidation();
            RunCascadedCaptureGroupsUseLastCapturingRegex(tempRoot);
            RunCascadedNamedCaptureGroupsSurviveNonCapturingStage(tempRoot);
            RunStagedLiteralSearchKeepsIntermediateResults(tempRoot);
            RunStagedCaptureGroupsUsePerStageCaptures(tempRoot);
            RunStagedNamedCaptureGroupsUsePerStageHeaders(tempRoot);
            RunAppendStagedSearchKeepsIntermediateResults(tempRoot);
            RunAppendSearchCascadeAddsMatches(tempRoot);
            RunAppendSearchAddsMatches(tempRoot);
            RunAppendSearchWithoutMatchKeepsCount(tempRoot);
            RunAppendSearchRescansPartialLastLine(tempRoot);
            RunAppendSearchPreservesRegexCaptureGroups(tempRoot);
            RunAppendSearchPreservesNamedCaptureGroups(tempRoot);
            RunAppendSearchInvertMatch(tempRoot);
            RunAppendSearchStalesWhenEarlierLineGrows(tempRoot);
            RunPausedStagedSearchPublishesCheckpointWithoutMatches(tempRoot);
            RunPausedAppendStagedSearchPublishesCheckpointWhenAlreadyCancelled(tempRoot);
            RunPausedResumeStagedSearchPublishesCheckpointWhenAlreadyCancelled(tempRoot);
            RunPausedStagedSearchPublishesCheckpointAfterCallbackCancellation(tempRoot);
            RunPausedStagedSearchResumesFromProcessedOffset(tempRoot);
            RunPartialStagedSearchReaderResumesFromProcessedOffset(tempRoot);
            RunResumeStagedSearchReprocessesIncompleteLastLine(tempRoot);
            RunResumeStagedSearchContinuesAfterLineBreak(tempRoot);
            RunResumeStagedSearchPreservesCascadeReaders(tempRoot);
            RunPageUpNearStartClampsToTop(tempRoot);
            RunPageUpInsideWrappedFirstLineClampsToTop(tempRoot);
            RunRefreshTailAtEndShowsAppendedRows(tempRoot);
            RunRefreshTailAtEndReloadsSameSizeChange(tempRoot);
            RunReloadAfterFileChangeSameSizeReloadsCurrentViewport(tempRoot);
            RunReloadAfterFileChangePreservesViewportPosition(tempRoot);
            RunFilteredReloadAfterFileChangeSameSizeReloadsCurrentViewport(tempRoot);
            RunFilteredReloadAfterFileChangeStalesWhenLineBoundaryChanges(tempRoot);
            RunRefreshTailSmallInitialFileShowsAppendedRows(tempRoot);
            RunRefreshFileSizeAwayFromEndLetsJumpEndSeeAppendedRows(tempRoot);
            RunRefreshTailAwayFromEndDoesNotMove(tempRoot);
            RunRefreshTailAfterTruncateReloadsFromStart(tempRoot);
            RunRefreshTailAfterTruncateToEmptyClearsRows(tempRoot);
            RunObservedZeroRepeatedKeepsConfirmedSize(tempRoot);
            RunObservedZeroThenLargerComparesWithConfirmedSize(tempRoot);
            RunObservedZeroThenSmallerNonZeroTruncates(tempRoot);
            RunObservedZeroThenSameSizeReloadsVisualRows(tempRoot);
            RunObservedZeroPreservesViewportRowsInternally(tempRoot);
            RunObservedZeroRefreshTailPreservesViewportRowsInternally(tempRoot);
            RunObservedZeroReloadPreservesViewportRowsInternally(tempRoot);
            RunObservedZeroReloadFromEndFollowsRestoredTail(tempRoot);
            RunObservedZeroNavigationPreservesViewportRowsInternally(tempRoot);
            RunFilteredObservedZeroRepeatedKeepsConfirmedSize(tempRoot);
            RunFilteredObservedZeroPreservesViewportRowsInternally(tempRoot);
            RunFilteredObservedZeroPreservesConfirmedEndState(tempRoot);
            RunFilteredObservedZeroReloadAwayFromEndPreservesPosition(tempRoot);
            RunFilteredObservedZeroNavigationPreservesViewportRowsInternally(tempRoot);
            RunFilteredObservedZeroThenSameSizeReloadsRows(tempRoot);
            RunFilteredObservedZeroThenSmallerKeepsConfirmedForStaleDecision(tempRoot);
            RunAppendAfterFilteredObservedZeroUsesConfirmedSize(tempRoot);
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

    private static void RunDisplayParserJsonTemplate()
    {
        DisplayParserRule rule = ParserRule(JsonStage("{Timestamp} [{Logger}] {upper:Level} {Logger} - {Message}"));

        string input = "{ \"Timestamp\": \"2025-09-12 14:50:48.637060\", \"Level\": \"Info\", \"Logger\": \"EventScheduler\", \"Message\": \"Strategy task JT67_48_250912145048_00064 is running\" }";
        string parsed = DisplayParserEvaluator.EvaluateOrOriginal(rule, input);

        AssertEqual(
            "display parser json template",
            parsed,
            "2025-09-12 14:50:48.637060 [EventScheduler] INFO EventScheduler - Strategy task JT67_48_250912145048_00064 is running");
    }

    private static void RunDisplayParserJsonWithTrailingText()
    {
        DisplayParserRule rule = ParserRule(JsonStage("{upper:Level} {Logger} - {Message}"));

        string input = "{ \"Level\": \"Info\", \"Logger\": \"EventScheduler\", \"Message\": \"running\" } 2025-09-12 [EventScheduler]";
        AssertEqual("display parser json trailing text", DisplayParserEvaluator.EvaluateOrOriginal(rule, input), "INFO EventScheduler - running");
    }

    private static void RunDisplayParserJsonSkipsInvalidPrefixCandidate()
    {
        DisplayParserRule rule = ParserRule(JsonStage("{Level}"));
        string input = "2026-06-24 [Logger] {\"Level\":\"Info\"}";

        AssertEqual(
            "display parser json skips invalid prefix candidate",
            DisplayParserEvaluator.EvaluateOrOriginal(rule, input),
            "Info");
    }

    private static void RunDisplayParserGeneratesJsonTemplateFromSample()
    {
        string sample = "2026-06-24 [Logger] prefix {\"Timestamp\":\"2026-06-24\",\"Level\":\"Info\"} trailing";

        AssertEqual(
            "display parser generated json template",
            DisplayParserEvaluator.GenerateJsonTemplateFromSample(sample),
            "Timestamp - {Timestamp}, Level - {Level}");
    }

    private static void RunDisplayParserGeneratesJsonTemplateFromMultipleSamples()
    {
        string sample =
            "{\"Key\":\"first\",\"Level\":\"Info\"}\r\n" +
            "not json\r\n" +
            "{\"key\":\"second\",\"Message\":\"running\"}";

        AssertEqual(
            "display parser generated distinct json template",
            DisplayParserEvaluator.GenerateJsonTemplateFromSample(sample),
            "Key - {Key}, Level - {Level}, Message - {Message}");
    }

    private static void RunDisplayParserGeneratesNestedJsonTemplate()
    {
        string sample =
            "{\"State\":{\"Message\":\"running\",\"Code\":42}," +
            "\"items\":[{\"name\":\"first\"},{\"name\":\"second\"}]}";
        string generatedTemplate = DisplayParserEvaluator.GenerateJsonTemplateFromSample(sample);

        AssertEqual(
            "display parser generated nested json template",
            generatedTemplate,
            "State.Message - {State.Message}, State.Code - {State.Code}, " +
            "items.0.name - {items.0.name}, items.1.name - {items.1.name}");
        AssertEqual(
            "display parser evaluates generated nested json template",
            DisplayParserEvaluator.EvaluateOrOriginal(ParserRule(JsonStage(generatedTemplate)), sample),
            "State.Message - running, State.Code - 42, items.0.name - first, items.1.name - second");
    }

    private static void RunDisplayParserIgnoresInvalidJsonTemplatePaths()
    {
        string sample =
            "{\"Valid\":\"yes\",\"State\":{\"{OriginalFormat}\":\"ignored\",\"Message\":\"running\"}," +
            "\"invalid.name\":\"ignored\",\"invalid{brace\":\"ignored\"}";

        AssertEqual(
            "display parser ignores invalid json template paths",
            DisplayParserEvaluator.GenerateJsonTemplateFromSample(sample),
            "Valid - {Valid}, State.Message - {State.Message}");
    }

    private static void RunDisplayParserGeneratesEmptyTemplateWithoutJson()
    {
        AssertEqual(
            "display parser generated empty template",
            DisplayParserEvaluator.GenerateJsonTemplateFromSample("plain text\r\n{invalid"),
            string.Empty);
    }

    private static void RunDisplayParserRegexNamedDisplay()
    {
        DisplayParserRule rule = ParserRule(RegexStage(
            @"(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+).*?\[(?<Logger>[^\]]+)\].*?(?<Level>Info).*? - (?<Message>.*)",
            "{Timestamp} [{Logger}] {upper:Level} {Logger} - {Message}"));

        string input = "2025-09-12 14:50:48.637060 [EventScheduler] Info EventScheduler - Strategy task JT67_48_250912145048_00064 is running";
        AssertEqual(
            "display parser regex named display",
            DisplayParserEvaluator.EvaluateOrOriginal(rule, input),
            "2025-09-12 14:50:48.637060 [EventScheduler] INFO EventScheduler - Strategy task JT67_48_250912145048_00064 is running");
    }

    private static void RunDisplayParserRegexDefaultFullMatch()
    {
        DisplayParserRule rule = ParserRule(RegexStage("user-(?<user>[a-z]+)"));

        AssertEqual("display parser regex default full match", DisplayParserEvaluator.EvaluateOrOriginal(rule, "prefix user-ana suffix"), "user-ana");
    }

    private static void RunDisplayParserRegexPreservesPatternSpaces()
    {
        DisplayParserRule rule = ParserRule(RegexStage(" abc ", "hit"));

        AssertEqual("display parser regex preserves pattern spaces match", DisplayParserEvaluator.EvaluateOrOriginal(rule, "x abc y"), "hit");
        AssertEqual("display parser regex preserves pattern spaces no match", DisplayParserEvaluator.EvaluateOrOriginal(rule, "abc"), "abc");
    }

    private static void RunDisplayParserRegexInvalidRegexValidation()
    {
        try
        {
            DisplayParserEvaluator.ValidateStage(RegexStage(@"("));
        }
        catch (ArgumentException ex) when (ex.Message.StartsWith("Regex error:", StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException("display parser regex invalid regex: expected regex validation failure.");
    }

    private static void RunDisplayParserFallbackOriginal()
    {
        DisplayParserRule rule = ParserRule(JsonStage("{Level} {Message}"));

        AssertEqual("display parser fallback original", DisplayParserEvaluator.EvaluateOrOriginal(rule, "not json"), "not json");
    }

    private static void RunDisplayParserRegexThenJsonTemplate()
    {
        DisplayParserRule rule = ParserRule(
            RegexStage(@": (?<json>.*)", "{json}"),
            JsonStage("{Key}"));

        AssertEqual("display parser regex then json", DisplayParserEvaluator.EvaluateOrOriginal(rule, "Out[0]: {\"Key\":\"Value\"}"), "Value");
    }

    private static void RunDisplayParserSecondStageFailureReturnsFirstOutput()
    {
        DisplayParserRule rule = ParserRule(
            RegexStage(@"payload=(?<json>.*)", "{json}"),
            JsonStage("{Key}"));

        AssertEqual("display parser second stage failure", DisplayParserEvaluator.EvaluateOrOriginal(rule, "payload=not-json"), "not-json");
    }

    private static void RunDisplayParserRegexReplaceSimple()
    {
        DisplayParserRule rule = ParserRule(RegexReplaceStage(@"\u0001", "|"));

        AssertEqual("display parser regex replace simple", DisplayParserEvaluator.EvaluateOrOriginal(rule, "a\u0001b"), "a|b");
    }

    private static void RunDisplayParserRegexReplaceGlobal()
    {
        DisplayParserRule rule = ParserRule(RegexReplaceStage(@"\u0001", "|"));

        AssertEqual("display parser regex replace global", DisplayParserEvaluator.EvaluateOrOriginal(rule, "a\u0001b\u0001c"), "a|b|c");
    }

    private static void RunDisplayParserRegexReplaceEmptyReplacement()
    {
        DisplayParserRule rule = ParserRule(RegexReplaceStage(@"[0-9]+", string.Empty));

        AssertEqual("display parser regex replace empty", DisplayParserEvaluator.EvaluateOrOriginal(rule, "task-123-ready"), "task--ready");
    }

    private static void RunDisplayParserRegexReplacePreservesSpaces()
    {
        DisplayParserRule rule = ParserRule(RegexReplaceStage(@"-", " | "));

        AssertEqual("display parser regex replace preserves spaces", DisplayParserEvaluator.EvaluateOrOriginal(rule, "a-b"), "a | b");
    }

    private static void RunDisplayParserRegexReplaceAllowsSpacePattern()
    {
        DisplayParserStage stage = RegexReplaceStage(" ", "|");
        DisplayParserEvaluator.ValidateStage(stage);
        DisplayParserRule rule = ParserRule(stage);

        AssertEqual("display parser regex replace allows space pattern", DisplayParserEvaluator.EvaluateOrOriginal(rule, "a b c"), "a|b|c");
    }

    private static void RunDisplayParserRegexReplaceGroups()
    {
        DisplayParserRule rule = ParserRule(RegexReplaceStage(@"(\w+)=(\w+)", "$1:$2"));

        AssertEqual("display parser regex replace groups", DisplayParserEvaluator.EvaluateOrOriginal(rule, "user=ana id=42"), "user:ana id:42");
    }

    private static void RunDisplayParserRegexReplaceNoMatchAllowsNextStage()
    {
        DisplayParserRule rule = ParserRule(
            RegexReplaceStage(@"missing", "replacement"),
            RegexStage(@"abc", "{0}"));

        AssertEqual("display parser regex replace no match allows next stage", DisplayParserEvaluator.EvaluateOrOriginal(rule, "abc"), "abc");
    }

    private static void RunDisplayParserRegexReplaceThenJsonTemplate()
    {
        DisplayParserRule rule = ParserRule(
            RegexReplaceStage(@"\u0001", "|"),
            JsonStage("{Key}"));

        AssertEqual("display parser regex replace then json", DisplayParserEvaluator.EvaluateOrOriginal(rule, "{\"Key\":\"A\u0001B\"}"), "A|B");
    }

    private static void RunDisplayParserRegexReplaceInvalidRegexValidation()
    {
        try
        {
            DisplayParserEvaluator.ValidateStage(RegexReplaceStage(@"(", "|"));
        }
        catch (ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException("display parser regex replace invalid regex: expected validation failure.");
    }

    private static void RunSearchUsesDisplayParserLiteral(string tempRoot)
    {
        string path = WriteLog(
            tempRoot,
            "display-parser-search-literal.log",
            "{\"Level\":\"Info\",\"Message\":\"ready\"}\r\n{\"Level\":\"Error\",\"Message\":\"failed\"}\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{upper:Level} - {Message}"));

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("ERROR - failed", UseRegex: false, IgnoreCase: false),
            parser);

        reader.ReadFromPercentage(0d, 10);
        IColumnViewportReader columns = reader;
        AssertSequence("display parser literal rows", reader.CurrentRows, "ERROR - failed");
        AssertSequence("display parser literal cells", columns.CurrentCells[0], "2", "ERROR - failed");
    }

    private static void RunSearchUsesDisplayParserRegexCaptures(string tempRoot)
    {
        string path = WriteLog(
            tempRoot,
            "display-parser-search-regex.log",
            "{\"Level\":\"Info\",\"Message\":\"ready\"}\r\n{\"Level\":\"Error\",\"Message\":\"failed\"}\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{upper:Level} - {Message}"));

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(?<level>ERROR) - (?<message>failed)", UseRegex: true, IgnoreCase: false),
            parser);

        reader.ReadFromPercentage(0d, 10);
        IColumnViewportReader columns = reader;
        AssertSequence("display parser regex headers", columns.ColumnHeaders, "#", "Text", "level", "message");
        AssertSequence("display parser regex cells", columns.CurrentCells[0], "2", "ERROR - failed", "ERROR", "failed");
    }

    private static void RunAppendSearchUsesDisplayParser(string tempRoot)
    {
        string path = WriteLog(tempRoot, "display-parser-append.log", "{\"Level\":\"Info\",\"Message\":\"ready\"}\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{upper:Level} - {Message}"));

        using FilteredVisualRowReader initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("ERROR", UseRegex: false, IgnoreCase: false),
            parser);

        File.AppendAllText(path, "{\"Level\":\"Error\",\"Message\":\"failed\"}\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using FilteredVisualRowReader appended = BuildAppendedReader(initial, new SearchOptions("ERROR", UseRegex: false, IgnoreCase: false), parser);
        appended.ReadFromPercentage(0d, 10);

        AssertSequence("display parser append rows", appended.CurrentRows, "ERROR - failed");
        AssertSequence("display parser append cells", ((IColumnViewportReader)appended).CurrentCells[0], "2", "ERROR - failed");
    }

    private static void RunSearchUsesCascadedDisplayParserLiteral(string tempRoot)
    {
        string path = WriteLog(
            tempRoot,
            "display-parser-cascade-search-literal.log",
            "Out[0]: {\"Key\":\"Value\"}\r\nOut[1]: {\"Key\":\"Other\"}\r\n");
        DisplayParserRule parser = ParserRule(
            RegexStage(@": (?<json>.*)", "{json}"),
            JsonStage("KEY={Key}"));

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("KEY=Value", UseRegex: false, IgnoreCase: false),
            parser);

        reader.ReadFromPercentage(0d, 10);
        IColumnViewportReader columns = reader;
        AssertSequence("cascaded display parser literal rows", reader.CurrentRows, "KEY=Value");
        AssertSequence("cascaded display parser literal cells", columns.CurrentCells[0], "1", "KEY=Value");
    }

    private static void RunSearchUsesCascadedDisplayParserRegexCaptures(string tempRoot)
    {
        string path = WriteLog(
            tempRoot,
            "display-parser-cascade-search-regex.log",
            "Out[0]: {\"Level\":\"Info\",\"Message\":\"ready\"}\r\nOut[1]: {\"Level\":\"Error\",\"Message\":\"failed\"}\r\n");
        DisplayParserRule parser = ParserRule(
            RegexStage(@": (?<json>.*)", "{json}"),
            JsonStage("{upper:Level} - {Message}"));

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(?<level>ERROR) - (?<message>failed)", UseRegex: true, IgnoreCase: false),
            parser);

        reader.ReadFromPercentage(0d, 10);
        IColumnViewportReader columns = reader;
        AssertSequence("cascaded display parser regex headers", columns.ColumnHeaders, "#", "Text", "level", "message");
        AssertSequence("cascaded display parser regex cells", columns.CurrentCells[0], "2", "ERROR - failed", "ERROR", "failed");
    }

    private static void RunAppendSearchUsesCascadedDisplayParser(string tempRoot)
    {
        string path = WriteLog(tempRoot, "display-parser-cascade-append.log", "Out[0]: {\"Level\":\"Info\",\"Message\":\"ready\"}\r\n");
        DisplayParserRule parser = ParserRule(
            RegexStage(@": (?<json>.*)", "{json}"),
            JsonStage("{upper:Level} - {Message}"));

        using FilteredVisualRowReader initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("ERROR", UseRegex: false, IgnoreCase: false),
            parser);

        File.AppendAllText(path, "Out[1]: {\"Level\":\"Error\",\"Message\":\"failed\"}\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using FilteredVisualRowReader appended = BuildAppendedReader(initial, new SearchOptions("ERROR", UseRegex: false, IgnoreCase: false), parser);
        appended.ReadFromPercentage(0d, 10);

        AssertSequence("cascaded display parser append rows", appended.CurrentRows, "ERROR - failed");
        AssertSequence("cascaded display parser append cells", ((IColumnViewportReader)appended).CurrentCells[0], "2", "ERROR - failed");
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

        AssertSequence("capture headers", columns.ColumnHeaders, "#", "Text", "0", "1");
        AssertEqual("capture row count", columns.CurrentCells.Count, 1);
        AssertEqual("capture line number", columns.CurrentCells[0][0], "1");
        AssertEqual("capture text", columns.CurrentCells[0][1], "aaabccc xx aabcc");
        AssertEqual("first match group 0", columns.CurrentCells[0][2], "aaa");
        AssertEqual("first match group 1", columns.CurrentCells[0][3], "ccc");
    }

    private static void RunRegexNamedCaptureGroups(string tempRoot)
    {
        string path = WriteLog(tempRoot, "named-capture-groups.log", "code-42 user-ian\r\nplain\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("code-(?<code>\\d+) user-(?<user>[a-z]+)", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        IColumnViewportReader columns = reader;

        AssertSequence("named capture headers", columns.ColumnHeaders, "#", "Text", "code", "user");
        AssertSequence("named capture cells", columns.CurrentCells[0], "1", "code-42 user-ian", "42", "ian");
    }

    private static void RunRegexMixedNamedAndUnnamedCaptureGroups(string tempRoot)
    {
        string path = WriteLog(tempRoot, "mixed-capture-groups.log", "aaabccc\r\nplain\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(a+)b(?<suffix>c+)", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        IColumnViewportReader columns = reader;

        AssertSequence("mixed capture headers", columns.ColumnHeaders, "#", "Text", "0", "suffix");
        AssertSequence("mixed capture cells", columns.CurrentCells[0], "1", "aaabccc", "aaa", "ccc");
    }

    private static void RunRegexNonCapturingGroupsDoNotCreateColumns(string tempRoot)
    {
        string path = WriteLog(tempRoot, "non-capturing-groups.log", "GET /api/orders\r\nPOST /api/users\r\nplain\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(?:GET|POST) /api/(?<resource>\\w+)", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        IColumnViewportReader columns = reader;

        AssertSequence("non-capturing headers", columns.ColumnHeaders, "#", "Text", "resource");
        AssertSequence("non-capturing first cells", columns.CurrentCells[0], "1", "GET /api/orders", "orders");
        AssertSequence("non-capturing second cells", columns.CurrentCells[1], "2", "POST /api/users", "users");
    }

    private static void RunRegexOnlyNonCapturingGroupsUsesPlainRows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "only-non-capturing-groups.log", "GET /api/orders\r\nplain\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(?:GET|POST) /api/\\w+", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        IColumnViewportReader columns = reader;

        AssertSequence("only non-capturing headers", columns.ColumnHeaders, "#", "Text");
        AssertSequence("only non-capturing cells", columns.CurrentCells[0], "1", "GET /api/orders");
    }

    private static void RunRegexOptionalCaptureGroupUsesEmptyCell(string tempRoot)
    {
        string path = WriteLog(tempRoot, "optional-capture-groups.log", "code-\r\ncode-42\r\nplain\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("code-(?<code>\\d+)?", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        IColumnViewportReader columns = reader;

        AssertSequence("optional capture headers", columns.ColumnHeaders, "#", "Text", "code");
        AssertSequence("optional capture empty cells", columns.CurrentCells[0], "1", "code-", string.Empty);
        AssertSequence("optional capture filled cells", columns.CurrentCells[1], "2", "code-42", "42");
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
        IColumnViewportReader columns = reader;
        AssertSequence("regex no-group headers", columns.ColumnHeaders, "#", "Text");
        AssertSequence("regex no-group cells", columns.CurrentCells[0], "1", "aaabccc");
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
        IColumnViewportReader columns = reader;
        AssertSequence("literal headers", columns.ColumnHeaders, "#", "Text");
        AssertSequence("literal cells", columns.CurrentCells[0], "1", "line.with.dot");
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
        IColumnViewportReader columns = reader;
        AssertSequence("literal invert headers", columns.ColumnHeaders, "#", "Text");
        AssertSequence("literal invert first cells", columns.CurrentCells[0], "2", "plain");
        AssertSequence("literal invert second cells", columns.CurrentCells[1], "3", "beta");
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
        IColumnViewportReader columns = reader;
        AssertSequence("regex invert headers", columns.ColumnHeaders, "#", "Text");
        AssertSequence("regex invert first cells", columns.CurrentCells[0], "2", "plain");
        AssertSequence("regex invert second cells", columns.CurrentCells[1], "3", "beta");
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
        IColumnViewportReader columns = reader;
        AssertSequence("regex capture invert headers", columns.ColumnHeaders, "#", "Text");
        AssertSequence("regex capture invert cells", columns.CurrentCells[0], "2", "plain");
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

        AssertSequence("wrapped headers", columns.ColumnHeaders, "#", "Text", "0", "1");
        AssertEqual("wrapped row count", columns.CurrentCells.Count, 2);
        AssertEqual("wrapped line number", columns.CurrentCells[0][0], "1");
        AssertEqual("wrapped first text", columns.CurrentCells[0][1], longText[..VisualRowReader.VisibleSegmentChars]);
        AssertEqual("wrapped first group 0", columns.CurrentCells[0][2], "aaa");
        AssertEqual("wrapped first group 1", columns.CurrentCells[0][3], "ccc");
        AssertEqual("wrapped second line number", columns.CurrentCells[1][0], "1");
        AssertEqual("wrapped second text", columns.CurrentCells[1][1], longText[VisualRowReader.VisibleSegmentChars..]);
        AssertEqual("wrapped second group 0", columns.CurrentCells[1][2], "aaa");
        AssertEqual("wrapped second group 1", columns.CurrentCells[1][3], "ccc");
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

    private static void RunSearchRowOrdinalLookup(string tempRoot)
    {
        string path = WriteLog(tempRoot, "search-row-ordinal.log", "alpha-0\r\nskip\r\nalpha-1\r\nalpha-2\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 2);
        ViewportRowSelectionKey secondMatch = ((ISelectableViewportReader)reader).CurrentRowSelectionKeys[1];
        reader.ReadFromPercentage(100d, 2);

        AssertEqual("search row ordinal available", ((IRowOrdinalViewportReader)reader).TryGetRowOrdinal(secondMatch, out long rowOrdinal), true);
        AssertEqual("search row ordinal value", rowOrdinal, 1L);

        reader.ReadFromRowOrdinal(rowOrdinal, 2);
        AssertSequence("search row ordinal rows", reader.CurrentRows, "alpha-1", "alpha-2");
    }

    private static void RunDisplayParserMainViewerParsesWholeWrappedJsonLine(string tempRoot)
    {
        string padding = new('x', VisualRowReader.VisibleSegmentChars + 32);
        string json = "{ \"Padding\": \"" + padding + "\", \"Timestamp\": \"2026-06-11 10:20:30.123\", \"Level\": \"Info\", \"Logger\": \"LongLine\", \"Message\": \"done\" }";
        string path = WriteLog(tempRoot, "display-parser-main-whole-line.log", json + "\r\nnext\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{Timestamp} [{Logger}] {upper:Level} {Logger} - {Message}"));

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0, parser);
        reader.ReadFromPercentage(0d, 4);

        AssertSequence("display parser main whole line", reader.CurrentRows, "2026-06-11 10:20:30.123 [LongLine] INFO LongLine - done", "next");
    }

    private static void RunDisplayParserMainViewerSkipsRawSegmentsAfterShortParse(string tempRoot)
    {
        string padding = new('y', VisualRowReader.VisibleSegmentChars * 2);
        string json = "{ \"Padding\": \"" + padding + "\", \"Message\": \"short\" }";
        string path = WriteLog(tempRoot, "display-parser-main-short-output.log", json + "\r\nnext\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{Message}"));

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0, parser);
        reader.ReadFromPercentage(0d, 5);

        AssertSequence("display parser main short output", reader.CurrentRows, "short", "next");
    }

    private static void RunDisplayParserMainViewerWrapsParsedOutput(string tempRoot)
    {
        string message = new('m', VisualRowReader.VisibleSegmentChars + 7);
        string path = WriteLog(tempRoot, "display-parser-main-long-output.log", "{ \"Message\": \"" + message + "\" }\r\nnext\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{Message}"));

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0, parser);
        reader.ReadFromPercentage(0d, 3);

        AssertSequence(
            "display parser main long output",
            reader.CurrentRows,
            new string('m', VisualRowReader.VisibleSegmentChars),
            new string('m', 7),
            "next");
    }

    private static void RunDisplayParserMainViewerSelectionCopiesParsedLine(string tempRoot)
    {
        string message = new('s', VisualRowReader.VisibleSegmentChars + 9);
        string path = WriteLog(tempRoot, "display-parser-main-selection.log", "{ \"Message\": \"" + message + "\" }\r\nnext\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{Message}"));

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0, parser);
        reader.ReadFromPercentage(0d, 3);
        ISelectableViewportReader selectable = reader;
        ViewportRowSelectionKey secondSegment = selectable.CurrentRowSelectionKeys[1];

        IReadOnlyList<ViewportSelectedRow> rows = selectable.ReadSelectedRows(
            selectAll: false,
            new[] { new ViewportRowSelectionRange(secondSegment, secondSegment) },
            Array.Empty<ViewportRowSelectionKey>());

        AssertSelectedRows("display parser main selection", rows, message);
    }

    private static void RunDisplayParserMainViewerSplitsParsedNewlines(string tempRoot)
    {
        string[] separators = ["\n", "\r\n", "\r"];
        for (int i = 0; i < separators.Length; i++)
        {
            string path = WriteLog(tempRoot, $"display-parser-main-newline-{i}.log", "{\"First\":\"a\",\"Second\":\"b\"}\r\n");
            DisplayParserRule parser = ParserRule(JsonStage("{First}" + separators[i] + "{Second}"));

            using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0, parser);
            reader.ReadFromPercentage(0d, 3);

            AssertSequence($"display parser main newline {i}", reader.CurrentRows, "a", "b");
        }
    }

    private static void RunDisplayParserMainViewerPreservesEmptyParsedLines(string tempRoot)
    {
        string path = WriteLog(tempRoot, "display-parser-main-empty-lines.log", "{\"First\":\"a\",\"Second\":\"b\"}\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{First}\n\n{Second}\n"));

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0, parser);
        reader.ReadFromPercentage(0d, 5);

        AssertSequence("display parser main empty lines", reader.CurrentRows, "a", string.Empty, "b", string.Empty);
    }

    private static void RunDisplayParserMainViewerWrapsParsedLinesIndependently(string tempRoot)
    {
        string first = new('x', VisualRowReader.VisibleSegmentChars + 1);
        string path = WriteLog(tempRoot, "display-parser-main-newline-wrap.log", "{\"First\":\"" + first + "\",\"Second\":\"tail\"}\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{First}\n{Second}"));

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0, parser);
        reader.ReadFromPercentage(0d, 4);

        AssertSequence(
            "display parser main newline wrap",
            reader.CurrentRows,
            new string('x', VisualRowReader.VisibleSegmentChars),
            "x",
            "tail");
    }

    private static void RunDisplayParserMainViewerSelectionPreservesParsedNewlines(string tempRoot)
    {
        string path = WriteLog(tempRoot, "display-parser-main-newline-selection.log", "{\"First\":\"a\",\"Second\":\"b\"}\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{First}\r\n{Second}"));

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0, parser);
        reader.ReadFromPercentage(0d, 3);
        ISelectableViewportReader selectable = reader;
        ViewportRowSelectionKey secondVisualLine = selectable.CurrentRowSelectionKeys[1];

        IReadOnlyList<ViewportSelectedRow> rows = selectable.ReadSelectedRows(
            selectAll: false,
            new[] { new ViewportRowSelectionRange(secondVisualLine, secondVisualLine) },
            Array.Empty<ViewportRowSelectionKey>());

        AssertSelectedRows("display parser main newline selection", rows, "a\r\nb");
    }

    private static void RunSearchFindsTextOnSecondParsedLine(string tempRoot)
    {
        string path = WriteLog(
            tempRoot,
            "display-parser-search-second-line.log",
            "{\"Level\":\"Info\",\"Message\":\"ready\"}\r\n{\"Level\":\"Error\",\"Message\":\"failed\"}\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{upper:Level}\n{Message}"));

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("failed", UseRegex: false, IgnoreCase: false),
            parser);

        reader.ReadFromPercentage(0d, 3);
        IColumnViewportReader columns = reader;
        AssertSequence("display parser search second line rows", reader.CurrentRows, "ERROR", "failed");
        AssertSequence("display parser search second line first cells", columns.CurrentCells[0], "2", "ERROR");
        AssertSequence("display parser search second line second cells", columns.CurrentCells[1], "2", "failed");

        ISelectableViewportReader selectable = reader;
        AssertEqual(
            "display parser search second line ordinal",
            ((IRowOrdinalViewportReader)reader).TryGetRowOrdinal(selectable.CurrentRowSelectionKeys[1], out long rowOrdinal),
            true);
        AssertEqual("display parser search second line ordinal value", rowOrdinal, 1L);

        IReadOnlyList<ViewportSelectedRow> selectedRows = selectable.ReadSelectedRows(
            selectAll: true,
            Array.Empty<ViewportRowSelectionRange>(),
            Array.Empty<ViewportRowSelectionKey>());
        AssertSelectedRows("display parser search second line selection", selectedRows, "ERROR", "failed");
    }

    private static void RunSearchRepeatsCapturesAcrossParsedLines(string tempRoot)
    {
        string path = WriteLog(tempRoot, "display-parser-search-multiline-captures.log", "{\"Level\":\"Error\",\"Message\":\"failed\"}\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{upper:Level}\n{Message}"));

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(?<level>ERROR)\\n(?<message>failed)", UseRegex: true, IgnoreCase: false),
            parser);

        reader.ReadFromPercentage(0d, 3);
        IColumnViewportReader columns = reader;
        AssertSequence("display parser multiline capture headers", columns.ColumnHeaders, "#", "Text", "level", "message");
        AssertSequence("display parser multiline capture first cells", columns.CurrentCells[0], "1", "ERROR", "ERROR", "failed");
        AssertSequence("display parser multiline capture second cells", columns.CurrentCells[1], "1", "failed", "ERROR", "failed");
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

    private static void RunNonBacktrackingRegexRejectsUnsupportedPattern()
    {
        try
        {
            LogSearchBuilder.ValidateOptions(new SearchOptions("(?<word>[a-z]+)\\s+\\k<word>", UseRegex: true, IgnoreCase: false));
        }
        catch (ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException("Unsupported non-backtracking regex did not fail validation.");
    }

    private static void RunNonBacktrackingRegexLeadingDotStarNoMatchOnLongLine(string tempRoot)
    {
        string path = WriteLog(tempRoot, "nonbacktracking-leading-dotstar-no-match.log", new string('a', VisualRowReader.VisibleSegmentChars * 12) + "\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(.*ERROR)", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        AssertEqual("nonbacktracking leading dotstar no-match count", reader.MatchedLineCount, 0L);
    }

    private static void RunNonBacktrackingRegexLeadingDotStarMatchesLongLine(string tempRoot)
    {
        string line = new string('a', VisualRowReader.VisibleSegmentChars * 6) + "ERROR" + new string('b', VisualRowReader.VisibleSegmentChars * 6);
        string path = WriteLog(tempRoot, "nonbacktracking-leading-dotstar-match.log", line + "\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(.*ERROR)", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        AssertEqual("nonbacktracking leading dotstar match count", reader.MatchedLineCount, 1L);
    }

    private static void RunCascadedLiteralSearchFiltersPrevious(string tempRoot)
    {
        string path = WriteLog(tempRoot, "cascade-literal.log", "alpha\r\nalpha beta\r\nbeta\r\nALPHA beta\r\n");
        SearchOptions[] options =
        {
            new("alpha", UseRegex: false, IgnoreCase: false),
            new("beta", UseRegex: false, IgnoreCase: false)
        };

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(path, Encoding.UTF8, dataOffset: 0, options);
        reader.ReadFromPercentage(0d, 10);

        AssertEqual("cascade literal count", reader.MatchedLineCount, 1L);
        AssertSequence("cascade literal rows", reader.CurrentRows, "alpha beta");
        AssertSequence("cascade literal cells", ((IColumnViewportReader)reader).CurrentCells[0], "2", "alpha beta");
    }

    private static void RunChangedCascadedSearchFiltersPreviousReader(string tempRoot)
    {
        string path = WriteLog(tempRoot, "changed-cascade.log", "ERROR user=ana\r\nERROR user=bob\r\nINFO user=bob\r\n");
        SearchOptions[] initialOptions =
        {
            new("ERROR", UseRegex: false, IgnoreCase: false),
            new("user=ana", UseRegex: false, IgnoreCase: false)
        };
        SearchOptions[] changedOptions =
        {
            new("ERROR", UseRegex: false, IgnoreCase: false),
            new("user=bob", UseRegex: false, IgnoreCase: false)
        };

        FilteredVisualRowReader[] initialReaders = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, initialOptions);
        try
        {
            StagedSearchProgressUpdate finalUpdate = default;
            LogSearchBuilder.BuildChangedStagedFilteredReadersIncremental(
                initialReaders,
                changedStageIndex: 1,
                changedOptions,
                new[] { 10, 10 },
                update => finalUpdate = update,
                CancellationToken.None);

            AssertEqual("changed cascade prefix reader unchanged", finalUpdate.Readers[0] is null, true);
            AssertEqual("changed cascade first stage count", finalUpdate.MatchedLineCounts[0], 2L);
            AssertEqual("changed cascade second stage count", finalUpdate.MatchedLineCounts[1], 1L);
            using FilteredVisualRowReader changedReader = finalUpdate.Readers[1] ?? throw new InvalidOperationException("changed cascade reader missing.");
            IReadOnlyList<string> rows = changedReader.ReadFromPercentage(0d, 10);

            AssertSequence("changed cascade rows", rows, "ERROR user=bob");
        }
        finally
        {
            foreach (FilteredVisualRowReader reader in initialReaders)
            {
                reader.Dispose();
            }
        }
    }

    private static void RunCascadedInvertMatch(string tempRoot)
    {
        string path = WriteLog(tempRoot, "cascade-invert.log", "alpha keep\r\nalpha drop\r\nplain keep\r\n");
        SearchOptions[] options =
        {
            new("alpha", UseRegex: false, IgnoreCase: false),
            new("drop", UseRegex: false, IgnoreCase: false, InvertMatch: true)
        };

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(path, Encoding.UTF8, dataOffset: 0, options);
        reader.ReadFromPercentage(0d, 10);

        AssertEqual("cascade invert count", reader.MatchedLineCount, 1L);
        AssertSequence("cascade invert rows", reader.CurrentRows, "alpha keep");
        AssertSequence("cascade invert cells", ((IColumnViewportReader)reader).CurrentCells[0], "1", "alpha keep");
    }

    private static void RunCascadedInvalidRegexValidation()
    {
        SearchOptions[] options =
        {
            new("alpha", UseRegex: false, IgnoreCase: false),
            new("[", UseRegex: true, IgnoreCase: false)
        };

        try
        {
            LogSearchBuilder.ValidateOptions(options);
        }
        catch (ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException("Invalid cascaded regex did not fail validation.");
    }

    private static void RunCascadedCaptureGroupsUseLastCapturingRegex(string tempRoot)
    {
        string path = WriteLog(tempRoot, "cascade-captures.log", "aaabccc code-42\r\naabcc code-7\r\nplain code-9\r\n");
        SearchOptions[] options =
        {
            new("(a+)b(c+)", UseRegex: true, IgnoreCase: false),
            new("code-(\\d+)", UseRegex: true, IgnoreCase: false)
        };

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(path, Encoding.UTF8, dataOffset: 0, options);
        reader.ReadFromPercentage(0d, 10);
        IColumnViewportReader columns = reader;

        AssertSequence("cascade capture headers", columns.ColumnHeaders, "#", "Text", "0");
        AssertEqual("cascade capture row count", columns.CurrentCells.Count, 2);
        AssertSequence("cascade capture first cells", columns.CurrentCells[0], "1", "aaabccc code-42", "42");
        AssertSequence("cascade capture second cells", columns.CurrentCells[1], "2", "aabcc code-7", "7");
    }

    private static void RunCascadedNamedCaptureGroupsSurviveNonCapturingStage(string tempRoot)
    {
        string path = WriteLog(tempRoot, "cascade-named-captures.log", "code-42 GET /api/orders\r\ncode-7 POST /api/users\r\nplain GET /api/orders\r\n");
        SearchOptions[] options =
        {
            new("code-(?<code>\\d+)", UseRegex: true, IgnoreCase: false),
            new("(?:GET|POST) /api/\\w+", UseRegex: true, IgnoreCase: false)
        };

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(path, Encoding.UTF8, dataOffset: 0, options);
        reader.ReadFromPercentage(0d, 10);
        IColumnViewportReader columns = reader;

        AssertSequence("cascade named capture headers", columns.ColumnHeaders, "#", "Text", "code");
        AssertSequence("cascade named capture first cells", columns.CurrentCells[0], "1", "code-42 GET /api/orders", "42");
        AssertSequence("cascade named capture second cells", columns.CurrentCells[1], "2", "code-7 POST /api/users", "7");
    }

    private static void RunStagedLiteralSearchKeepsIntermediateResults(string tempRoot)
    {
        string path = WriteLog(tempRoot, "staged-literal.log", "alpha\r\nalpha beta\r\nbeta\r\nalpha beta gamma\r\n");
        SearchOptions[] options =
        {
            new("alpha", UseRegex: false, IgnoreCase: false),
            new("beta", UseRegex: false, IgnoreCase: false)
        };

        FilteredVisualRowReader[] readers = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, options);
        try
        {
            AssertEqual("staged literal reader count", readers.Length, 2);
            readers[0].ReadFromPercentage(0d, 10);
            readers[1].ReadFromPercentage(0d, 10);

            AssertEqual("staged literal first count", readers[0].MatchedLineCount, 3L);
            AssertSequence("staged literal first rows", readers[0].CurrentRows, "alpha", "alpha beta", "alpha beta gamma");
            AssertSequence("staged literal first cells", ((IColumnViewportReader)readers[0]).CurrentCells[1], "2", "alpha beta");

            AssertEqual("staged literal second count", readers[1].MatchedLineCount, 2L);
            AssertSequence("staged literal second rows", readers[1].CurrentRows, "alpha beta", "alpha beta gamma");
            AssertSequence("staged literal second cells", ((IColumnViewportReader)readers[1]).CurrentCells[0], "2", "alpha beta");
        }
        finally
        {
            DisposeReaders(readers);
        }
    }

    private static void RunStagedCaptureGroupsUsePerStageCaptures(string tempRoot)
    {
        string path = WriteLog(tempRoot, "staged-captures.log", "aaabccc code-42\r\naabcc code-7\r\nplain code-9\r\n");
        SearchOptions[] options =
        {
            new("(a+)b(c+)", UseRegex: true, IgnoreCase: false),
            new("code-(\\d+)", UseRegex: true, IgnoreCase: false)
        };

        FilteredVisualRowReader[] readers = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, options);
        try
        {
            AssertEqual("staged capture reader count", readers.Length, 2);
            readers[0].ReadFromPercentage(0d, 10);
            readers[1].ReadFromPercentage(0d, 10);

            IColumnViewportReader firstColumns = readers[0];
            AssertSequence("staged capture first headers", firstColumns.ColumnHeaders, "#", "Text", "0", "1");
            AssertSequence("staged capture first cells", firstColumns.CurrentCells[0], "1", "aaabccc code-42", "aaa", "ccc");

            IColumnViewportReader secondColumns = readers[1];
            AssertSequence("staged capture second headers", secondColumns.ColumnHeaders, "#", "Text", "0");
            AssertSequence("staged capture second first cells", secondColumns.CurrentCells[0], "1", "aaabccc code-42", "42");
            AssertSequence("staged capture second second cells", secondColumns.CurrentCells[1], "2", "aabcc code-7", "7");
        }
        finally
        {
            DisposeReaders(readers);
        }
    }

    private static void RunStagedNamedCaptureGroupsUsePerStageHeaders(string tempRoot)
    {
        string path = WriteLog(tempRoot, "staged-named-captures.log", "code-42 user-ian\r\ncode-7 user-ana\r\nplain user-bob\r\n");
        SearchOptions[] options =
        {
            new("code-(?<code>\\d+)", UseRegex: true, IgnoreCase: false),
            new("user-(?<user>[a-z]+)", UseRegex: true, IgnoreCase: false)
        };

        FilteredVisualRowReader[] readers = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, options);
        try
        {
            AssertEqual("staged named capture reader count", readers.Length, 2);
            readers[0].ReadFromPercentage(0d, 10);
            readers[1].ReadFromPercentage(0d, 10);

            IColumnViewportReader firstColumns = readers[0];
            AssertSequence("staged named capture first headers", firstColumns.ColumnHeaders, "#", "Text", "code");
            AssertSequence("staged named capture first cells", firstColumns.CurrentCells[0], "1", "code-42 user-ian", "42");
            AssertSequence("staged named capture first second cells", firstColumns.CurrentCells[1], "2", "code-7 user-ana", "7");

            IColumnViewportReader secondColumns = readers[1];
            AssertSequence("staged named capture second headers", secondColumns.ColumnHeaders, "#", "Text", "user");
            AssertSequence("staged named capture second first cells", secondColumns.CurrentCells[0], "1", "code-42 user-ian", "ian");
            AssertSequence("staged named capture second second cells", secondColumns.CurrentCells[1], "2", "code-7 user-ana", "ana");
        }
        finally
        {
            DisposeReaders(readers);
        }
    }

    private static void RunAppendStagedSearchKeepsIntermediateResults(string tempRoot)
    {
        string path = WriteLog(tempRoot, "append-staged-search.log", "alpha beta\r\nalpha plain\r\n");
        SearchOptions[] options =
        {
            new("alpha", UseRegex: false, IgnoreCase: false),
            new("beta", UseRegex: false, IgnoreCase: false)
        };

        FilteredVisualRowReader[] initial = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, options);
        try
        {
            File.AppendAllText(path, "new alpha\r\nnew alpha beta\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            FilteredVisualRowReader[] appended = BuildAppendedStagedReaders(initial, options);
            try
            {
                appended[0].ReadFromPercentage(0d, 10);
                appended[1].ReadFromPercentage(0d, 10);

                AssertEqual("append staged first count", appended[0].MatchedLineCount, 4L);
                AssertSequence("append staged first rows", appended[0].CurrentRows, "alpha beta", "alpha plain", "new alpha", "new alpha beta");
                AssertSequence("append staged first final cells", ((IColumnViewportReader)appended[0]).CurrentCells[3], "4", "new alpha beta");

                AssertEqual("append staged second count", appended[1].MatchedLineCount, 2L);
                AssertSequence("append staged second rows", appended[1].CurrentRows, "alpha beta", "new alpha beta");
                AssertSequence("append staged second final cells", ((IColumnViewportReader)appended[1]).CurrentCells[1], "4", "new alpha beta");
            }
            finally
            {
                DisposeReaders(appended);
            }
        }
        finally
        {
            DisposeReaders(initial);
        }
    }

    private static void RunAppendSearchCascadeAddsMatches(string tempRoot)
    {
        string path = WriteLog(tempRoot, "append-search-cascade.log", "alpha beta\r\nalpha plain\r\n");
        SearchOptions[] options =
        {
            new("alpha", UseRegex: false, IgnoreCase: false),
            new("beta", UseRegex: false, IgnoreCase: false)
        };

        using FilteredVisualRowReader initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            options);

        File.AppendAllText(path, "new alpha\r\nnew alpha beta\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using FilteredVisualRowReader appended = BuildAppendedReader(initial, options);
        appended.ReadFromPercentage(0d, 10);

        AssertEqual("append cascade count", appended.MatchedLineCount, 2L);
        AssertSequence("append cascade rows", appended.CurrentRows, "alpha beta", "new alpha beta");
        IColumnViewportReader columns = appended;
        AssertSequence("append cascade first cells", columns.CurrentCells[0], "1", "alpha beta");
        AssertSequence("append cascade second cells", columns.CurrentCells[1], "4", "new alpha beta");
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
        IColumnViewportReader columns = appended;
        AssertSequence("append match first cells", columns.CurrentCells[0], "1", "alpha");
        AssertSequence("append match second cells", columns.CurrentCells[1], "3", "new alpha");
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
        AssertSequence("append partial cells", ((IColumnViewportReader)appended).CurrentCells[0], "1", "prefix alpha");
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

        AssertSequence("append capture headers", columns.ColumnHeaders, "#", "Text", "0", "1");
        AssertEqual("append capture row count", columns.CurrentCells.Count, 2);
        AssertEqual("append capture line number", columns.CurrentCells[1][0], "3");
        AssertEqual("append capture text", columns.CurrentCells[1][1], "aaabccc");
        AssertEqual("append capture group 0", columns.CurrentCells[1][2], "aaa");
        AssertEqual("append capture group 1", columns.CurrentCells[1][3], "ccc");
    }

    private static void RunAppendSearchPreservesNamedCaptureGroups(string tempRoot)
    {
        string path = WriteLog(tempRoot, "append-search-named-captures.log", "code-42\r\nplain\r\n");
        SearchOptions options = new("code-(?<code>\\d+)", UseRegex: true, IgnoreCase: false);

        using FilteredVisualRowReader initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            options);

        File.AppendAllText(path, "code-7\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using FilteredVisualRowReader appended = BuildAppendedReader(initial, options);
        appended.ReadFromPercentage(0d, 10);
        IColumnViewportReader columns = appended;

        AssertSequence("append named capture headers", columns.ColumnHeaders, "#", "Text", "code");
        AssertEqual("append named capture row count", columns.CurrentCells.Count, 2);
        AssertSequence("append named capture first cells", columns.CurrentCells[0], "1", "code-42", "42");
        AssertSequence("append named capture second cells", columns.CurrentCells[1], "3", "code-7", "7");
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
        IColumnViewportReader columns = appended;
        AssertSequence("append invert first cells", columns.CurrentCells[0], "2", "plain");
        AssertSequence("append invert second cells", columns.CurrentCells[1], "3", "beta");
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

    private static void RunPausedStagedSearchPublishesCheckpointWithoutMatches(string tempRoot)
    {
        string path = WriteLog(tempRoot, "paused-search-no-matches.log", "plain-0\r\nplain-1\r\n");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        List<StagedSearchProgressUpdate> updates = new();

        AssertThrows<OperationCanceledException>(
            "paused staged search cancellation",
            () => LogSearchBuilder.BuildStagedFilteredReadersIncremental(
                path,
                Encoding.UTF8,
                dataOffset: 0,
                new[] { new SearchOptions("alpha", UseRegex: false, IgnoreCase: false) },
                new[] { 10 },
                update => updates.Add(update),
                cancellation.Token));

        StagedSearchProgressUpdate paused = FindPausedUpdate(updates);
        AssertEqual("paused no-match is paused", paused.IsPaused, true);
        AssertEqual("paused no-match processed offset", paused.ProcessedOffset, 0L);
        AssertEqual("paused no-match target size", paused.TargetFileSize, new FileInfo(path).Length);
        AssertEqual("paused no-match reader exists", paused.Readers[0] is not null, true);
        AssertEqual("paused no-match reader confirmed size", paused.Readers[0]!.ConfirmedFileSize, 0L);
        DisposeNullableReaders(paused.Readers);
    }

    private static void RunPausedAppendStagedSearchPublishesCheckpointWhenAlreadyCancelled(string tempRoot)
    {
        string path = WriteLog(tempRoot, "paused-append-search-pre-cancel.log", "alpha-0\r\nplain-1\r\n");
        SearchOptions[] options = { new("alpha", UseRegex: false, IgnoreCase: false) };
        FilteredVisualRowReader[] initial = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, options);
        try
        {
            File.AppendAllText(path, "alpha-2\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            long newFileSize = new FileInfo(path).Length;
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            List<StagedSearchProgressUpdate> updates = new();

            AssertThrows<OperationCanceledException>(
                "paused append staged search pre-cancel",
                () => LogSearchBuilder.BuildAppendedStagedFilteredReadersIncremental(
                    initial,
                    options,
                    newFileSize,
                    new[] { 10 },
                    update => updates.Add(update),
                    cancellation.Token));

            StagedSearchProgressUpdate paused = FindPausedUpdate(updates);
            AssertEqual("paused append pre-cancel is paused", paused.IsPaused, true);
            AssertEqual("paused append pre-cancel target size", paused.TargetFileSize, newFileSize);
            AssertEqual("paused append pre-cancel progress global lower bound", paused.ProgressPercentage > 0d, true);
            AssertEqual("paused append pre-cancel progress global upper bound", paused.ProgressPercentage < 100d, true);
            AssertEqual("paused append pre-cancel reader exists", paused.Readers[0] is not null, true);
            AssertEqual("paused append pre-cancel reader confirmed size", paused.Readers[0]!.ConfirmedFileSize, paused.ProcessedOffset);
            DisposeNullableReaders(paused.Readers);
        }
        finally
        {
            DisposeReaders(initial);
        }
    }

    private static void RunPausedResumeStagedSearchPublishesCheckpointWhenAlreadyCancelled(string tempRoot)
    {
        string path = WriteLog(tempRoot, "paused-resume-search-pre-cancel.log", "alpha-0\r\nplain-1\r\n");
        SearchOptions[] options = { new("alpha", UseRegex: false, IgnoreCase: false) };
        FilteredVisualRowReader[] pausedReaders = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, options);
        try
        {
            long processedOffset = new FileInfo(path).Length;
            File.AppendAllText(path, "alpha-2\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            long newFileSize = new FileInfo(path).Length;
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            List<StagedSearchProgressUpdate> updates = new();

            AssertThrows<OperationCanceledException>(
                "paused resume staged search pre-cancel",
                () => LogSearchBuilder.ResumeStagedFilteredReadersIncremental(
                    pausedReaders,
                    options,
                    processedOffset,
                    newFileSize,
                    new[] { 10 },
                    update => updates.Add(update),
                    cancellation.Token));

            StagedSearchProgressUpdate paused = FindPausedUpdate(updates);
            AssertEqual("paused resume pre-cancel is paused", paused.IsPaused, true);
            AssertEqual("paused resume pre-cancel target size", paused.TargetFileSize, newFileSize);
            AssertEqual("paused resume pre-cancel reader exists", paused.Readers[0] is not null, true);
            AssertEqual("paused resume pre-cancel reader confirmed size", paused.Readers[0]!.ConfirmedFileSize, paused.ProcessedOffset);
            DisposeNullableReaders(paused.Readers);
        }
        finally
        {
            DisposeReaders(pausedReaders);
        }
    }

    private static void RunPausedStagedSearchPublishesCheckpointAfterCallbackCancellation(string tempRoot)
    {
        string path = WriteLog(tempRoot, "paused-search-callback-cancel.log", "alpha-0\r\nplain-1\r\n");
        SearchOptions[] options = { new("alpha", UseRegex: false, IgnoreCase: false) };
        using CancellationTokenSource cancellation = new();
        List<StagedSearchProgressUpdate> updates = new();

        AssertThrows<OperationCanceledException>(
            "paused staged search callback cancellation",
            () => LogSearchBuilder.BuildStagedFilteredReadersIncremental(
                path,
                Encoding.UTF8,
                dataOffset: 0,
                options,
                new[] { 10 },
                update =>
                {
                    if (!update.IsPaused && update.MatchedLineCounts.Length > 0 && update.MatchedLineCounts[0] > 0)
                    {
                        cancellation.Cancel();
                    }

                    if (!update.IsPaused && cancellation.IsCancellationRequested)
                    {
                        DisposeNullableReaders(update.Readers);
                        cancellation.Token.ThrowIfCancellationRequested();
                    }

                    updates.Add(update);
                },
                cancellation.Token));

        StagedSearchProgressUpdate paused = FindPausedUpdate(updates);
        AssertEqual("paused callback cancel is paused", paused.IsPaused, true);
        AssertEqual("paused callback cancel reader exists", paused.Readers[0] is not null, true);
        AssertEqual("paused callback cancel processed positive", paused.ProcessedOffset > 0, true);
        DisposeNullableReaders(paused.Readers);
    }

    private static void RunPausedStagedSearchResumesFromProcessedOffset(string tempRoot)
    {
        string original = "alpha-0\r\nplain-1\r\nalpha-2\r\nalpha-3\r\n";
        string modifiedPrefix = "bravo-0\r\nplain-1\r\nalpha-2\r\nalpha-3\r\n";
        string path = WriteLog(tempRoot, "paused-search-resume.log", original);
        SearchOptions[] options = { new("alpha", UseRegex: false, IgnoreCase: false) };
        using CancellationTokenSource cancellation = new();
        List<StagedSearchProgressUpdate> updates = new();

        AssertThrows<OperationCanceledException>(
            "paused staged search resume cancellation",
            () => LogSearchBuilder.BuildStagedFilteredReadersIncremental(
                path,
                Encoding.UTF8,
                dataOffset: 0,
                options,
                new[] { 10 },
                update =>
                {
                    updates.Add(update);
                    if (!update.IsPaused && !update.IsFinal && update.MatchedLineCounts.Length > 0 && update.MatchedLineCounts[0] > 0)
                    {
                        cancellation.Cancel();
                    }
                },
                cancellation.Token));

        StagedSearchProgressUpdate paused = FindPausedUpdate(updates);
        AssertEqual("paused resume has reader", paused.Readers[0] is not null, true);
        AssertEqual("paused resume processed positive", paused.ProcessedOffset > 0, true);
        AssertEqual("paused resume partial confirmed size", paused.Readers[0]!.ConfirmedFileSize, paused.ProcessedOffset);
        File.WriteAllText(path, modifiedPrefix, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        FilteredVisualRowReader? resumed = null;
        try
        {
            LogSearchBuilder.ResumeStagedFilteredReadersIncremental(
                new[] { paused.Readers[0]! },
                options,
                paused.ProcessedOffset,
                new FileInfo(path).Length,
                new[] { 10 },
                update =>
                {
                    if (update.Readers.Length > 0 && update.Readers[0] is not null)
                    {
                        resumed?.Dispose();
                        resumed = update.Readers[0];
                        update.Readers[0] = null;
                    }
                },
                CancellationToken.None);

            if (resumed is null)
            {
                throw new InvalidOperationException("Resume did not publish a reader.");
            }

            resumed.ReadFromPercentage(0d, 10);
            AssertEqual("paused resume keeps processed descriptor count", resumed.MatchedLineCount, 3L);
            AssertSequence("paused resume rows", resumed.CurrentRows, "bravo-0", "alpha-2", "alpha-3");
        }
        finally
        {
            resumed?.Dispose();
            DisposeNullableReaders(paused.Readers);
        }
    }

    private static void RunPartialStagedSearchReaderResumesFromProcessedOffset(string tempRoot)
    {
        string original = "alpha-0\r\nplain-1\r\nalpha-2\r\n";
        string path = WriteLog(tempRoot, "partial-search-resume.log", original);
        long targetFileSize = new FileInfo(path).Length;
        SearchOptions[] options = { new("alpha", UseRegex: false, IgnoreCase: false) };
        FilteredVisualRowReader? partialReader = null;
        long partialProcessedOffset = 0;
        long partialTargetFileSize = 0;

        LogSearchBuilder.BuildStagedFilteredReadersIncremental(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            options,
            new[] { 10 },
            update =>
            {
                if (partialReader is null &&
                    !update.IsFinal &&
                    !update.IsPaused &&
                    update.Readers.Length > 0 &&
                    update.Readers[0] is FilteredVisualRowReader reader)
                {
                    partialReader = reader;
                    partialProcessedOffset = update.ProcessedOffset;
                    partialTargetFileSize = update.TargetFileSize;
                    update.Readers[0] = null;
                }

                DisposeNullableReaders(update.Readers);
            },
            CancellationToken.None);

        if (partialReader is null)
        {
            throw new InvalidOperationException("Partial search did not publish a resumable reader.");
        }

        FilteredVisualRowReader? resumed = null;
        try
        {
            AssertEqual("partial resume target file size", partialTargetFileSize, targetFileSize);
            AssertEqual("partial resume reader confirmed processed", partialReader.ConfirmedFileSize, partialProcessedOffset);
            AssertEqual("partial resume processed before target", partialProcessedOffset < partialTargetFileSize, true);

            LogSearchBuilder.ResumeStagedFilteredReadersIncremental(
                new[] { partialReader },
                options,
                partialProcessedOffset,
                targetFileSize,
                new[] { 10 },
                update =>
                {
                    if (update.Readers.Length > 0 && update.Readers[0] is not null)
                    {
                        resumed?.Dispose();
                        resumed = update.Readers[0];
                        update.Readers[0] = null;
                    }

                    DisposeNullableReaders(update.Readers);
                },
                CancellationToken.None);

            if (resumed is null)
            {
                throw new InvalidOperationException("Resume from partial search did not publish a reader.");
            }

            resumed.ReadFromPercentage(0d, 10);
            AssertEqual("partial resume count", resumed.MatchedLineCount, 2L);
            AssertSequence("partial resume rows", resumed.CurrentRows, "alpha-0", "alpha-2");
        }
        finally
        {
            resumed?.Dispose();
            partialReader.Dispose();
        }
    }

    private static void RunResumeStagedSearchReprocessesIncompleteLastLine(string tempRoot)
    {
        string original = "alpha-0\r\nalpha-last";
        string replacement = "alpha-0\r\nbravo-last-tail\r\nalpha-new\r\n";
        string path = WriteLog(tempRoot, "resume-incomplete-last-line.log", original);
        SearchOptions[] options = { new("alpha", UseRegex: false, IgnoreCase: false) };
        FilteredVisualRowReader[] paused = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, options);
        try
        {
            long processedOffset = new FileInfo(path).Length;
            File.WriteAllText(path, replacement, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            FilteredVisualRowReader[] resumed = ResumeStagedReaders(paused, options, processedOffset, new FileInfo(path).Length);
            try
            {
                resumed[0].ReadFromPercentage(0d, 10);
                AssertEqual("resume incomplete count", resumed[0].MatchedLineCount, 2L);
                AssertSequence("resume incomplete rows", resumed[0].CurrentRows, "alpha-0", "alpha-new");
                IColumnViewportReader columns = resumed[0];
                AssertSequence("resume incomplete first cells", columns.CurrentCells[0], "1", "alpha-0");
                AssertSequence("resume incomplete second cells", columns.CurrentCells[1], "3", "alpha-new");
            }
            finally
            {
                DisposeReaders(resumed);
            }
        }
        finally
        {
            DisposeReaders(paused);
        }
    }

    private static void RunResumeStagedSearchContinuesAfterLineBreak(string tempRoot)
    {
        string original = "alpha-0\r\nalpha-1\r\n";
        string path = WriteLog(tempRoot, "resume-after-line-break.log", original);
        SearchOptions[] options = { new("alpha", UseRegex: false, IgnoreCase: false) };
        FilteredVisualRowReader[] paused = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, options);
        try
        {
            long processedOffset = new FileInfo(path).Length;
            File.AppendAllText(path, "alpha-2\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            FilteredVisualRowReader[] resumed = ResumeStagedReaders(paused, options, processedOffset, new FileInfo(path).Length);
            try
            {
                resumed[0].ReadFromPercentage(0d, 10);
                AssertEqual("resume line-break count", resumed[0].MatchedLineCount, 3L);
                AssertSequence("resume line-break rows", resumed[0].CurrentRows, "alpha-0", "alpha-1", "alpha-2");
                IColumnViewportReader columns = resumed[0];
                AssertSequence("resume line-break first cells", columns.CurrentCells[0], "1", "alpha-0");
                AssertSequence("resume line-break second cells", columns.CurrentCells[1], "2", "alpha-1");
                AssertSequence("resume line-break third cells", columns.CurrentCells[2], "3", "alpha-2");
            }
            finally
            {
                DisposeReaders(resumed);
            }
        }
        finally
        {
            DisposeReaders(paused);
        }
    }

    private static void RunResumeStagedSearchPreservesCascadeReaders(string tempRoot)
    {
        string original = "alpha beta\r\nalpha beta-last";
        string replacement = "alpha beta\r\nalpha plain-tail\r\nalpha beta-new\r\n";
        string path = WriteLog(tempRoot, "resume-cascade-incomplete.log", original);
        SearchOptions[] options =
        {
            new("alpha", UseRegex: false, IgnoreCase: false),
            new("beta", UseRegex: false, IgnoreCase: false)
        };
        FilteredVisualRowReader[] paused = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, options);
        try
        {
            long processedOffset = new FileInfo(path).Length;
            File.WriteAllText(path, replacement, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            FilteredVisualRowReader[] resumed = ResumeStagedReaders(paused, options, processedOffset, new FileInfo(path).Length);
            try
            {
                resumed[0].ReadFromPercentage(0d, 10);
                resumed[1].ReadFromPercentage(0d, 10);
                AssertEqual("resume cascade first count", resumed[0].MatchedLineCount, 3L);
                AssertSequence("resume cascade first rows", resumed[0].CurrentRows, "alpha beta", "alpha plain-tail", "alpha beta-new");
                AssertEqual("resume cascade second count", resumed[1].MatchedLineCount, 2L);
                AssertSequence("resume cascade second rows", resumed[1].CurrentRows, "alpha beta", "alpha beta-new");
            }
            finally
            {
                DisposeReaders(resumed);
            }
        }
        finally
        {
            DisposeReaders(paused);
        }
    }

    private static FilteredVisualRowReader BuildAppendedReader(FilteredVisualRowReader initial, SearchOptions options)
    {
        return BuildAppendedReader(initial, new[] { options });
    }

    private static FilteredVisualRowReader BuildAppendedReader(FilteredVisualRowReader initial, SearchOptions options, DisplayParserRule displayParserRule)
    {
        return BuildAppendedReader(initial, new[] { options }, displayParserRule);
    }

    private static FilteredVisualRowReader BuildAppendedReader(FilteredVisualRowReader initial, IReadOnlyList<SearchOptions> options, DisplayParserRule? displayParserRule = null)
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
            CancellationToken.None,
            displayParserRule);

        return latest ?? throw new InvalidOperationException("Append search did not publish a reader.");
    }

    private static FilteredVisualRowReader[] BuildAppendedStagedReaders(IReadOnlyList<FilteredVisualRowReader> initial, IReadOnlyList<SearchOptions> options)
    {
        FilteredVisualRowReader?[] latest = new FilteredVisualRowReader?[initial.Count];
        LogSearchBuilder.BuildAppendedStagedFilteredReadersIncremental(
            initial,
            options,
            new FileInfo(initial[0].FilePath).Length,
            CreateFilledArray(initial.Count, 10),
            update =>
            {
                for (int i = 0; i < update.Readers.Length; i++)
                {
                    if (update.Readers[i] is not null)
                    {
                        latest[i]?.Dispose();
                        latest[i] = update.Readers[i];
                    }
                }
            },
            CancellationToken.None);

        FilteredVisualRowReader[] readers = new FilteredVisualRowReader[latest.Length];
        for (int i = 0; i < latest.Length; i++)
        {
            readers[i] = latest[i] ?? throw new InvalidOperationException("Append staged search did not publish every reader.");
        }

        return readers;
    }

    private static FilteredVisualRowReader[] ResumeStagedReaders(IReadOnlyList<FilteredVisualRowReader> paused, IReadOnlyList<SearchOptions> options, long processedOffset, long newFileSize)
    {
        FilteredVisualRowReader?[] latest = new FilteredVisualRowReader?[paused.Count];
        LogSearchBuilder.ResumeStagedFilteredReadersIncremental(
            paused,
            options,
            processedOffset,
            newFileSize,
            CreateFilledArray(paused.Count, 10),
            update =>
            {
                for (int i = 0; i < update.Readers.Length; i++)
                {
                    if (update.Readers[i] is not null)
                    {
                        latest[i]?.Dispose();
                        latest[i] = update.Readers[i];
                        update.Readers[i] = null;
                    }
                }
            },
            CancellationToken.None);

        FilteredVisualRowReader[] readers = new FilteredVisualRowReader[latest.Length];
        for (int i = 0; i < latest.Length; i++)
        {
            readers[i] = latest[i] ?? throw new InvalidOperationException("Resume staged search did not publish every reader.");
        }

        return readers;
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

    private static void RunRefreshTailAtEndReloadsSameSizeChange(string tempRoot)
    {
        string path = WriteLog(tempRoot, "tail-same-size-change.log", "line-0\r\nline-1\r\nline-2\r\n");

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(100d, 2);
        File.WriteAllText(path, "line-0\r\nline-1\r\nLINE-2\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshTail(2);

        AssertSequence("tail same-size change rows", reader.CurrentRows, "line-1", "LINE-2");
    }

    private static void RunReloadAfterFileChangeSameSizeReloadsCurrentViewport(string tempRoot)
    {
        string path = WriteLog(tempRoot, "reload-same-size-current.log", "line-0\r\nline-1\r\nline-2\r\n");

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(0d, 2);
        File.WriteAllText(path, "LINE-0\r\nline-1\r\nline-2\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(2);

        AssertSequence("reload same-size current rows", reader.CurrentRows, "LINE-0", "line-1");
    }

    private static void RunReloadAfterFileChangePreservesViewportPosition(string tempRoot)
    {
        string path = WriteLog(tempRoot, "reload-preserve-position.log", "line-0\r\nline-1\r\nline-2\r\nline-3\r\nline-4\r\n");

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromOffset(16L, 2);
        File.WriteAllText(path, "LINE-0\r\nline-1\r\nline-2\r\nLINE-3\r\nline-4\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(2);

        AssertEqual("reload preserve position top offset", reader.TopOffset, 16L);
        AssertSequence("reload preserve position rows", reader.CurrentRows, "line-2", "LINE-3");
    }

    private static void RunFilteredReloadAfterFileChangeSameSizeReloadsCurrentViewport(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-reload-same-size.log", "alpha\r\nplain\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));
        reader.ReadFromPercentage(0d, 10);

        File.WriteAllText(path, "bravo\r\nplain\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(10);

        AssertSequence("filtered reload same-size rows", reader.CurrentRows, "bravo");
        AssertSequence("filtered reload same-size cells", ((IColumnViewportReader)reader).CurrentCells[0], "1", "bravo");
    }

    private static void RunFilteredReloadAfterFileChangeStalesWhenLineBoundaryChanges(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-reload-stale-boundary.log", "alpha\r\nplain\r\n");

        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));
        reader.ReadFromPercentage(0d, 10);

        File.WriteAllText(path, "alphax\nplain\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        AssertThrows<FilteredLineStaleException>(
            "filtered reload stale line boundary",
            () => reader.ReloadAfterFileChange(10));
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
        long confirmedSize = reader.FileSize;
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _ = reader.IsAtKnownEnd;
        reader.RefreshTail(2);

        AssertEqual("tail truncate empty count", reader.CurrentRows.Count, 0);
        AssertEqual("tail truncate empty size", reader.FileSize, 0L);
        File.WriteAllText(path, "line-0\r\nline-1\r\nline-2\r\nline-3\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        AssertEqual("tail truncate empty compares with confirmed size", reader.RefreshFileSize(out long previousSize, out long currentSize), true);
        AssertEqual("tail truncate empty previous confirmed size", previousSize, confirmedSize);
        AssertEqual("tail truncate empty current size", currentSize, new FileInfo(path).Length);
    }

    private static void RunObservedZeroRepeatedKeepsConfirmedSize(string tempRoot)
    {
        string path = WriteLog(tempRoot, "observed-zero-repeated.log", "line-0\r\nline-1\r\nline-2\r\n");

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        long confirmedSize = reader.FileSize;
        reader.ReadFromPercentage(100d, 2);
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        AssertEqual("observed zero first refresh changed", reader.RefreshFileSize(out long firstPrevious, out long firstCurrent), true);
        AssertEqual("observed zero first previous confirmed", firstPrevious, confirmedSize);
        AssertEqual("observed zero first current", firstCurrent, 0L);
        AssertEqual("observed zero file size", reader.FileSize, 0L);
        AssertEqual("observed zero has content", reader.HasContent, false);
        AssertEqual("observed zero rows", reader.CurrentRows.Count, 0);
        AssertEqual("observed zero repeated refresh unchanged", reader.RefreshFileSize(out long secondPrevious, out long secondCurrent), false);
        AssertEqual("observed zero repeated previous confirmed", secondPrevious, confirmedSize);
        AssertEqual("observed zero repeated current", secondCurrent, 0L);
        AssertEqual("observed zero repeated file size", reader.FileSize, 0L);
    }

    private static void RunObservedZeroThenLargerComparesWithConfirmedSize(string tempRoot)
    {
        string initial = "line-0\r\nline-1\r\n";
        string path = WriteLog(tempRoot, "observed-zero-larger.log", initial);

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        long confirmedSize = reader.FileSize;
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshFileSize();
        File.WriteAllText(path, initial + "line-2\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        AssertEqual("observed zero larger refresh changed", reader.RefreshFileSize(out long previousSize, out long currentSize), true);
        AssertEqual("observed zero larger previous confirmed", previousSize, confirmedSize);
        AssertEqual("observed zero larger current size", currentSize, new FileInfo(path).Length);
        AssertEqual("observed zero larger visible size", reader.FileSize, currentSize);
    }

    private static void RunObservedZeroThenSmallerNonZeroTruncates(string tempRoot)
    {
        string path = WriteLog(tempRoot, "observed-zero-smaller.log", "line-0\r\nline-1\r\nline-2\r\nline-3\r\n");

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        long confirmedSize = reader.FileSize;
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshFileSize();
        File.WriteAllText(path, "short\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        AssertEqual("observed zero smaller refresh changed", reader.RefreshFileSize(out long previousSize, out long currentSize), true);
        AssertEqual("observed zero smaller previous confirmed", previousSize, confirmedSize);
        AssertEqual("observed zero smaller current size", currentSize, new FileInfo(path).Length);
        AssertEqual("observed zero smaller is truncate", currentSize < previousSize, true);
    }

    private static void RunObservedZeroThenSameSizeReloadsVisualRows(string tempRoot)
    {
        string initial = "line-0\r\nline-1\r\n";
        string replacement = "neww-0\r\nneww-1\r\n";
        string path = WriteLog(tempRoot, "observed-zero-same-size.log", initial);

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        long confirmedSize = reader.FileSize;
        AssertEqual("observed zero same size setup", replacement.Length, initial.Length);
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshFileSize();
        File.WriteAllText(path, replacement, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        AssertEqual("observed zero same size refresh changed", reader.RefreshFileSize(out long previousSize, out long currentSize), true);
        AssertEqual("observed zero same size previous confirmed", previousSize, confirmedSize);
        AssertEqual("observed zero same size current size", currentSize, confirmedSize);
        reader.ReadFromPercentage(0d, 2);
        AssertSequence("observed zero same size rows", reader.CurrentRows, "neww-0", "neww-1");
    }

    private static void RunObservedZeroPreservesViewportRowsInternally(string tempRoot)
    {
        string initial = "line-0\r\nline-1\r\n";
        string path = WriteLog(tempRoot, "observed-zero-preserve-viewport.log", initial);

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(0d, 2);
        AssertSequence("observed zero preserve setup rows", reader.CurrentRows, "line-0", "line-1");
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshFileSize();
        AssertEqual("observed zero preserve hidden rows", reader.CurrentRows.Count, 0);
        File.WriteAllText(path, initial, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshFileSize();

        AssertSequence("observed zero preserve restored rows", reader.CurrentRows, "line-0", "line-1");
    }

    private static void RunObservedZeroRefreshTailPreservesViewportRowsInternally(string tempRoot)
    {
        string initial = "line-0\r\nline-1\r\n";
        string path = WriteLog(tempRoot, "observed-zero-refresh-tail-preserve-viewport.log", initial);

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(0d, 2);
        AssertSequence("observed zero refresh tail setup rows", reader.CurrentRows, "line-0", "line-1");
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshTail(2);
        AssertEqual("observed zero refresh tail hidden rows", reader.CurrentRows.Count, 0);
        File.WriteAllText(path, initial, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshFileSize();

        AssertSequence("observed zero refresh tail restored rows", reader.CurrentRows, "line-0", "line-1");
    }

    private static void RunObservedZeroReloadPreservesViewportRowsInternally(string tempRoot)
    {
        string initial = "line-0\r\nline-1\r\n";
        string path = WriteLog(tempRoot, "observed-zero-reload-preserve-viewport.log", initial);

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(0d, 2);
        AssertSequence("observed zero reload setup rows", reader.CurrentRows, "line-0", "line-1");
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(2);
        AssertEqual("observed zero reload hidden rows", reader.CurrentRows.Count, 0);
        File.WriteAllText(path, initial, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshFileSize();

        AssertSequence("observed zero reload restored rows", reader.CurrentRows, "line-0", "line-1");
    }

    private static void RunObservedZeroReloadFromEndFollowsRestoredTail(string tempRoot)
    {
        string initial = "line-0\r\nline-1\r\nline-2\r\n";
        string path = WriteLog(tempRoot, "observed-zero-reload-end-tail.log", initial);

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(100d, 2);
        AssertSequence("observed zero reload end setup rows", reader.CurrentRows, "line-1", "line-2");
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(2);
        AssertEqual("observed zero reload end hidden rows", reader.CurrentRows.Count, 0);
        File.WriteAllText(path, initial + "line-3\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(2);

        AssertSequence("observed zero reload end restored tail rows", reader.CurrentRows, "line-2", "line-3");
    }

    private static void RunObservedZeroNavigationPreservesViewportRowsInternally(string tempRoot)
    {
        AssertVisualObservedZeroNavigationPreservesRows(
            tempRoot,
            "read-percentage",
            reader => reader.ReadFromPercentage(100d, 2));
        AssertVisualObservedZeroNavigationPreservesRows(
            tempRoot,
            "read-offset",
            reader => reader.ReadFromOffset(0, 2));
        AssertVisualObservedZeroNavigationPreservesRows(
            tempRoot,
            "read-next",
            reader => reader.ReadNext(1));
        AssertVisualObservedZeroNavigationPreservesRows(
            tempRoot,
            "read-previous",
            reader => reader.ReadPrevious(1));
    }

    private static void AssertVisualObservedZeroNavigationPreservesRows(
        string tempRoot,
        string name,
        Action<VisualRowReader> action)
    {
        string initial = "line-0\r\nline-1\r\n";
        string path = WriteLog(tempRoot, "observed-zero-navigation-" + name + ".log", initial);

        using VisualRowReader reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(0d, 2);
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshFileSize();

        action(reader);
        AssertEqual("observed zero navigation " + name + " hidden rows", reader.CurrentRows.Count, 0);
        File.WriteAllText(path, initial, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshFileSize();

        AssertSequence("observed zero navigation " + name + " restored rows", reader.CurrentRows, "line-0", "line-1");
    }

    private static void RunFilteredObservedZeroRepeatedKeepsConfirmedSize(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-observed-zero-repeated.log", "line-0\r\nline-1\r\nline-2\r\n");
        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("line", UseRegex: false, IgnoreCase: false));
        long confirmedSize = reader.ConfirmedFileSize;
        reader.ReadFromPercentage(100d, 10);
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        reader.ReloadAfterFileChange(10);
        AssertEqual("filtered observed zero file size", reader.FileSize, 0L);
        AssertEqual("filtered observed zero confirmed size", reader.ConfirmedFileSize, confirmedSize);
        AssertEqual("filtered observed zero has content", reader.HasContent, false);
        AssertEqual("filtered observed zero rows", reader.CurrentRows.Count, 0);
        AssertEqual("filtered observed zero cells", reader.CurrentCells.Count, 0);
        reader.ReloadAfterFileChange(10);
        AssertEqual("filtered observed zero repeated file size", reader.FileSize, 0L);
        AssertEqual("filtered observed zero repeated confirmed size", reader.ConfirmedFileSize, confirmedSize);
    }

    private static void RunFilteredObservedZeroPreservesViewportRowsInternally(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-observed-zero-preserve-viewport.log", "line-0\r\nline-1\r\n");
        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("line", UseRegex: false, IgnoreCase: false));
        reader.ReadFromPercentage(0d, 10);
        AssertSequence("filtered observed zero preserve setup rows", reader.CurrentRows, "line-0", "line-1");
        AssertEqual("filtered observed zero preserve setup cells", reader.CurrentCells.Count, 2);

        reader.MarkObservedZeroFileSize();
        AssertEqual("filtered observed zero preserve hidden rows", reader.CurrentRows.Count, 0);
        AssertEqual("filtered observed zero preserve hidden cells", reader.CurrentCells.Count, 0);
        reader.ClearObservedZeroFileSize();

        AssertSequence("filtered observed zero preserve restored rows", reader.CurrentRows, "line-0", "line-1");
        AssertEqual("filtered observed zero preserve restored cells", reader.CurrentCells.Count, 2);
    }

    private static void RunFilteredObservedZeroPreservesConfirmedEndState(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-observed-zero-confirmed-end.log", "line-0\r\nline-1\r\nline-2\r\nline-3\r\n");
        using FilteredVisualRowReader endReader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("line", UseRegex: false, IgnoreCase: false));
        endReader.ReadFromPercentage(100d, 2);
        AssertEqual("filtered observed zero end setup confirmed end", endReader.IsAtConfirmedEnd, true);
        endReader.MarkObservedZeroFileSize();
        AssertEqual("filtered observed zero end visual end", endReader.IsAtEnd, true);
        AssertEqual("filtered observed zero end confirmed end", endReader.IsAtConfirmedEnd, true);

        using FilteredVisualRowReader topReader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("line", UseRegex: false, IgnoreCase: false));
        topReader.ReadFromPercentage(0d, 2);
        AssertEqual("filtered observed zero top setup confirmed end", topReader.IsAtConfirmedEnd, false);
        topReader.MarkObservedZeroFileSize();
        AssertEqual("filtered observed zero top visual end", topReader.IsAtEnd, true);
        AssertEqual("filtered observed zero top confirmed end", topReader.IsAtConfirmedEnd, false);
    }

    private static void RunFilteredObservedZeroReloadAwayFromEndPreservesPosition(string tempRoot)
    {
        string initial = "line-0\r\nline-1\r\nline-2\r\nline-3\r\n";
        string path = WriteLog(tempRoot, "filtered-observed-zero-reload-away.log", initial);
        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("line", UseRegex: false, IgnoreCase: false));
        reader.ReadFromPercentage(0d, 2);
        AssertSequence("filtered observed zero reload away setup rows", reader.CurrentRows, "line-0", "line-1");
        AssertEqual("filtered observed zero reload away setup confirmed end", reader.IsAtConfirmedEnd, false);
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(2);
        AssertEqual("filtered observed zero reload away hidden rows", reader.CurrentRows.Count, 0);
        File.WriteAllText(path, initial + "line-4\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(2);

        AssertSequence("filtered observed zero reload away restored rows", reader.CurrentRows, "line-0", "line-1");
    }

    private static void RunFilteredObservedZeroNavigationPreservesViewportRowsInternally(string tempRoot)
    {
        AssertFilteredObservedZeroNavigationPreservesRows(
            tempRoot,
            "read-percentage",
            reader => reader.ReadFromPercentage(100d, 10));
        AssertFilteredObservedZeroNavigationPreservesRows(
            tempRoot,
            "read-row-ordinal",
            reader => reader.ReadFromRowOrdinal(1, 10));
        AssertFilteredObservedZeroNavigationPreservesRows(
            tempRoot,
            "read-next",
            reader => reader.ReadNext(1));
        AssertFilteredObservedZeroNavigationPreservesRows(
            tempRoot,
            "read-previous",
            reader => reader.ReadPrevious(1));
    }

    private static void AssertFilteredObservedZeroNavigationPreservesRows(
        string tempRoot,
        string name,
        Action<FilteredVisualRowReader> action)
    {
        string path = WriteLog(tempRoot, "filtered-observed-zero-navigation-" + name + ".log", "line-0\r\nline-1\r\n");
        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("line", UseRegex: false, IgnoreCase: false));
        reader.ReadFromPercentage(0d, 10);
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(10);

        action(reader);
        AssertEqual("filtered observed zero navigation " + name + " hidden rows", reader.CurrentRows.Count, 0);
        AssertEqual("filtered observed zero navigation " + name + " hidden cells", reader.CurrentCells.Count, 0);
        reader.ClearObservedZeroFileSize();

        AssertSequence("filtered observed zero navigation " + name + " restored rows", reader.CurrentRows, "line-0", "line-1");
        AssertEqual("filtered observed zero navigation " + name + " restored cells", reader.CurrentCells.Count, 2);
    }

    private static void RunFilteredObservedZeroThenSameSizeReloadsRows(string tempRoot)
    {
        string initial = "line-0\r\nline-1\r\n";
        string replacement = "neww-0\r\nneww-1\r\n";
        string path = WriteLog(tempRoot, "filtered-observed-zero-same-size.log", initial);
        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("line", UseRegex: false, IgnoreCase: false));
        long confirmedSize = reader.ConfirmedFileSize;
        AssertEqual("filtered observed zero same size setup", replacement.Length, initial.Length);
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(10);
        File.WriteAllText(path, replacement, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        reader.ReloadAfterFileChange(10);
        AssertEqual("filtered observed zero same size visible size", reader.FileSize, confirmedSize);
        AssertEqual("filtered observed zero same size confirmed size", reader.ConfirmedFileSize, confirmedSize);
        AssertSequence("filtered observed zero same size rows", reader.CurrentRows, "neww-0", "neww-1");
    }

    private static void RunFilteredObservedZeroThenSmallerKeepsConfirmedForStaleDecision(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-observed-zero-smaller.log", "line-0\r\nline-1\r\nline-2\r\n");
        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("line", UseRegex: false, IgnoreCase: false));
        long confirmedSize = reader.ConfirmedFileSize;
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(10);
        File.WriteAllText(path, "short\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        long currentSize = new FileInfo(path).Length;
        AssertEqual("filtered observed zero smaller visible size", reader.FileSize, 0L);
        AssertEqual("filtered observed zero smaller confirmed size", reader.ConfirmedFileSize, confirmedSize);
        AssertEqual("filtered observed zero smaller is stale candidate", currentSize < reader.ConfirmedFileSize, true);
    }

    private static void RunAppendAfterFilteredObservedZeroUsesConfirmedSize(string tempRoot)
    {
        string initial = "alpha-0\r\nalpha-1\r\n";
        string path = WriteLog(tempRoot, "filtered-observed-zero-append.log", initial);
        using FilteredVisualRowReader reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(10);
        File.WriteAllText(path, initial + "alpha-2\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        using FilteredVisualRowReader appended = BuildAppendedReader(reader, new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));
        AssertEqual("filtered observed zero append match count", appended.MatchedLineCount, 3L);
        AssertSequence("filtered observed zero append rows", appended.CurrentRows, "alpha-0", "alpha-1", "alpha-2");
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

    private static int[] CreateFilledArray(int count, int value)
    {
        int[] values = new int[count];
        Array.Fill(values, value);
        return values;
    }

    private static void DisposeReaders(IReadOnlyList<FilteredVisualRowReader> readers)
    {
        foreach (FilteredVisualRowReader reader in readers)
        {
            reader.Dispose();
        }
    }

    private static void DisposeNullableReaders(IReadOnlyList<FilteredVisualRowReader?> readers)
    {
        foreach (FilteredVisualRowReader? reader in readers)
        {
            reader?.Dispose();
        }
    }

    private static StagedSearchProgressUpdate FindPausedUpdate(IReadOnlyList<StagedSearchProgressUpdate> updates)
    {
        foreach (StagedSearchProgressUpdate update in updates)
        {
            if (update.IsPaused)
            {
                return update;
            }
        }

        throw new InvalidOperationException("Paused update was not published.");
    }

    private static DisplayParserRule ParserRule(params DisplayParserStage[] stages)
    {
        return new DisplayParserRule
        {
            Stages = new List<DisplayParserStage>(stages)
        };
    }

    private static DisplayParserStage JsonStage(string template)
    {
        return new DisplayParserStage
        {
            Mode = DisplayParserMode.Json,
            Rule = template
        };
    }

    private static DisplayParserStage RegexStage(string pattern, string template = "")
    {
        return new DisplayParserStage
        {
            Mode = DisplayParserMode.Regex,
            Rule = pattern,
            Template = template
        };
    }

    private static DisplayParserStage RegexReplaceStage(string pattern, string replacement)
    {
        return new DisplayParserStage
        {
            Mode = DisplayParserMode.RegexReplace,
            Rule = pattern,
            Template = replacement
        };
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
