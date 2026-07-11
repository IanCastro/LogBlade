using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

internal static class Program
{
    private const int LongLineChunkChars = 4096;
    private static int Main()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "LogBladeBackSmoke", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempRoot);

            RunDisplayParserJsonTemplate();
            RunDisplayParserJsonPreservesTemplateWhitespace();
            RunDisplayParserJsonAllowsWhitespaceOnlyTemplate();
            RunDisplayParserJsonWithTrailingText();
            RunDisplayParserJsonSkipsInvalidPrefixCandidate();
            RunDisplayParserGeneratesJsonTemplateFromSample();
            RunDisplayParserGeneratesJsonTemplateFromMultipleSamples();
            RunDisplayParserGeneratesNestedJsonTemplate();
            RunDisplayParserGeneratesTemplateFromSplitJson();
            RunDisplayParserIgnoresInvalidJsonTemplatePaths();
            RunDisplayParserGeneratesEmptyTemplateWithoutJson();
            RunDisplayParserRegexNamedDisplay();
            RunDisplayParserRegexDefaultFullMatch();
            RunDisplayParserRegexPreservesPatternSpaces();
            RunDisplayParserRegexPreservesDisplaySpaces();
            RunDisplayParserRegexAllowsWhitespaceOnlyDisplay();
            RunDisplayParserRegexInvalidRegexValidation();
            RunDisplayParserFallbackOriginal();
            RunDisplayParserRegexThenJsonTemplate();
            RunDisplayParserRegexThenJsonAcrossLines();
            RunDisplayParserIncompleteJsonPreservesOriginalLines();
            RunDisplayParserContinuationMismatchPreservesOriginalLines();
            RunDisplayParserRecordLineLimitPreservesOriginalLines();
            RunDisplayParserSecondStageFailureReturnsFirstOutput();
            RunDisplayParserRegexReplaceSimple();
            RunDisplayParserRegexReplaceGlobal();
            RunDisplayParserRegexReplaceEmptyReplacement();
            RunDisplayParserRegexReplacePreservesSpaces();
            RunDisplayParserRegexReplaceAllowsSpacePattern();
            RunDisplayParserRegexReplaceGroups();
            RunDisplayParserRegexReplaceReplacementUnicodeEscape();
            RunDisplayParserRegexReplaceReplacementEscapes();
            RunDisplayParserRegexReplacePreservesUnknownReplacementEscape();
            RunDisplayParserRegexReplaceNoMatchAllowsNextStage();
            RunDisplayParserRegexReplaceThenJsonTemplate();
            RunDisplayParserRegexReplaceInvalidRegexValidation();
            RunDisplayParserRegexReplaceInvalidReplacementEscapeValidation();
            RunMemoryContentSource();
            RunSearchUsesDisplayParserLiteral(tempRoot);
            RunSearchUsesDisplayParserRegexCaptures(tempRoot);
            RunAppendSearchUsesDisplayParser(tempRoot);
            RunSearchUsesCascadedDisplayParserLiteral(tempRoot);
            RunSearchUsesCascadedDisplayParserAcrossLines(tempRoot);
            RunSearchUsesCascadedDisplayParserRegexCaptures(tempRoot);
            RunAppendSearchUsesCascadedDisplayParser(tempRoot);
            RunAppendSearchCompletesCascadedDisplayParserRecord(tempRoot);
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
            RunSearchFindsTextOnSecondParsedLine(tempRoot);
            RunSearchCapturesOnlyMatchingParsedLine(tempRoot);
            RunSearchDoesNotWrapLongParsedLine(tempRoot);
            RunSearchReturnsEachMatchingParsedLine(tempRoot);
            RunSearchInvertFiltersParsedLines(tempRoot);
            RunCascadedSearchDoesNotCrossParsedLines(tempRoot);
            RunChangedCascadedSearchUsesMatchedParsedLine(tempRoot);
            RunAppendSearchKeepsMatchedParsedLine(tempRoot);
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
            RunAppendStagedSearchCompletesCascadedParserRecord(tempRoot);
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
            RunLogRecordSourceReusesSlidingWindow(tempRoot);
            RunFilteredLogRecordSourceReusesSlidingWindow(tempRoot);
            RunPageUpNearStartClampsToTop(tempRoot);
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
            RunObservedZeroThenSameSizeReloadsRecords(tempRoot);
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

    private static void RunDisplayParserJsonPreservesTemplateWhitespace()
    {
        DisplayParserRule rule = ParserRule(JsonStage(" \t{Level}\r\n "));

        AssertEqual(
            "display parser json preserves template whitespace",
            DisplayParserEvaluator.EvaluateOrOriginal(rule, "{\"Level\":\"Info\"}"),
            " \tInfo\r\n ");
    }

    private static void RunDisplayParserJsonAllowsWhitespaceOnlyTemplate()
    {
        DisplayParserStage stage = JsonStage("   ");
        DisplayParserEvaluator.ValidateStage(stage);

        AssertEqual(
            "display parser json allows whitespace-only template",
            DisplayParserEvaluator.EvaluateOrOriginal(ParserRule(stage), "{\"Level\":\"Info\"}"),
            "   ");
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

    private static void RunDisplayParserGeneratesTemplateFromSplitJson()
    {
        string sample =
            "{\"Timestamp\":\"2026-06-24\",\"Level\":\"Inf\r\n" +
            "o\",\"Message\":\"running\"}";

        AssertEqual(
            "display parser generated template from split json",
            DisplayParserEvaluator.GenerateJsonTemplateFromSample(sample),
            "Timestamp - {Timestamp}, Level - {Level}, Message - {Message}");
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

    private static void RunDisplayParserRegexPreservesDisplaySpaces()
    {
        DisplayParserRule rule = ParserRule(RegexStage(@"(?<value>abc)", " \t{value}\r\n "));

        AssertEqual(
            "display parser regex preserves display spaces",
            DisplayParserEvaluator.EvaluateOrOriginal(rule, "abc"),
            " \tabc\r\n ");
    }

    private static void RunDisplayParserRegexAllowsWhitespaceOnlyDisplay()
    {
        DisplayParserRule rule = ParserRule(RegexStage("abc", "   "));

        AssertEqual(
            "display parser regex allows whitespace-only display",
            DisplayParserEvaluator.EvaluateOrOriginal(rule, "abc"),
            "   ");
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

    private static void RunDisplayParserRegexThenJsonAcrossLines()
    {
        DisplayParserRule rule = ParserRule(
            RegexStage(@": (?<json>.*)", "{json}"),
            JsonStage("{Timestamp} [{Logger}] {upper:Level} {Logger} - {Message}"));
        string input =
            "a[0]: { \"Timestamp\": \"2025-09-12 14:50:48.637060\", \"Level\": \"Inf\r\n" +
            "a[1]: o\", \"Logger\": \"EventScheduler\", \"Mes\r" +
            "a[2]: sage\": \"Strategy task JT67_48_250912145048_00064 is running\" }";

        AssertEqual(
            "display parser regex then json across lines",
            DisplayParserEvaluator.EvaluateLinesOrOriginal(rule, input),
            "2025-09-12 14:50:48.637060 [EventScheduler] INFO EventScheduler - " +
            "Strategy task JT67_48_250912145048_00064 is running");
    }

    private static void RunDisplayParserIncompleteJsonPreservesOriginalLines()
    {
        DisplayParserRule rule = ParserRule(
            RegexStage(@": (?<json>.*)", "{json}"),
            JsonStage("{Key}"));
        string input =
            "a[0]: {\"Key\":\"val\r\n" +
            "a[1]: ue";

        AssertEqual(
            "display parser incomplete json preserves originals",
            DisplayParserEvaluator.EvaluateLinesOrOriginal(rule, input),
            input);
    }

    private static void RunDisplayParserContinuationMismatchPreservesOriginalLines()
    {
        DisplayParserRule rule = ParserRule(
            RegexStage(@": (?<json>.*)", "{json}"),
            JsonStage("{Key}"));
        string input =
            "a[0]: {\"Key\":\"val\r\n" +
            "not a continuation\r\n" +
            "a[2]: ue\"}";

        AssertEqual(
            "display parser continuation mismatch preserves originals",
            DisplayParserEvaluator.EvaluateLinesOrOriginal(rule, input),
            "a[0]: {\"Key\":\"val" + Environment.NewLine +
            "not a continuation" + Environment.NewLine +
            "ue\"}");
    }

    private static void RunDisplayParserRecordLineLimitPreservesOriginalLines()
    {
        DisplayParserRule rule = ParserRule(
            RegexStage(@": (?<json>.*)", "{json}"),
            JsonStage("{Key}"));
        StringBuilder input = new();
        input.Append("a[0]: {");
        for (int i = 1; i <= 4096; i++)
        {
            input.AppendLine();
            input.Append("a[");
            input.Append(i);
            input.Append("]: ");
        }

        string value = input.ToString();
        AssertEqual(
            "display parser record line limit preserves originals",
            DisplayParserEvaluator.EvaluateLinesOrOriginal(rule, value),
            value);
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

    private static void RunDisplayParserRegexReplaceReplacementUnicodeEscape()
    {
        DisplayParserRule rule = ParserRule(RegexReplaceStage(@"\|", @"\u0001"));

        AssertEqual("display parser regex replace replacement unicode escape", DisplayParserEvaluator.EvaluateOrOriginal(rule, "a|b"), "a\u0001b");
    }

    private static void RunDisplayParserRegexReplaceReplacementEscapes()
    {
        DisplayParserRule rule = ParserRule(RegexReplaceStage(@"x", @"\t\n\r\\"));

        AssertEqual("display parser regex replace replacement escapes", DisplayParserEvaluator.EvaluateOrOriginal(rule, "x"), "\t\n\r\\");
    }

    private static void RunDisplayParserRegexReplacePreservesUnknownReplacementEscape()
    {
        DisplayParserRule rule = ParserRule(RegexReplaceStage(@"x", @"\q"));

        AssertEqual("display parser regex replace preserves unknown replacement escape", DisplayParserEvaluator.EvaluateOrOriginal(rule, "x"), @"\q");
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

    private static void RunDisplayParserRegexReplaceInvalidReplacementEscapeValidation()
    {
        try
        {
            DisplayParserEvaluator.ValidateStage(RegexReplaceStage(@"x", @"\u00ZZ"));
        }
        catch (ArgumentException ex) when (ex.Message.Contains("Unicode escape", StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException("display parser regex replace invalid replacement escape: expected validation failure.");
    }

    private static void RunSearchUsesDisplayParserLiteral(string tempRoot)
    {
        string path = WriteLog(
            tempRoot,
            "display-parser-search-literal.log",
            "{\"Level\":\"Info\",\"Message\":\"ready\"}\r\n{\"Level\":\"Error\",\"Message\":\"failed\"}\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{upper:Level} - {Message}"));

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("ERROR - failed", UseRegex: false, IgnoreCase: false),
            parser);

        reader.ReadFromPercentage(0d, 10);
        FilteredLogRecordSource columns = reader;
        AssertSequence("display parser literal rows", reader.CurrentDisplayTexts, "ERROR - failed");
        AssertSequence("display parser literal cells", columns.CurrentCells[0], "2", "ERROR - failed");
    }

    private static void RunSearchUsesDisplayParserRegexCaptures(string tempRoot)
    {
        string path = WriteLog(
            tempRoot,
            "display-parser-search-regex.log",
            "{\"Level\":\"Info\",\"Message\":\"ready\"}\r\n{\"Level\":\"Error\",\"Message\":\"failed\"}\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{upper:Level} - {Message}"));

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(?<level>ERROR) - (?<message>failed)", UseRegex: true, IgnoreCase: false),
            parser);

        reader.ReadFromPercentage(0d, 10);
        FilteredLogRecordSource columns = reader;
        AssertSequence("display parser regex headers", columns.ColumnHeaders, "#", "Text", "level", "message");
        AssertSequence("display parser regex cells", columns.CurrentCells[0], "2", "ERROR - failed", "ERROR", "failed");
    }

    private static void RunAppendSearchUsesDisplayParser(string tempRoot)
    {
        string path = WriteLog(tempRoot, "display-parser-append.log", "{\"Level\":\"Info\",\"Message\":\"ready\"}\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{upper:Level} - {Message}"));

        using FilteredLogRecordSource initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("ERROR", UseRegex: false, IgnoreCase: false),
            parser);

        File.AppendAllText(path, "{\"Level\":\"Error\",\"Message\":\"failed\"}\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using FilteredLogRecordSource appended = BuildAppendedReader(initial, new SearchOptions("ERROR", UseRegex: false, IgnoreCase: false), parser);
        appended.ReadFromPercentage(0d, 10);

        AssertSequence("display parser append rows", appended.CurrentDisplayTexts, "ERROR - failed");
        AssertSequence("display parser append cells", ((FilteredLogRecordSource)appended).CurrentCells[0], "2", "ERROR - failed");
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

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("KEY=Value", UseRegex: false, IgnoreCase: false),
            parser);

        reader.ReadFromPercentage(0d, 10);
        FilteredLogRecordSource columns = reader;
        AssertSequence("cascaded display parser literal rows", reader.CurrentDisplayTexts, "KEY=Value");
        AssertSequence("cascaded display parser literal cells", columns.CurrentCells[0], "1", "KEY=Value");
    }

    private static void RunSearchUsesCascadedDisplayParserAcrossLines(string tempRoot)
    {
        string path = WriteLog(
            tempRoot,
            "display-parser-cascade-multiline.log",
            "a[0]: { \"Timestamp\": \"2025-09-12 14:50:48.637060\", \"Level\": \"Inf\r\n" +
            "a[1]: o\", \"Logger\": \"EventScheduler\", \"Mes\r\n" +
            "a[2]: sage\": \"Strategy task JT67_48_250912145048_00064 is running\" }\r\n" +
            "plain\r\n");
        DisplayParserRule parser = ParserRule(
            RegexStage(@": (?<json>.*)", "{json}"),
            JsonStage("{Timestamp} [{Logger}] {upper:Level} {Logger} - {Message}"));

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("JT67_48", UseRegex: false, IgnoreCase: false),
            parser);

        reader.ReadFromPercentage(0d, 10);
        AssertSequence(
            "cascaded display parser multiline rows",
            reader.CurrentDisplayTexts,
            "2025-09-12 14:50:48.637060 [EventScheduler] INFO EventScheduler - " +
            "Strategy task JT67_48_250912145048_00064 is running");
        AssertSequence(
            "cascaded display parser multiline cells",
            ((FilteredLogRecordSource)reader).CurrentCells[0],
            "1",
            "2025-09-12 14:50:48.637060 [EventScheduler] INFO EventScheduler - " +
            "Strategy task JT67_48_250912145048_00064 is running");
        AssertEqual(
            "cascaded display parser multiline offset",
            ((FilteredLogRecordSource)reader).TryGetRecordStartOffset(0, out long startOffset),
            true);
        AssertEqual("cascaded display parser multiline first offset", startOffset, 0L);
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

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(?<level>ERROR) - (?<message>failed)", UseRegex: true, IgnoreCase: false),
            parser);

        reader.ReadFromPercentage(0d, 10);
        FilteredLogRecordSource columns = reader;
        AssertSequence("cascaded display parser regex headers", columns.ColumnHeaders, "#", "Text", "level", "message");
        AssertSequence("cascaded display parser regex cells", columns.CurrentCells[0], "2", "ERROR - failed", "ERROR", "failed");
    }

    private static void RunAppendSearchUsesCascadedDisplayParser(string tempRoot)
    {
        string path = WriteLog(tempRoot, "display-parser-cascade-append.log", "Out[0]: {\"Level\":\"Info\",\"Message\":\"ready\"}\r\n");
        DisplayParserRule parser = ParserRule(
            RegexStage(@": (?<json>.*)", "{json}"),
            JsonStage("{upper:Level} - {Message}"));

        using FilteredLogRecordSource initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("ERROR", UseRegex: false, IgnoreCase: false),
            parser);

        File.AppendAllText(path, "Out[1]: {\"Level\":\"Error\",\"Message\":\"failed\"}\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using FilteredLogRecordSource appended = BuildAppendedReader(initial, new SearchOptions("ERROR", UseRegex: false, IgnoreCase: false), parser);
        appended.ReadFromPercentage(0d, 10);

        AssertSequence("cascaded display parser append rows", appended.CurrentDisplayTexts, "ERROR - failed");
        AssertSequence("cascaded display parser append cells", ((FilteredLogRecordSource)appended).CurrentCells[0], "2", "ERROR - failed");
    }

    private static void RunAppendSearchCompletesCascadedDisplayParserRecord(string tempRoot)
    {
        string path = WriteLog(
            tempRoot,
            "display-parser-cascade-append-combined.log",
            "a[0]: {\"Level\":\"Inf\r\n" +
            "a[1]: o\",\"Message\":\"run\r\n");
        DisplayParserRule parser = ParserRule(
            RegexStage(@": (?<json>.*)", "{json}"),
            JsonStage("{upper:Level} - {Message}"));

        using FilteredLogRecordSource initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("running", UseRegex: false, IgnoreCase: false),
            parser);

        File.AppendAllText(path, "a[2]: ning\"}\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using FilteredLogRecordSource appended = BuildAppendedReader(
            initial,
            new SearchOptions("running", UseRegex: false, IgnoreCase: false),
            parser);
        appended.ReadFromPercentage(0d, 10);

        AssertSequence("cascaded display parser append combined rows", appended.CurrentDisplayTexts, "INFO - running");
        AssertSequence(
            "cascaded display parser append combined cells",
            ((FilteredLogRecordSource)appended).CurrentCells[0],
            "1",
            "INFO - running");
    }

    private static void RunRegexCaptureGroups(string tempRoot)
    {
        string path = WriteLog(tempRoot, "capture-groups.log", "aaabccc xx aabcc\r\nplain\r\n");

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(a+)b(c+)", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        FilteredLogRecordSource columns = reader;

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

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("code-(?<code>\\d+) user-(?<user>[a-z]+)", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        FilteredLogRecordSource columns = reader;

        AssertSequence("named capture headers", columns.ColumnHeaders, "#", "Text", "code", "user");
        AssertSequence("named capture cells", columns.CurrentCells[0], "1", "code-42 user-ian", "42", "ian");
    }

    private static void RunRegexMixedNamedAndUnnamedCaptureGroups(string tempRoot)
    {
        string path = WriteLog(tempRoot, "mixed-capture-groups.log", "aaabccc\r\nplain\r\n");

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(a+)b(?<suffix>c+)", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        FilteredLogRecordSource columns = reader;

        AssertSequence("mixed capture headers", columns.ColumnHeaders, "#", "Text", "0", "suffix");
        AssertSequence("mixed capture cells", columns.CurrentCells[0], "1", "aaabccc", "aaa", "ccc");
    }

    private static void RunRegexNonCapturingGroupsDoNotCreateColumns(string tempRoot)
    {
        string path = WriteLog(tempRoot, "non-capturing-groups.log", "GET /api/orders\r\nPOST /api/users\r\nplain\r\n");

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(?:GET|POST) /api/(?<resource>\\w+)", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        FilteredLogRecordSource columns = reader;

        AssertSequence("non-capturing headers", columns.ColumnHeaders, "#", "Text", "resource");
        AssertSequence("non-capturing first cells", columns.CurrentCells[0], "1", "GET /api/orders", "orders");
        AssertSequence("non-capturing second cells", columns.CurrentCells[1], "2", "POST /api/users", "users");
    }

    private static void RunRegexOnlyNonCapturingGroupsUsesPlainRows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "only-non-capturing-groups.log", "GET /api/orders\r\nplain\r\n");

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(?:GET|POST) /api/\\w+", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        FilteredLogRecordSource columns = reader;

        AssertSequence("only non-capturing headers", columns.ColumnHeaders, "#", "Text");
        AssertSequence("only non-capturing cells", columns.CurrentCells[0], "1", "GET /api/orders");
    }

    private static void RunRegexOptionalCaptureGroupUsesEmptyCell(string tempRoot)
    {
        string path = WriteLog(tempRoot, "optional-capture-groups.log", "code-\r\ncode-42\r\nplain\r\n");

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("code-(?<code>\\d+)?", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        FilteredLogRecordSource columns = reader;

        AssertSequence("optional capture headers", columns.ColumnHeaders, "#", "Text", "code");
        AssertSequence("optional capture empty cells", columns.CurrentCells[0], "1", "code-", string.Empty);
        AssertSequence("optional capture filled cells", columns.CurrentCells[1], "2", "code-42", "42");
    }

    private static void RunRegexWithoutGroupsUsesPlainRows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "regex-no-groups.log", "aaabccc\r\n");

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("a+b", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        FilteredLogRecordSource columns = reader;
        AssertSequence("regex no-group headers", columns.ColumnHeaders, "#", "Text");
        AssertSequence("regex no-group cells", columns.CurrentCells[0], "1", "aaabccc");
        AssertSequence("regex no-group rows", reader.CurrentDisplayTexts, "aaabccc");
    }

    private static void RunLiteralSearchUsesPlainRows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "literal.log", "line.with.dot\r\nplain\r\n");

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions(".", UseRegex: false, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        FilteredLogRecordSource columns = reader;
        AssertSequence("literal headers", columns.ColumnHeaders, "#", "Text");
        AssertSequence("literal cells", columns.CurrentCells[0], "1", "line.with.dot");
        AssertSequence("literal rows", reader.CurrentDisplayTexts, "line.with.dot");
    }

    private static void RunLiteralInvertMatch(string tempRoot)
    {
        string path = WriteLog(tempRoot, "literal-invert.log", "alpha\r\nplain\r\nbeta\r\n");

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false, InvertMatch: true));

        reader.ReadFromPercentage(0d, 10);
        FilteredLogRecordSource columns = reader;
        AssertSequence("literal invert headers", columns.ColumnHeaders, "#", "Text");
        AssertSequence("literal invert first cells", columns.CurrentCells[0], "2", "plain");
        AssertSequence("literal invert second cells", columns.CurrentCells[1], "3", "beta");
        AssertSequence("literal invert rows", reader.CurrentDisplayTexts, "plain", "beta");
    }

    private static void RunRegexInvertMatch(string tempRoot)
    {
        string path = WriteLog(tempRoot, "regex-invert.log", "alpha\r\nplain\r\nbeta\r\n");

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("^a", UseRegex: true, IgnoreCase: false, InvertMatch: true));

        reader.ReadFromPercentage(0d, 10);
        FilteredLogRecordSource columns = reader;
        AssertSequence("regex invert headers", columns.ColumnHeaders, "#", "Text");
        AssertSequence("regex invert first cells", columns.CurrentCells[0], "2", "plain");
        AssertSequence("regex invert second cells", columns.CurrentCells[1], "3", "beta");
        AssertSequence("regex invert rows", reader.CurrentDisplayTexts, "plain", "beta");
    }

    private static void RunRegexCaptureGroupsInvertUsesPlainRows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "regex-capture-invert.log", "aaabccc\r\nplain\r\n");

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(a+)b(c+)", UseRegex: true, IgnoreCase: false, InvertMatch: true));

        reader.ReadFromPercentage(0d, 10);
        FilteredLogRecordSource columns = reader;
        AssertSequence("regex capture invert headers", columns.ColumnHeaders, "#", "Text");
        AssertSequence("regex capture invert cells", columns.CurrentCells[0], "2", "plain");
        AssertSequence("regex capture invert rows", reader.CurrentDisplayTexts, "plain");
    }

    private static void RunWrappedLineCaptureGroups(string tempRoot)
    {
        string longText = "aaabccc" + new string('x', LongLineChunkChars + 16);
        string path = WriteLog(tempRoot, "wrapped-captures.log", longText + "\r\n");

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(a+)b(c+)", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 3);
        FilteredLogRecordSource columns = reader;

        AssertSequence("wrapped headers", columns.ColumnHeaders, "#", "Text", "0", "1");
        AssertEqual("wrapped row count", columns.CurrentCells.Count, 1);
        AssertEqual("wrapped line number", columns.CurrentCells[0][0], "1");
        AssertEqual("wrapped text", columns.CurrentCells[0][1], longText);
        AssertEqual("wrapped first group 0", columns.CurrentCells[0][2], "aaa");
        AssertEqual("wrapped first group 1", columns.CurrentCells[0][3], "ccc");
    }

    private static void RunFilteredLineStaleWhenStartMoves(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-stale-start.log", "one\r\ntwo alpha\r\n");

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
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

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
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

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("beta", UseRegex: false, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        AssertSequence("filtered lf rows", reader.CurrentDisplayTexts, "beta");
    }

    private static void RunFilteredLineValidationAcceptsUtf16Le(string tempRoot)
    {
        string path = Path.Combine(tempRoot, "filtered-utf16-le.log");
        File.WriteAllText(path, "alpha\r\nbeta\r\n", Encoding.Unicode);

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.Unicode,
            dataOffset: 2,
            new SearchOptions("beta", UseRegex: false, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        AssertSequence("filtered utf16 le rows", reader.CurrentDisplayTexts, "beta");
    }

    private static void RunFilteredLineValidationAcceptsUtf16Be(string tempRoot)
    {
        string path = Path.Combine(tempRoot, "filtered-utf16-be.log");
        File.WriteAllText(path, "alpha\r\nbeta\r\n", Encoding.BigEndianUnicode);

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.BigEndianUnicode,
            dataOffset: 2,
            new SearchOptions("beta", UseRegex: false, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        AssertSequence("filtered utf16 be rows", reader.CurrentDisplayTexts, "beta");
    }

    private static void RunFilteredLineValidationAcceptsFinalLineWithoutBreak(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-no-final-break.log", "plain\r\nalpha");

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        AssertSequence("filtered no final break rows", reader.CurrentDisplayTexts, "alpha");
    }

    private static void RunFilteredLineValidationAcceptsEmptyLine(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-empty-line.log", "alpha\r\n\r\nbeta\r\n");

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("^$", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        AssertSequence("filtered empty line rows", reader.CurrentDisplayTexts, string.Empty);
    }

    private static void RunSearchRowOffsetSyncsMainReader(string tempRoot)
    {
        string path = WriteLog(tempRoot, "search-row-offset-sync.log", "zero\r\none alpha\r\ntwo beta alpha\r\nthree\r\n");

        using FilteredLogRecordSource filtered = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));

        AssertEqual("search row offset available", ((FilteredLogRecordSource)filtered).TryGetRecordStartOffset(1, out long startOffset), true);

        using LogRecordSource main = new(path, Encoding.UTF8, dataOffset: 0);
        main.ReadFromOffset(startOffset, 2);

        AssertSequence("search row offset main rows", main.CurrentDisplayTexts, "two beta alpha", "three");
    }

    private static void RunSearchRowOffsetDetectsStaleLine(string tempRoot)
    {
        string path = WriteLog(tempRoot, "search-row-offset-stale.log", "one\r\ntwo alpha\r\n");

        using FilteredLogRecordSource filtered = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));

        File.WriteAllText(path, "one extended\r\ntwo alpha\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        AssertThrows<FilteredLineStaleException>(
            "search row offset stale",
            () => ((FilteredLogRecordSource)filtered).TryGetRecordStartOffset(0, out _));
    }

    private static void RunSearchRowOrdinalLookup(string tempRoot)
    {
        string path = WriteLog(tempRoot, "search-row-ordinal.log", "alpha-0\r\nskip\r\nalpha-1\r\nalpha-2\r\n");

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 2);
        LogRecordKey secondMatch = reader.CurrentRecords[1].Key;
        reader.ReadFromPercentage(100d, 2);

        AssertEqual("search row ordinal available", ((FilteredLogRecordSource)reader).TryGetRecordOrdinal(secondMatch, out long rowOrdinal), true);
        AssertEqual("search row ordinal value", rowOrdinal, 1L);

        reader.ReadFromRecordOrdinal(rowOrdinal, 2);
        AssertSequence("search row ordinal rows", reader.CurrentDisplayTexts, "alpha-1", "alpha-2");
    }

    private static void RunSearchFindsTextOnSecondParsedLine(string tempRoot)
    {
        string path = WriteLog(
            tempRoot,
            "display-parser-search-second-line.log",
            "{\"Level\":\"Info\",\"Message\":\"ready\"}\r\n{\"Level\":\"Error\",\"Message\":\"failed\"}\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{upper:Level}\n{Message}"));

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("failed", UseRegex: false, IgnoreCase: false),
            parser);

        reader.ReadFromPercentage(0d, 3);
        FilteredLogRecordSource columns = reader;
        AssertSequence("display parser search second line rows", reader.CurrentDisplayTexts, "failed");
        AssertSequence("display parser search second line cells", columns.CurrentCells[0], "2", "failed");

        LogRecordKey key = reader.CurrentRecords[0].Key;
        AssertEqual("display parser search second line key", key.ExplicitRowIndex, 1);
        AssertEqual(
            "display parser search second line ordinal",
            ((FilteredLogRecordSource)reader).TryGetRecordOrdinal(key, out long rowOrdinal),
            true);
        AssertEqual("display parser search second line ordinal value", rowOrdinal, 0L);

        AssertSelectedRows("display parser search second line selection", ReadAllRecords(reader), "failed");
    }

    private static void RunSearchCapturesOnlyMatchingParsedLine(string tempRoot)
    {
        string path = WriteLog(tempRoot, "display-parser-search-multiline-captures.log", "{\"Level\":\"Error\",\"Message\":\"failed\"}\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{upper:Level}\n{Message}"));

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(?<message>failed)", UseRegex: true, IgnoreCase: false),
            parser);

        reader.ReadFromPercentage(0d, 3);
        FilteredLogRecordSource columns = reader;
        AssertSequence("display parser multiline capture headers", columns.ColumnHeaders, "#", "Text", "message");
        AssertSequence("display parser multiline capture cells", columns.CurrentCells[0], "1", "failed", "failed");

        using FilteredLogRecordSource crossLineReader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("ERROR\\nfailed", UseRegex: true, IgnoreCase: false),
            parser);
        AssertEqual("display parser regex does not cross parsed lines", crossLineReader.MatchedLineCount, 0L);
    }

    private static void RunSearchDoesNotWrapLongParsedLine(string tempRoot)
    {
        string first = new('x', LongLineChunkChars + 5);
        string path = WriteLog(
            tempRoot,
            "display-parser-search-long-explicit-lines.log",
            "{\"First\":\"" + first + "\",\"Second\":\"tail\"}\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{First}\n{Second}"));

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("tail", UseRegex: false, IgnoreCase: false),
            parser);

        reader.ReadFromPercentage(0d, 3);
        AssertSequence("display parser search long explicit rows", reader.CurrentDisplayTexts, "tail");
        AssertSelectedRows("display parser search long explicit selection", ReadAllRecords(reader), "tail");
    }

    private static void RunSearchReturnsEachMatchingParsedLine(string tempRoot)
    {
        string path = WriteLog(
            tempRoot,
            "display-parser-search-multiple-explicit-lines.log",
            "{\"First\":\"MATCH one\",\"Second\":\"skip\",\"Third\":\"MATCH two\"}\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{First}\n{Second}\n{Third}"));

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("MATCH", UseRegex: false, IgnoreCase: false),
            parser);
        reader.ReadFromPercentage(0d, 3);

        AssertSequence("display parser multiple matching rows", reader.CurrentDisplayTexts, "MATCH one", "MATCH two");
        LogRecordKey firstKey = reader.CurrentRecords[0].Key;
        LogRecordKey secondKey = reader.CurrentRecords[1].Key;
        AssertEqual("display parser multiple first segment", firstKey.ExplicitRowIndex, 0);
        AssertEqual("display parser multiple second segment", secondKey.ExplicitRowIndex, 2);
        AssertEqual("display parser multiple first ordinal", ((FilteredLogRecordSource)reader).TryGetRecordOrdinal(firstKey, out long firstOrdinal), true);
        AssertEqual("display parser multiple first ordinal value", firstOrdinal, 0L);
        AssertEqual("display parser multiple second ordinal", ((FilteredLogRecordSource)reader).TryGetRecordOrdinal(secondKey, out long secondOrdinal), true);
        AssertEqual("display parser multiple second ordinal value", secondOrdinal, 1L);
    }

    private static void RunSearchInvertFiltersParsedLines(string tempRoot)
    {
        string path = WriteLog(tempRoot, "display-parser-search-invert-lines.log", "{\"Level\":\"ERROR\",\"Message\":\"failed\"}\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{Level}\n{Message}"));

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("failed", UseRegex: false, IgnoreCase: false, InvertMatch: true),
            parser);
        reader.ReadFromPercentage(0d, 3);

        AssertSequence("display parser invert rows", reader.CurrentDisplayTexts, "ERROR");
    }

    private static void RunCascadedSearchDoesNotCrossParsedLines(string tempRoot)
    {
        string path = WriteLog(tempRoot, "display-parser-cascade-lines.log", "{\"Level\":\"ERROR\",\"Message\":\"failed\"}\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{Level}\n{Message}"));
        SearchOptions[] options =
        {
            new("ERROR", UseRegex: false, IgnoreCase: false),
            new("failed", UseRegex: false, IgnoreCase: false)
        };

        FilteredLogRecordSource[] readers = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, options, parser);
        try
        {
            AssertSequence("display parser cascade first rows", readers[0].CurrentDisplayTexts, "ERROR");
            AssertEqual("display parser cascade second count", readers[1].MatchedLineCount, 0L);
        }
        finally
        {
            DisposeReaders(readers);
        }
    }

    private static void RunChangedCascadedSearchUsesMatchedParsedLine(string tempRoot)
    {
        string path = WriteLog(tempRoot, "display-parser-changed-cascade-lines.log", "{\"Level\":\"ERROR\",\"Message\":\"failed\"}\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{Level}\n{Message}"));
        SearchOptions[] initialOptions =
        {
            new("ERROR", UseRegex: false, IgnoreCase: false),
            new("ERROR", UseRegex: false, IgnoreCase: false)
        };
        SearchOptions[] changedOptions =
        {
            new("ERROR", UseRegex: false, IgnoreCase: false),
            new("failed", UseRegex: false, IgnoreCase: false)
        };

        FilteredLogRecordSource[] initial = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, initialOptions, parser);
        try
        {
            StagedSearchProgressUpdate finalUpdate = default;
            LogSearchBuilder.BuildChangedStagedFilteredReadersIncremental(
                initial,
                changedStageIndex: 1,
                changedOptions,
                new[] { 3, 3 },
                update => finalUpdate = update,
                CancellationToken.None,
                parser);

            using FilteredLogRecordSource changed = finalUpdate.Readers[1] ?? throw new InvalidOperationException("Changed parser cascade reader missing.");
            AssertEqual("display parser changed cascade second count", changed.MatchedLineCount, 0L);
        }
        finally
        {
            DisposeReaders(initial);
        }
    }

    private static void RunAppendSearchKeepsMatchedParsedLine(string tempRoot)
    {
        string path = WriteLog(tempRoot, "display-parser-append-lines.log", "{\"Level\":\"INFO\",\"Message\":\"ready\"}\r\n");
        DisplayParserRule parser = ParserRule(JsonStage("{Level}\n{Message}"));
        SearchOptions options = new("failed", UseRegex: false, IgnoreCase: false);
        using FilteredLogRecordSource initial = LogSearchBuilder.BuildFilteredReader(path, Encoding.UTF8, dataOffset: 0, options, parser);
        File.AppendAllText(path, "{\"Level\":\"ERROR\",\"Message\":\"failed\"}\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        using FilteredLogRecordSource appended = BuildAppendedReader(initial, options, parser);
        appended.ReadFromPercentage(0d, 3);

        AssertSequence("display parser append matching row", appended.CurrentDisplayTexts, "failed");
        AssertEqual("display parser append matching segment", appended.CurrentRecords[0].Key.ExplicitRowIndex, 1);
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
        string path = WriteLog(tempRoot, "nonbacktracking-leading-dotstar-no-match.log", new string('a', LongLineChunkChars * 12) + "\r\n");

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("(.*ERROR)", UseRegex: true, IgnoreCase: false));

        reader.ReadFromPercentage(0d, 10);
        AssertEqual("nonbacktracking leading dotstar no-match count", reader.MatchedLineCount, 0L);
    }

    private static void RunNonBacktrackingRegexLeadingDotStarMatchesLongLine(string tempRoot)
    {
        string line = new string('a', LongLineChunkChars * 6) + "ERROR" + new string('b', LongLineChunkChars * 6);
        string path = WriteLog(tempRoot, "nonbacktracking-leading-dotstar-match.log", line + "\r\n");

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
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

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(path, Encoding.UTF8, dataOffset: 0, options);
        reader.ReadFromPercentage(0d, 10);

        AssertEqual("cascade literal count", reader.MatchedLineCount, 1L);
        AssertSequence("cascade literal rows", reader.CurrentDisplayTexts, "alpha beta");
        AssertSequence("cascade literal cells", ((FilteredLogRecordSource)reader).CurrentCells[0], "2", "alpha beta");
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

        FilteredLogRecordSource[] initialReaders = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, initialOptions);
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
            using FilteredLogRecordSource changedReader = finalUpdate.Readers[1] ?? throw new InvalidOperationException("changed cascade reader missing.");
            changedReader.ReadFromPercentage(0d, 10);

            AssertSequence("changed cascade rows", changedReader.CurrentDisplayTexts, "ERROR user=bob");
        }
        finally
        {
            foreach (FilteredLogRecordSource reader in initialReaders)
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

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(path, Encoding.UTF8, dataOffset: 0, options);
        reader.ReadFromPercentage(0d, 10);

        AssertEqual("cascade invert count", reader.MatchedLineCount, 1L);
        AssertSequence("cascade invert rows", reader.CurrentDisplayTexts, "alpha keep");
        AssertSequence("cascade invert cells", ((FilteredLogRecordSource)reader).CurrentCells[0], "1", "alpha keep");
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

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(path, Encoding.UTF8, dataOffset: 0, options);
        reader.ReadFromPercentage(0d, 10);
        FilteredLogRecordSource columns = reader;

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

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(path, Encoding.UTF8, dataOffset: 0, options);
        reader.ReadFromPercentage(0d, 10);
        FilteredLogRecordSource columns = reader;

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

        FilteredLogRecordSource[] readers = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, options);
        try
        {
            AssertEqual("staged literal reader count", readers.Length, 2);
            readers[0].ReadFromPercentage(0d, 10);
            readers[1].ReadFromPercentage(0d, 10);

            AssertEqual("staged literal first count", readers[0].MatchedLineCount, 3L);
            AssertSequence("staged literal first rows", readers[0].CurrentDisplayTexts, "alpha", "alpha beta", "alpha beta gamma");
            AssertSequence("staged literal first cells", ((FilteredLogRecordSource)readers[0]).CurrentCells[1], "2", "alpha beta");

            AssertEqual("staged literal second count", readers[1].MatchedLineCount, 2L);
            AssertSequence("staged literal second rows", readers[1].CurrentDisplayTexts, "alpha beta", "alpha beta gamma");
            AssertSequence("staged literal second cells", ((FilteredLogRecordSource)readers[1]).CurrentCells[0], "2", "alpha beta");
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

        FilteredLogRecordSource[] readers = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, options);
        try
        {
            AssertEqual("staged capture reader count", readers.Length, 2);
            readers[0].ReadFromPercentage(0d, 10);
            readers[1].ReadFromPercentage(0d, 10);

            FilteredLogRecordSource firstColumns = readers[0];
            AssertSequence("staged capture first headers", firstColumns.ColumnHeaders, "#", "Text", "0", "1");
            AssertSequence("staged capture first cells", firstColumns.CurrentCells[0], "1", "aaabccc code-42", "aaa", "ccc");

            FilteredLogRecordSource secondColumns = readers[1];
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

        FilteredLogRecordSource[] readers = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, options);
        try
        {
            AssertEqual("staged named capture reader count", readers.Length, 2);
            readers[0].ReadFromPercentage(0d, 10);
            readers[1].ReadFromPercentage(0d, 10);

            FilteredLogRecordSource firstColumns = readers[0];
            AssertSequence("staged named capture first headers", firstColumns.ColumnHeaders, "#", "Text", "code");
            AssertSequence("staged named capture first cells", firstColumns.CurrentCells[0], "1", "code-42 user-ian", "42");
            AssertSequence("staged named capture first second cells", firstColumns.CurrentCells[1], "2", "code-7 user-ana", "7");

            FilteredLogRecordSource secondColumns = readers[1];
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

        FilteredLogRecordSource[] initial = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, options);
        try
        {
            File.AppendAllText(path, "new alpha\r\nnew alpha beta\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            FilteredLogRecordSource[] appended = BuildAppendedStagedReaders(initial, options);
            try
            {
                appended[0].ReadFromPercentage(0d, 10);
                appended[1].ReadFromPercentage(0d, 10);

                AssertEqual("append staged first count", appended[0].MatchedLineCount, 4L);
                AssertSequence("append staged first rows", appended[0].CurrentDisplayTexts, "alpha beta", "alpha plain", "new alpha", "new alpha beta");
                AssertSequence("append staged first final cells", ((FilteredLogRecordSource)appended[0]).CurrentCells[3], "4", "new alpha beta");

                AssertEqual("append staged second count", appended[1].MatchedLineCount, 2L);
                AssertSequence("append staged second rows", appended[1].CurrentDisplayTexts, "alpha beta", "new alpha beta");
                AssertSequence("append staged second final cells", ((FilteredLogRecordSource)appended[1]).CurrentCells[1], "4", "new alpha beta");
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

    private static void RunAppendStagedSearchCompletesCascadedParserRecord(string tempRoot)
    {
        string path = WriteLog(
            tempRoot,
            "append-staged-parser-combined.log",
            "a[0]: {\"Level\":\"Inf\r\n" +
            "a[1]: o\",\"Message\":\"run\r\n");
        SearchOptions[] options =
        {
            new("INFO", UseRegex: false, IgnoreCase: false),
            new("running", UseRegex: false, IgnoreCase: false)
        };
        DisplayParserRule parser = ParserRule(
            RegexStage(@": (?<json>.*)", "{json}"),
            JsonStage("{upper:Level} - {Message}"));

        FilteredLogRecordSource[] initial = LogSearchBuilder.BuildStagedFilteredReaders(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            options,
            parser);
        try
        {
            File.AppendAllText(path, "a[2]: ning\"}\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            FilteredLogRecordSource[] appended = BuildAppendedStagedReaders(initial, options, parser);
            try
            {
                appended[0].ReadFromPercentage(0d, 10);
                appended[1].ReadFromPercentage(0d, 10);
                AssertSequence("append staged parser combined first rows", appended[0].CurrentDisplayTexts, "INFO - running");
                AssertSequence("append staged parser combined second rows", appended[1].CurrentDisplayTexts, "INFO - running");
                AssertSequence(
                    "append staged parser combined cells",
                    ((FilteredLogRecordSource)appended[1]).CurrentCells[0],
                    "1",
                    "INFO - running");
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

        using FilteredLogRecordSource initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            options);

        File.AppendAllText(path, "new alpha\r\nnew alpha beta\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using FilteredLogRecordSource appended = BuildAppendedReader(initial, options);
        appended.ReadFromPercentage(0d, 10);

        AssertEqual("append cascade count", appended.MatchedLineCount, 2L);
        AssertSequence("append cascade rows", appended.CurrentDisplayTexts, "alpha beta", "new alpha beta");
        FilteredLogRecordSource columns = appended;
        AssertSequence("append cascade first cells", columns.CurrentCells[0], "1", "alpha beta");
        AssertSequence("append cascade second cells", columns.CurrentCells[1], "4", "new alpha beta");
    }

    private static void RunAppendSearchAddsMatches(string tempRoot)
    {
        string path = WriteLog(tempRoot, "append-search-match.log", "alpha\r\nplain\r\n");

        using FilteredLogRecordSource initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));

        File.AppendAllText(path, "new alpha\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using FilteredLogRecordSource appended = BuildAppendedReader(initial, new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));
        appended.ReadFromPercentage(0d, 10);

        AssertEqual("append match count", appended.MatchedLineCount, 2L);
        AssertSequence("append match rows", appended.CurrentDisplayTexts, "alpha", "new alpha");
        FilteredLogRecordSource columns = appended;
        AssertSequence("append match first cells", columns.CurrentCells[0], "1", "alpha");
        AssertSequence("append match second cells", columns.CurrentCells[1], "3", "new alpha");
    }

    private static void RunAppendSearchWithoutMatchKeepsCount(string tempRoot)
    {
        string path = WriteLog(tempRoot, "append-search-no-match.log", "alpha\r\nplain\r\n");

        using FilteredLogRecordSource initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));

        File.AppendAllText(path, "new plain\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using FilteredLogRecordSource appended = BuildAppendedReader(initial, new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));
        appended.ReadFromPercentage(0d, 10);

        AssertEqual("append no-match count", appended.MatchedLineCount, 1L);
        AssertSequence("append no-match rows", appended.CurrentDisplayTexts, "alpha");
    }

    private static void RunAppendSearchRescansPartialLastLine(string tempRoot)
    {
        string path = WriteLog(tempRoot, "append-search-partial.log", "prefix alp");

        using FilteredLogRecordSource initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));

        File.AppendAllText(path, "ha\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using FilteredLogRecordSource appended = BuildAppendedReader(initial, new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));
        appended.ReadFromPercentage(0d, 10);

        AssertEqual("append partial count", appended.MatchedLineCount, 1L);
        AssertSequence("append partial rows", appended.CurrentDisplayTexts, "prefix alpha");
        AssertSequence("append partial cells", ((FilteredLogRecordSource)appended).CurrentCells[0], "1", "prefix alpha");
    }

    private static void RunAppendSearchPreservesRegexCaptureGroups(string tempRoot)
    {
        string path = WriteLog(tempRoot, "append-search-captures.log", "aabcc\r\nplain\r\n");
        SearchOptions options = new("(a+)b(c+)", UseRegex: true, IgnoreCase: false);

        using FilteredLogRecordSource initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            options);

        File.AppendAllText(path, "aaabccc\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using FilteredLogRecordSource appended = BuildAppendedReader(initial, options);
        appended.ReadFromPercentage(0d, 10);
        FilteredLogRecordSource columns = appended;

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

        using FilteredLogRecordSource initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            options);

        File.AppendAllText(path, "code-7\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using FilteredLogRecordSource appended = BuildAppendedReader(initial, options);
        appended.ReadFromPercentage(0d, 10);
        FilteredLogRecordSource columns = appended;

        AssertSequence("append named capture headers", columns.ColumnHeaders, "#", "Text", "code");
        AssertEqual("append named capture row count", columns.CurrentCells.Count, 2);
        AssertSequence("append named capture first cells", columns.CurrentCells[0], "1", "code-42", "42");
        AssertSequence("append named capture second cells", columns.CurrentCells[1], "3", "code-7", "7");
    }

    private static void RunAppendSearchInvertMatch(string tempRoot)
    {
        string path = WriteLog(tempRoot, "append-search-invert.log", "alpha\r\nplain\r\n");
        SearchOptions options = new("alpha", UseRegex: false, IgnoreCase: false, InvertMatch: true);

        using FilteredLogRecordSource initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            options);

        File.AppendAllText(path, "beta\r\nalpha again\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using FilteredLogRecordSource appended = BuildAppendedReader(initial, options);
        appended.ReadFromPercentage(0d, 10);

        AssertEqual("append invert count", appended.MatchedLineCount, 2L);
        AssertSequence("append invert rows", appended.CurrentDisplayTexts, "plain", "beta");
        FilteredLogRecordSource columns = appended;
        AssertSequence("append invert first cells", columns.CurrentCells[0], "2", "plain");
        AssertSequence("append invert second cells", columns.CurrentCells[1], "3", "beta");
    }

    private static void RunAppendSearchStalesWhenEarlierLineGrows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "append-search-stale-earlier.log", "one\r\ntwo alpha\r\n");

        using FilteredLogRecordSource initial = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));

        File.WriteAllText(path, "one extended\r\ntwo alpha\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        AssertThrows<FilteredLineStaleException>(
            "append search stale earlier line grows",
            () =>
            {
                using FilteredLogRecordSource appended = BuildAppendedReader(initial, new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));
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
        FilteredLogRecordSource[] initial = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, options);
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
        FilteredLogRecordSource[] pausedReaders = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, options);
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

        FilteredLogRecordSource? resumed = null;
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
            AssertSequence("paused resume rows", resumed.CurrentDisplayTexts, "bravo-0", "alpha-2", "alpha-3");
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
        FilteredLogRecordSource? partialReader = null;
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
                    update.Readers[0] is FilteredLogRecordSource reader)
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

        FilteredLogRecordSource? resumed = null;
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
            AssertSequence("partial resume rows", resumed.CurrentDisplayTexts, "alpha-0", "alpha-2");
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
        FilteredLogRecordSource[] paused = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, options);
        try
        {
            long processedOffset = new FileInfo(path).Length;
            File.WriteAllText(path, replacement, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            FilteredLogRecordSource[] resumed = ResumeStagedReaders(paused, options, processedOffset, new FileInfo(path).Length);
            try
            {
                resumed[0].ReadFromPercentage(0d, 10);
                AssertEqual("resume incomplete count", resumed[0].MatchedLineCount, 2L);
                AssertSequence("resume incomplete rows", resumed[0].CurrentDisplayTexts, "alpha-0", "alpha-new");
                FilteredLogRecordSource columns = resumed[0];
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
        FilteredLogRecordSource[] paused = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, options);
        try
        {
            long processedOffset = new FileInfo(path).Length;
            File.AppendAllText(path, "alpha-2\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            FilteredLogRecordSource[] resumed = ResumeStagedReaders(paused, options, processedOffset, new FileInfo(path).Length);
            try
            {
                resumed[0].ReadFromPercentage(0d, 10);
                AssertEqual("resume line-break count", resumed[0].MatchedLineCount, 3L);
                AssertSequence("resume line-break rows", resumed[0].CurrentDisplayTexts, "alpha-0", "alpha-1", "alpha-2");
                FilteredLogRecordSource columns = resumed[0];
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
        FilteredLogRecordSource[] paused = LogSearchBuilder.BuildStagedFilteredReaders(path, Encoding.UTF8, dataOffset: 0, options);
        try
        {
            long processedOffset = new FileInfo(path).Length;
            File.WriteAllText(path, replacement, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            FilteredLogRecordSource[] resumed = ResumeStagedReaders(paused, options, processedOffset, new FileInfo(path).Length);
            try
            {
                resumed[0].ReadFromPercentage(0d, 10);
                resumed[1].ReadFromPercentage(0d, 10);
                AssertEqual("resume cascade first count", resumed[0].MatchedLineCount, 3L);
                AssertSequence("resume cascade first rows", resumed[0].CurrentDisplayTexts, "alpha beta", "alpha plain-tail", "alpha beta-new");
                AssertEqual("resume cascade second count", resumed[1].MatchedLineCount, 2L);
                AssertSequence("resume cascade second rows", resumed[1].CurrentDisplayTexts, "alpha beta", "alpha beta-new");
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

    private static FilteredLogRecordSource BuildAppendedReader(FilteredLogRecordSource initial, SearchOptions options)
    {
        return BuildAppendedReader(initial, new[] { options });
    }

    private static FilteredLogRecordSource BuildAppendedReader(FilteredLogRecordSource initial, SearchOptions options, DisplayParserRule displayParserRule)
    {
        return BuildAppendedReader(initial, new[] { options }, displayParserRule);
    }

    private static FilteredLogRecordSource BuildAppendedReader(FilteredLogRecordSource initial, IReadOnlyList<SearchOptions> options, DisplayParserRule? displayParserRule = null)
    {
        FilteredLogRecordSource? latest = null;
        LogSearchBuilder.BuildAppendedFilteredReaderIncremental(
            initial,
            options,
            new FileInfo(initial.SourceName).Length,
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

    private static FilteredLogRecordSource[] BuildAppendedStagedReaders(
        IReadOnlyList<FilteredLogRecordSource> initial,
        IReadOnlyList<SearchOptions> options,
        DisplayParserRule? displayParserRule = null)
    {
        FilteredLogRecordSource?[] latest = new FilteredLogRecordSource?[initial.Count];
        LogSearchBuilder.BuildAppendedStagedFilteredReadersIncremental(
            initial,
            options,
            new FileInfo(initial[0].SourceName).Length,
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
            CancellationToken.None,
            displayParserRule);

        FilteredLogRecordSource[] readers = new FilteredLogRecordSource[latest.Length];
        for (int i = 0; i < latest.Length; i++)
        {
            readers[i] = latest[i] ?? throw new InvalidOperationException("Append staged search did not publish every reader.");
        }

        return readers;
    }

    private static FilteredLogRecordSource[] ResumeStagedReaders(IReadOnlyList<FilteredLogRecordSource> paused, IReadOnlyList<SearchOptions> options, long processedOffset, long newFileSize)
    {
        FilteredLogRecordSource?[] latest = new FilteredLogRecordSource?[paused.Count];
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

        FilteredLogRecordSource[] readers = new FilteredLogRecordSource[latest.Length];
        for (int i = 0; i < latest.Length; i++)
        {
            readers[i] = latest[i] ?? throw new InvalidOperationException("Resume staged search did not publish every reader.");
        }

        return readers;
    }

    private static void RunLogRecordSourceReusesSlidingWindow(string tempRoot)
    {
        string path = WriteLog(
            tempRoot,
            "record-source-sliding-window.log",
            "{\"Value\":\"line-0\"}\r\n" +
            "{\"Value\":\"line-1\"}\r\n" +
            "{\"Value\":\"line-2\"}\r\n" +
            "{\"Value\":\"line-3\"}\r\n" +
            "{\"Value\":\"line-4\"}\r\n" +
            "{\"Value\":\"line-5\"}\r\n");
        using LogRecordSource reader = new(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            ParserRule(JsonStage("{Value}")));
        reader.ReadFromPercentage(0d, 4);
        LogViewportRecord[] initial = CopyRecords(reader.CurrentRecords);

        reader.ReadNextRecords(1);
        AssertEqual("record source forward first reused", ReferenceEquals(initial[1], reader.CurrentRecords[0]), true);
        AssertEqual("record source forward second reused", ReferenceEquals(initial[2], reader.CurrentRecords[1]), true);
        AssertEqual("record source forward third reused", ReferenceEquals(initial[3], reader.CurrentRecords[2]), true);
        LogViewportRecord[] afterForward = CopyRecords(reader.CurrentRecords);

        reader.ReadPreviousRecords(1);
        AssertEqual("record source backward first overlap reused", ReferenceEquals(afterForward[0], reader.CurrentRecords[1]), true);
        AssertEqual("record source backward second overlap reused", ReferenceEquals(afterForward[1], reader.CurrentRecords[2]), true);
        AssertEqual("record source backward third overlap reused", ReferenceEquals(afterForward[2], reader.CurrentRecords[3]), true);
        AssertSequence("record source backward rows", reader.CurrentDisplayTexts, "line-0", "line-1", "line-2", "line-3");
    }

    private static void RunFilteredLogRecordSourceReusesSlidingWindow(string tempRoot)
    {
        string path = WriteLog(
            tempRoot,
            "filtered-source-sliding-window.log",
            "match-0\r\nmatch-1\r\nmatch-2\r\nmatch-3\r\nmatch-4\r\nmatch-5\r\n");
        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("match", UseRegex: false, IgnoreCase: false));
        reader.ReadFromPercentage(0d, 4);
        LogViewportRecord[] initial = CopyRecords(reader.CurrentRecords);

        reader.ReadNextRecords(1);
        AssertEqual("filtered source forward first reused", ReferenceEquals(initial[1], reader.CurrentRecords[0]), true);
        AssertEqual("filtered source forward second reused", ReferenceEquals(initial[2], reader.CurrentRecords[1]), true);
        AssertEqual("filtered source forward third reused", ReferenceEquals(initial[3], reader.CurrentRecords[2]), true);
        LogViewportRecord[] afterForward = CopyRecords(reader.CurrentRecords);

        reader.ReadPreviousRecords(1);
        AssertEqual("filtered source backward first overlap reused", ReferenceEquals(afterForward[0], reader.CurrentRecords[1]), true);
        AssertEqual("filtered source backward second overlap reused", ReferenceEquals(afterForward[1], reader.CurrentRecords[2]), true);
        AssertEqual("filtered source backward third overlap reused", ReferenceEquals(afterForward[2], reader.CurrentRecords[3]), true);
        AssertSequence("filtered source backward rows", reader.CurrentDisplayTexts, "match-0", "match-1", "match-2", "match-3");
    }

    private static LogViewportRecord[] CopyRecords(IReadOnlyList<LogViewportRecord> records)
    {
        var copy = new LogViewportRecord[records.Count];
        for (int i = 0; i < records.Count; i++)
        {
            copy[i] = records[i];
        }

        return copy;
    }

    private static void RunPageUpNearStartClampsToTop(string tempRoot)
    {
        string path = WriteLog(tempRoot, "page-up-near-start.log", "line-0\r\nline-1\r\nline-2\r\nline-3\r\nline-4\r\n");

        using LogRecordSource reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadNextRecords(3);
        reader.ReadNextRecords(1);
        reader.ReadPreviousRecords(3);

        AssertSequence("page up near start", reader.CurrentDisplayTexts, "line-0", "line-1", "line-2");
        AssertEqual("page up near start offset", reader.TopOffset, 0L);
    }

    private static void RunRefreshTailAtEndShowsAppendedRows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "tail-at-end.log", "line-0\r\nline-1\r\n");

        using LogRecordSource reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(100d, 2);
        File.AppendAllText(path, "line-2\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshTail(2);

        AssertSequence("tail at end rows", reader.CurrentDisplayTexts, "line-1", "line-2");
    }

    private static void RunRefreshTailAtEndReloadsSameSizeChange(string tempRoot)
    {
        string path = WriteLog(tempRoot, "tail-same-size-change.log", "line-0\r\nline-1\r\nline-2\r\n");

        using LogRecordSource reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(100d, 2);
        File.WriteAllText(path, "line-0\r\nline-1\r\nLINE-2\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshTail(2);

        AssertSequence("tail same-size change rows", reader.CurrentDisplayTexts, "line-1", "LINE-2");
    }

    private static void RunReloadAfterFileChangeSameSizeReloadsCurrentViewport(string tempRoot)
    {
        string path = WriteLog(tempRoot, "reload-same-size-current.log", "line-0\r\nline-1\r\nline-2\r\n");

        using LogRecordSource reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(0d, 2);
        File.WriteAllText(path, "LINE-0\r\nline-1\r\nline-2\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(2);

        AssertSequence("reload same-size current rows", reader.CurrentDisplayTexts, "LINE-0", "line-1");
    }

    private static void RunReloadAfterFileChangePreservesViewportPosition(string tempRoot)
    {
        string path = WriteLog(tempRoot, "reload-preserve-position.log", "line-0\r\nline-1\r\nline-2\r\nline-3\r\nline-4\r\n");

        using LogRecordSource reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromOffset(16L, 2);
        File.WriteAllText(path, "LINE-0\r\nline-1\r\nline-2\r\nLINE-3\r\nline-4\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(2);

        AssertEqual("reload preserve position top offset", reader.TopOffset, 16L);
        AssertSequence("reload preserve position rows", reader.CurrentDisplayTexts, "line-2", "LINE-3");
    }

    private static void RunFilteredReloadAfterFileChangeSameSizeReloadsCurrentViewport(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-reload-same-size.log", "alpha\r\nplain\r\n");

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));
        reader.ReadFromPercentage(0d, 10);

        File.WriteAllText(path, "bravo\r\nplain\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(10);

        AssertSequence("filtered reload same-size rows", reader.CurrentDisplayTexts, "bravo");
        AssertSequence("filtered reload same-size cells", ((FilteredLogRecordSource)reader).CurrentCells[0], "1", "bravo");
    }

    private static void RunFilteredReloadAfterFileChangeStalesWhenLineBoundaryChanges(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-reload-stale-boundary.log", "alpha\r\nplain\r\n");

        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
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

        using LogRecordSource reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(0d, 20);
        AssertEqual("tail small initial starts at end", reader.IsAtEnd, true);
        File.AppendAllText(path, "line-2\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshTail(20);

        AssertSequence("tail small initial rows", reader.CurrentDisplayTexts, "line-0", "line-1", "line-2");
        AssertEqual("tail small initial at end", reader.IsAtEnd, true);
    }

    private static void RunRefreshFileSizeAwayFromEndLetsJumpEndSeeAppendedRows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "tail-away-then-end.log", "line-0\r\nline-1\r\nline-2\r\nline-3\r\n");

        using LogRecordSource reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(0d, 2);
        File.AppendAllText(path, "line-4\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        AssertEqual("tail away file size changed", reader.RefreshFileSize(), true);
        reader.ReadFromPercentage(100d, 2);

        AssertSequence("tail away then end rows", reader.CurrentDisplayTexts, "line-3", "line-4");
        AssertEqual("tail away then end at end", reader.IsAtEnd, true);
    }

    private static void RunRefreshTailAwayFromEndDoesNotMove(string tempRoot)
    {
        string path = WriteLog(tempRoot, "tail-away-from-end.log", "line-0\r\nline-1\r\nline-2\r\nline-3\r\n");

        using LogRecordSource reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(0d, 2);
        File.AppendAllText(path, "line-4\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshTail(2);

        AssertSequence("tail away rows", reader.CurrentDisplayTexts, "line-0", "line-1");
    }

    private static void RunRefreshTailAfterTruncateReloadsFromStart(string tempRoot)
    {
        string path = WriteLog(tempRoot, "tail-truncate.log", "line-0\r\nline-1\r\nline-2\r\nline-3\r\n");

        using LogRecordSource reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(100d, 2);
        File.WriteAllText(path, "new-0\r\nnew-1\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshTail(2);

        AssertSequence("tail truncate rows", reader.CurrentDisplayTexts, "new-0", "new-1");
    }

    private static void RunRefreshTailAfterTruncateToEmptyClearsRows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "tail-truncate-empty.log", "line-0\r\nline-1\r\nline-2\r\n");

        using LogRecordSource reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(100d, 2);
        long confirmedSize = reader.FileSize;
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _ = reader.IsAtEnd;
        reader.RefreshTail(2);

        AssertEqual("tail truncate empty count", reader.CurrentDisplayTexts.Count, 0);
        AssertEqual("tail truncate empty size", reader.FileSize, 0L);
        File.WriteAllText(path, "line-0\r\nline-1\r\nline-2\r\nline-3\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        AssertEqual("tail truncate empty compares with confirmed size", reader.RefreshFileSize(out long previousSize, out long currentSize), true);
        AssertEqual("tail truncate empty previous confirmed size", previousSize, confirmedSize);
        AssertEqual("tail truncate empty current size", currentSize, new FileInfo(path).Length);
    }

    private static void RunObservedZeroRepeatedKeepsConfirmedSize(string tempRoot)
    {
        string path = WriteLog(tempRoot, "observed-zero-repeated.log", "line-0\r\nline-1\r\nline-2\r\n");

        using LogRecordSource reader = new(path, Encoding.UTF8, dataOffset: 0);
        long confirmedSize = reader.FileSize;
        reader.ReadFromPercentage(100d, 2);
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        AssertEqual("observed zero first refresh changed", reader.RefreshFileSize(out long firstPrevious, out long firstCurrent), true);
        AssertEqual("observed zero first previous confirmed", firstPrevious, confirmedSize);
        AssertEqual("observed zero first current", firstCurrent, 0L);
        AssertEqual("observed zero file size", reader.FileSize, 0L);
        AssertEqual("observed zero has content", reader.HasContent, false);
        AssertEqual("observed zero rows", reader.CurrentDisplayTexts.Count, 0);
        AssertEqual("observed zero repeated refresh unchanged", reader.RefreshFileSize(out long secondPrevious, out long secondCurrent), false);
        AssertEqual("observed zero repeated previous confirmed", secondPrevious, confirmedSize);
        AssertEqual("observed zero repeated current", secondCurrent, 0L);
        AssertEqual("observed zero repeated file size", reader.FileSize, 0L);
    }

    private static void RunObservedZeroThenLargerComparesWithConfirmedSize(string tempRoot)
    {
        string initial = "line-0\r\nline-1\r\n";
        string path = WriteLog(tempRoot, "observed-zero-larger.log", initial);

        using LogRecordSource reader = new(path, Encoding.UTF8, dataOffset: 0);
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

        using LogRecordSource reader = new(path, Encoding.UTF8, dataOffset: 0);
        long confirmedSize = reader.FileSize;
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshFileSize();
        File.WriteAllText(path, "short\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        AssertEqual("observed zero smaller refresh changed", reader.RefreshFileSize(out long previousSize, out long currentSize), true);
        AssertEqual("observed zero smaller previous confirmed", previousSize, confirmedSize);
        AssertEqual("observed zero smaller current size", currentSize, new FileInfo(path).Length);
        AssertEqual("observed zero smaller is truncate", currentSize < previousSize, true);
    }

    private static void RunObservedZeroThenSameSizeReloadsRecords(string tempRoot)
    {
        string initial = "line-0\r\nline-1\r\n";
        string replacement = "neww-0\r\nneww-1\r\n";
        string path = WriteLog(tempRoot, "observed-zero-same-size.log", initial);

        using LogRecordSource reader = new(path, Encoding.UTF8, dataOffset: 0);
        long confirmedSize = reader.FileSize;
        AssertEqual("observed zero same size setup", replacement.Length, initial.Length);
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshFileSize();
        File.WriteAllText(path, replacement, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        AssertEqual("observed zero same size refresh changed", reader.RefreshFileSize(out long previousSize, out long currentSize), true);
        AssertEqual("observed zero same size previous confirmed", previousSize, confirmedSize);
        AssertEqual("observed zero same size current size", currentSize, confirmedSize);
        reader.ReadFromPercentage(0d, 2);
        AssertSequence("observed zero same size rows", reader.CurrentDisplayTexts, "neww-0", "neww-1");
    }

    private static void RunObservedZeroPreservesViewportRowsInternally(string tempRoot)
    {
        string initial = "line-0\r\nline-1\r\n";
        string path = WriteLog(tempRoot, "observed-zero-preserve-viewport.log", initial);

        using LogRecordSource reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(0d, 2);
        AssertSequence("observed zero preserve setup rows", reader.CurrentDisplayTexts, "line-0", "line-1");
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshFileSize();
        AssertEqual("observed zero preserve hidden rows", reader.CurrentDisplayTexts.Count, 0);
        File.WriteAllText(path, initial, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshFileSize();

        AssertSequence("observed zero preserve restored rows", reader.CurrentDisplayTexts, "line-0", "line-1");
    }

    private static void RunObservedZeroRefreshTailPreservesViewportRowsInternally(string tempRoot)
    {
        string initial = "line-0\r\nline-1\r\n";
        string path = WriteLog(tempRoot, "observed-zero-refresh-tail-preserve-viewport.log", initial);

        using LogRecordSource reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(0d, 2);
        AssertSequence("observed zero refresh tail setup rows", reader.CurrentDisplayTexts, "line-0", "line-1");
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshTail(2);
        AssertEqual("observed zero refresh tail hidden rows", reader.CurrentDisplayTexts.Count, 0);
        File.WriteAllText(path, initial, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshFileSize();

        AssertSequence("observed zero refresh tail restored rows", reader.CurrentDisplayTexts, "line-0", "line-1");
    }

    private static void RunObservedZeroReloadPreservesViewportRowsInternally(string tempRoot)
    {
        string initial = "line-0\r\nline-1\r\n";
        string path = WriteLog(tempRoot, "observed-zero-reload-preserve-viewport.log", initial);

        using LogRecordSource reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(0d, 2);
        AssertSequence("observed zero reload setup rows", reader.CurrentDisplayTexts, "line-0", "line-1");
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(2);
        AssertEqual("observed zero reload hidden rows", reader.CurrentDisplayTexts.Count, 0);
        File.WriteAllText(path, initial, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshFileSize();

        AssertSequence("observed zero reload restored rows", reader.CurrentDisplayTexts, "line-0", "line-1");
    }

    private static void RunObservedZeroReloadFromEndFollowsRestoredTail(string tempRoot)
    {
        string initial = "line-0\r\nline-1\r\nline-2\r\n";
        string path = WriteLog(tempRoot, "observed-zero-reload-end-tail.log", initial);

        using LogRecordSource reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(100d, 2);
        AssertSequence("observed zero reload end setup rows", reader.CurrentDisplayTexts, "line-1", "line-2");
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(2);
        AssertEqual("observed zero reload end hidden rows", reader.CurrentDisplayTexts.Count, 0);
        File.WriteAllText(path, initial + "line-3\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(2);

        AssertSequence("observed zero reload end restored tail rows", reader.CurrentDisplayTexts, "line-2", "line-3");
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
            reader => reader.ReadNextRecords(1));
        AssertVisualObservedZeroNavigationPreservesRows(
            tempRoot,
            "read-previous",
            reader => reader.ReadPreviousRecords(1));
    }

    private static void AssertVisualObservedZeroNavigationPreservesRows(
        string tempRoot,
        string name,
        Action<LogRecordSource> action)
    {
        string initial = "line-0\r\nline-1\r\n";
        string path = WriteLog(tempRoot, "observed-zero-navigation-" + name + ".log", initial);

        using LogRecordSource reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(0d, 2);
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshFileSize();

        action(reader);
        AssertEqual("observed zero navigation " + name + " hidden rows", reader.CurrentDisplayTexts.Count, 0);
        File.WriteAllText(path, initial, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshFileSize();

        AssertSequence("observed zero navigation " + name + " restored rows", reader.CurrentDisplayTexts, "line-0", "line-1");
    }

    private static void RunFilteredObservedZeroRepeatedKeepsConfirmedSize(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-observed-zero-repeated.log", "line-0\r\nline-1\r\nline-2\r\n");
        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
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
        AssertEqual("filtered observed zero rows", reader.CurrentDisplayTexts.Count, 0);
        AssertEqual("filtered observed zero cells", reader.CurrentCells.Count, 0);
        reader.ReloadAfterFileChange(10);
        AssertEqual("filtered observed zero repeated file size", reader.FileSize, 0L);
        AssertEqual("filtered observed zero repeated confirmed size", reader.ConfirmedFileSize, confirmedSize);
    }

    private static void RunFilteredObservedZeroPreservesViewportRowsInternally(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-observed-zero-preserve-viewport.log", "line-0\r\nline-1\r\n");
        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("line", UseRegex: false, IgnoreCase: false));
        reader.ReadFromPercentage(0d, 10);
        AssertSequence("filtered observed zero preserve setup rows", reader.CurrentDisplayTexts, "line-0", "line-1");
        AssertEqual("filtered observed zero preserve setup cells", reader.CurrentCells.Count, 2);

        reader.MarkObservedZeroFileSize();
        AssertEqual("filtered observed zero preserve hidden rows", reader.CurrentDisplayTexts.Count, 0);
        AssertEqual("filtered observed zero preserve hidden cells", reader.CurrentCells.Count, 0);
        reader.ClearObservedZeroFileSize();

        AssertSequence("filtered observed zero preserve restored rows", reader.CurrentDisplayTexts, "line-0", "line-1");
        AssertEqual("filtered observed zero preserve restored cells", reader.CurrentCells.Count, 2);
    }

    private static void RunFilteredObservedZeroPreservesConfirmedEndState(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-observed-zero-confirmed-end.log", "line-0\r\nline-1\r\nline-2\r\nline-3\r\n");
        using FilteredLogRecordSource endReader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("line", UseRegex: false, IgnoreCase: false));
        endReader.ReadFromPercentage(100d, 2);
        AssertEqual("filtered observed zero end setup confirmed end", endReader.IsAtConfirmedEnd, true);
        endReader.MarkObservedZeroFileSize();
        AssertEqual("filtered observed zero end visual end", endReader.IsAtEnd, true);
        AssertEqual("filtered observed zero end confirmed end", endReader.IsAtConfirmedEnd, true);

        using FilteredLogRecordSource topReader = LogSearchBuilder.BuildFilteredReader(
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
        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("line", UseRegex: false, IgnoreCase: false));
        reader.ReadFromPercentage(0d, 2);
        AssertSequence("filtered observed zero reload away setup rows", reader.CurrentDisplayTexts, "line-0", "line-1");
        AssertEqual("filtered observed zero reload away setup confirmed end", reader.IsAtConfirmedEnd, false);
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(2);
        AssertEqual("filtered observed zero reload away hidden rows", reader.CurrentDisplayTexts.Count, 0);
        File.WriteAllText(path, initial + "line-4\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(2);

        AssertSequence("filtered observed zero reload away restored rows", reader.CurrentDisplayTexts, "line-0", "line-1");
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
            reader => reader.ReadFromRecordOrdinal(1, 10));
        AssertFilteredObservedZeroNavigationPreservesRows(
            tempRoot,
            "read-next",
            reader => reader.ReadNextRecords(1));
        AssertFilteredObservedZeroNavigationPreservesRows(
            tempRoot,
            "read-previous",
            reader => reader.ReadPreviousRecords(1));
    }

    private static void AssertFilteredObservedZeroNavigationPreservesRows(
        string tempRoot,
        string name,
        Action<FilteredLogRecordSource> action)
    {
        string path = WriteLog(tempRoot, "filtered-observed-zero-navigation-" + name + ".log", "line-0\r\nline-1\r\n");
        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("line", UseRegex: false, IgnoreCase: false));
        reader.ReadFromPercentage(0d, 10);
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(10);

        action(reader);
        AssertEqual("filtered observed zero navigation " + name + " hidden rows", reader.CurrentDisplayTexts.Count, 0);
        AssertEqual("filtered observed zero navigation " + name + " hidden cells", reader.CurrentCells.Count, 0);
        reader.ClearObservedZeroFileSize();

        AssertSequence("filtered observed zero navigation " + name + " restored rows", reader.CurrentDisplayTexts, "line-0", "line-1");
        AssertEqual("filtered observed zero navigation " + name + " restored cells", reader.CurrentCells.Count, 2);
    }

    private static void RunFilteredObservedZeroThenSameSizeReloadsRows(string tempRoot)
    {
        string initial = "line-0\r\nline-1\r\n";
        string replacement = "neww-0\r\nneww-1\r\n";
        string path = WriteLog(tempRoot, "filtered-observed-zero-same-size.log", initial);
        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
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
        AssertSequence("filtered observed zero same size rows", reader.CurrentDisplayTexts, "neww-0", "neww-1");
    }

    private static void RunFilteredObservedZeroThenSmallerKeepsConfirmedForStaleDecision(string tempRoot)
    {
        string path = WriteLog(tempRoot, "filtered-observed-zero-smaller.log", "line-0\r\nline-1\r\nline-2\r\n");
        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
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
        using FilteredLogRecordSource reader = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            dataOffset: 0,
            new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.ReloadAfterFileChange(10);
        File.WriteAllText(path, initial + "alpha-2\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        using FilteredLogRecordSource appended = BuildAppendedReader(reader, new SearchOptions("alpha", UseRegex: false, IgnoreCase: false));
        AssertEqual("filtered observed zero append match count", appended.MatchedLineCount, 3L);
        AssertSequence("filtered observed zero append rows", appended.CurrentDisplayTexts, "alpha-0", "alpha-1", "alpha-2");
    }

    private static void RunRefreshFileSizeAfterTruncateLetsJumpEndSeeAppendedRows(string tempRoot)
    {
        string path = WriteLog(tempRoot, "tail-truncate-append.log", "line-0\r\nline-1\r\nline-2\r\n");

        using LogRecordSource reader = new(path, Encoding.UTF8, dataOffset: 0);
        reader.ReadFromPercentage(100d, 2);
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        reader.RefreshTail(2);
        File.AppendAllText(path, "after-0\r\nafter-1\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        AssertEqual("tail truncate append file size changed", reader.RefreshFileSize(), true);
        reader.ReadFromPercentage(100d, 2);

        AssertSequence("tail truncate append rows", reader.CurrentDisplayTexts, "after-0", "after-1");
    }

    private static int[] CreateFilledArray(int count, int value)
    {
        int[] values = new int[count];
        Array.Fill(values, value);
        return values;
    }

    private static void DisposeReaders(IReadOnlyList<FilteredLogRecordSource> readers)
    {
        foreach (FilteredLogRecordSource reader in readers)
        {
            reader.Dispose();
        }
    }

    private static void DisposeNullableReaders(IReadOnlyList<FilteredLogRecordSource?> readers)
    {
        foreach (FilteredLogRecordSource? reader in readers)
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

    private static void RunMemoryContentSource()
    {
        const string text =
            "{\"Level\":\"Info\",\"Message\":\"início\"}\r\n" +
            "{\"Level\":\"Error\",\"Message\":\"alpha\"}\r\n" +
            "{\"Level\":\"Error\",\"Message\":\"beta\"}\r\n";
        byte[] encoded = CreateUtf8BomContent(text);
        byte[] ownedBuffer = new byte[encoded.Length + 32];
        encoded.CopyTo(ownedBuffer, 0);
        Array.Fill(ownedBuffer, (byte)0x7F, encoded.Length, ownedBuffer.Length - encoded.Length);
        LogContentSource content = LogContentSource.FromMemory("Pasted text", ownedBuffer, encoded.Length);
        AssertEqual("memory source is not file", content.IsFile, false);
        AssertEqual("memory source has no path", content.FilePath, null);
        AssertEqual("memory source uses valid buffer length", content.Length, (long)encoded.Length);

        using (Stream first = content.OpenRead())
        using (Stream second = content.OpenRead())
        {
            first.Position = 5;
            AssertEqual("memory streams have independent positions", second.Position, 0L);
        }

        DetectedEncodingInfo detected = LogEncodingDetector.DetectEncoding(content);
        AssertEqual("memory source encoding", detected.Kind, LogEncodingKind.Utf8);
        AssertEqual("memory source data offset", detected.DataOffset, 3L);

        DisplayParserRule parser = new()
        {
            Name = "memory-json",
            Stages = new List<DisplayParserStage>
            {
                new()
                {
                    Mode = DisplayParserMode.Json,
                    Rule = "{upper:Level} {Message}"
                }
            }
        };

        using (var source = new LogRecordSource(content, detected.Encoding, detected.DataOffset, parser))
        {
            AssertEqual("memory record source name", source.SourceName, "Pasted text");
            source.ReadFromPercentage(0d, 10);
            AssertSequence("memory parsed records", source.CurrentDisplayTexts, "INFO início", "ERROR alpha", "ERROR beta");
        }

        FilteredLogRecordSource[] readers = LogSearchBuilder.BuildStagedFilteredReaders(
            content,
            detected.Encoding,
            detected.DataOffset,
            new[]
            {
                new SearchOptions("ERROR", UseRegex: false, IgnoreCase: false),
                new SearchOptions("beta", UseRegex: false, IgnoreCase: false)
            },
            parser);
        try
        {
            AssertEqual("memory first search count", readers[0].MatchedLineCount, 2L);
            AssertEqual("memory cascaded search count", readers[1].MatchedLineCount, 1L);
            readers[1].ReadFromPercentage(0d, 1);
            AssertSequence("memory cascaded search text", readers[1].CurrentDisplayTexts, "ERROR beta");
            AssertEqual("memory result offset is after BOM", readers[1].TopOffset > detected.DataOffset, true);
        }
        finally
        {
            foreach (FilteredLogRecordSource reader in readers)
            {
                reader.Dispose();
            }
        }

        FilteredLogRecordSource?[]? incrementalReaders = null;
        LogSearchBuilder.BuildStagedFilteredReadersIncremental(
            content,
            detected.Encoding,
            detected.DataOffset,
            new[]
            {
                new SearchOptions("ERROR", UseRegex: false, IgnoreCase: false),
                new SearchOptions("beta", UseRegex: false, IgnoreCase: false)
            },
            new[] { 2, 2 },
            update =>
            {
                if (update.IsFinal)
                {
                    incrementalReaders = update.Readers;
                    return;
                }

                foreach (FilteredLogRecordSource? reader in update.Readers)
                {
                    reader?.Dispose();
                }
            },
            CancellationToken.None,
            parser);
        AssertEqual("memory incremental readers published", incrementalReaders is not null, true);
        try
        {
            AssertEqual("memory incremental first count", incrementalReaders![0]!.MatchedLineCount, 2L);
            AssertEqual("memory incremental cascaded count", incrementalReaders[1]!.MatchedLineCount, 1L);
        }
        finally
        {
            if (incrementalReaders is not null)
            {
                foreach (FilteredLogRecordSource? reader in incrementalReaders)
                {
                    reader?.Dispose();
                }
            }
        }
    }

    private static byte[] CreateUtf8BomContent(string text)
    {
        byte[] preamble = Encoding.UTF8.GetPreamble();
        byte[] payload = Encoding.UTF8.GetBytes(text);
        byte[] content = new byte[preamble.Length + payload.Length];
        preamble.CopyTo(content, 0);
        payload.CopyTo(content, preamble.Length);
        return content;
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

    private static void AssertSelectedRows(string name, IReadOnlyList<LogViewportRecord> actual, params string[] expected)
    {
        AssertEqual(name + " count", actual.Count, expected.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            AssertEqual(name + " [" + i + "]", actual[i].DisplayText, expected[i]);
        }
    }

    private static IReadOnlyList<LogViewportRecord> ReadAllRecords(ILogRecordSource source)
    {
        var records = new List<LogViewportRecord>();
        foreach (LogViewportRecord record in source.EnumerateRecords(null, null))
        {
            records.Add(record);
        }

        return records;
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
