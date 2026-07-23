using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

internal static class Program
{
    private static int Main()
    {
        string tempBase = Environment.GetEnvironmentVariable("LOGBLADE_TEST_TEMP") ?? Path.GetTempPath();
        string tempRoot = Path.Combine(tempBase, "logblade-front-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            Run("main projection", () => RunMainProjection(tempRoot));
            Run("memory projection", RunMemoryProjection);
            Run("pasted text serialization", RunPastedTextSerialization);
            Run("pasted text launch failure", RunPastedTextLaunchFailure);
            Run("pasted text transfer state", RunPastedTextTransferState);
            Run("combined parser", () => RunCombinedParserProjection(tempRoot));
            Run("search projection", () => RunSearchProjection(tempRoot));
            Run("record navigation", () => RunRecordNavigation(tempRoot));
            Run("batched navigation", RunBatchedNavigation);
            Run("mixed wrapped batched navigation", RunMixedWrappedBatchedNavigation);
            Run("large line scrollbar", () => RunLargeLineScrollbar(tempRoot));
            Run("parsed scrollbar", () => RunParsedScrollbar(tempRoot));
            Run("encoded scrollbar offsets", () => RunEncodedScrollbarOffsets(tempRoot));
            Run("selection", () => RunSelectionAcrossRecords(tempRoot));
            Run("zero", () => RunZeroProjection(tempRoot));
            Run("word boundary", () => RunWordAcrossSegmentBoundary(tempRoot));
            Run("word tokens", RunWordTokenSelection);
            Run("word drag snapping", RunWordDragSnapping);
            Run("non-word double click", RunNonWordDoubleClickSelection);
            Run("display text normalization", RunDisplayTextNormalization);
            Run("multiline edit normalization", RunMultilineEditNormalization);
            Run("clipboard headers", RunClipboardHeaders);
            Run("cascaded filter stage preview", RunCascadedFilterStagePreview);
            Run("parser processing equivalence", RunParserProcessingEquivalence);
            Run("search input visibility", RunSearchInputVisibility);
            Run("panel divider resize", RunPanelDividerResize);
            Run("viewport minimum height", RunViewportMinimumHeight);
            Run("undersized layout clipping", RunUndersizedLayoutClipping);
            Run("viewport focus restore", RunViewportFocusRestore);
            Run("output export", () => RunOutputExport(tempRoot));
            Run("window state store", () => RunWindowStateStore(tempRoot));
            Run("regex reference content", RunRegexReferenceContent);
            Console.WriteLine("Front smoke tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Front smoke tests failed.");
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static void Run(string name, Action action)
    {
        Console.WriteLine("Running " + name + "...");
        action();
    }

    private static void RunRegexReferenceContent()
    {
        string reference = RegexReferenceWindow.ReferenceText;
        AssertReferenceContains(reference, ".NET REGEX PATTERNS");
        AssertReferenceContains(reference, "This syntax belongs to .NET Regex.");
        AssertReferenceContains(reference, "LOGBLADE REGEX EXECUTION");
        AssertReferenceContains(reference, "These are LogBlade rules, not Regex syntax.");
        AssertReferenceContains(reference, "LOGBLADE OUTPUT TEMPLATES");
        AssertReferenceContains(reference, "This is LogBlade syntax, not Regex pattern syntax");
        AssertReferenceContains(reference, "$1");
        AssertReferenceContains(reference, "${State.Message}");
        AssertReferenceContains(reference, "${upper:name}");
        AssertReferenceContains(reference, @"\b");
        AssertReferenceContains(reference, @"\.");
        AssertReferenceContains(reference, @"\\");
        AssertReferenceContains(reference, "Anchors and alternation");
        AssertReferenceContains(reference, "Start of the current input");
        AssertReferenceContains(reference, "End of the current input");
        AssertReferenceContains(reference, "Complete parser example");
        AssertReferenceContains(reference, "Display template");
        AssertReferenceContains(reference, "Input             12:34:56 error Disk full");
        AssertReferenceContains(reference, "Result            12:34:56 [ERROR] Disk full");
        AssertReferenceContains(reference, "Regex Replace renders the template for every match.");
        AssertReferenceContains(reference, "Lookarounds and backreferences in patterns are not supported by NonBacktracking.");
        AssertReferenceContains(reference, "Displayed as result columns");
    }

    private static void AssertReferenceContains(string reference, string expected)
    {
        if (!reference.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Regex reference is missing: " + expected);
        }
    }

    private static void RunCascadedFilterStagePreview()
    {
        const string sample =
            "2126-04-21T13:00:00.000Z OUT 8=FIX.4.4|35=A|148=old|\r\n" +
            "2026-04-21T13:00:01.000Z IN 8=FIX.4.4|35=A|\r\n" +
            "2026-04-21T13:00:02.000Z OUT 8=FIX.4.4|35=0|369=0|\r\n" +
            "2026-04-21T13:00:02.000Z IN FIX4.4|8=FIX.4.4|35=B|148=Cross asset volumes remain elevated after macro releases|10=146|";
        var previousRule = new DisplayParserRule
        {
            Stages = new List<DisplayParserStage>
            {
                new()
                {
                    Mode = DisplayParserMode.Filter,
                    Rule = "21T13:00:02.000Z ",
                    UseRegex = true
                },
                new()
                {
                    Mode = DisplayParserMode.RegexReplace,
                    Rule = @"\|",
                    Template = "\r\n"
                }
            }
        };
        string secondFilterInput = DisplayParserEvaluator.EvaluateLinesOrOriginal(previousRule, sample);
        var secondFilter = new DisplayParserStage
        {
            Mode = DisplayParserMode.Filter,
            Rule = "148",
            UseRegex = true
        };

        string filtered = ParserStageEditorWindow.EvaluateStagePreview(
            secondFilterInput,
            secondFilter,
            hasPreviousFilter: true);
        AssertEqual(
            "cascaded filter preview",
            filtered,
            "148=Cross asset volumes remain elevated after macro releases");

        var replacement = new DisplayParserStage
        {
            Mode = DisplayParserMode.RegexReplace,
            Rule = "=",
            Template = "|"
        };
        string replaced = ParserStageEditorWindow.EvaluateStagePreview(
            filtered,
            replacement,
            hasPreviousFilter: true);
        AssertEqual(
            "stage after cascaded filter preview",
            replaced,
            "148|Cross asset volumes remain elevated after macro releases");
    }

    private static void RunParserProcessingEquivalence()
    {
        DisplayParserRule original = new()
        {
            Name = "Original",
            Sample = "sample-a",
            Stages = new List<DisplayParserStage>
            {
                new()
                {
                    Mode = DisplayParserMode.Regex,
                    Rule = "(?<value>.*)",
                    Template = "${value}",
                    UseRegex = true,
                    IgnoreCase = true,
                    InvertMatch = true
                },
                new()
                {
                    Mode = DisplayParserMode.Json,
                    Rule = "${Value}",
                    Template = "ignored-json-template",
                    IgnoreCase = true
                },
                new()
                {
                    Mode = DisplayParserMode.RegexReplace,
                    Rule = "=",
                    Template = "|"
                },
                new()
                {
                    Mode = DisplayParserMode.Filter,
                    Rule = "ERROR",
                    Template = "ignored-filter-template",
                    UseRegex = true,
                    IgnoreCase = false,
                    InvertMatch = false
                }
            }
        };

        DisplayParserRule equivalent = original.Clone();
        equivalent.Name = "Renamed";
        equivalent.Sample = "sample-b";
        equivalent.Stages[0].UseRegex = false;
        equivalent.Stages[0].IgnoreCase = false;
        equivalent.Stages[0].InvertMatch = false;
        equivalent.Stages[1].Template = "different-ignored-json-template";
        equivalent.Stages[1].UseRegex = true;
        equivalent.Stages[3].Template = "different-ignored-filter-template";
        AssertEqual(
            "parser processing ignores metadata and inactive properties",
            DisplayParserRuleProcessingComparer.AreEquivalent(original, equivalent),
            true);

        DisplayParserRule equivalentEmptyTemplate = original.Clone();
        equivalentEmptyTemplate.Stages[2].Template = null!;
        original.Stages[2].Template = string.Empty;
        AssertEqual(
            "parser processing normalizes empty output text",
            DisplayParserRuleProcessingComparer.AreEquivalent(original, equivalentEmptyTemplate),
            true);

        AssertParserProcessingDifferent(original, rule => rule.Stages[0].Rule = "different", "rule");
        AssertParserProcessingDifferent(original, rule => rule.Stages[0].Template = "different", "regex template");
        AssertParserProcessingDifferent(original, rule => rule.Stages[0].Mode = DisplayParserMode.RegexReplace, "mode");
        AssertParserProcessingDifferent(original, rule => rule.Stages[2].Template = "different", "regex replace template");
        AssertParserProcessingDifferent(original, rule => rule.Stages[3].UseRegex = false, "filter regex");
        AssertParserProcessingDifferent(original, rule => rule.Stages[3].IgnoreCase = true, "filter ignore case");
        AssertParserProcessingDifferent(original, rule => rule.Stages[3].InvertMatch = true, "filter invert match");
        AssertParserProcessingDifferent(original, rule => rule.Stages.RemoveAt(3), "stage count");

        DisplayParserRule reordered = original.Clone();
        (reordered.Stages[0], reordered.Stages[1]) = (reordered.Stages[1], reordered.Stages[0]);
        AssertEqual(
            "parser processing detects stage order",
            DisplayParserRuleProcessingComparer.AreEquivalent(original, reordered),
            false);
        AssertEqual(
            "parser processing detects null",
            DisplayParserRuleProcessingComparer.AreEquivalent(original, null),
            false);
        AssertEqual(
            "parser processing accepts matching null",
            DisplayParserRuleProcessingComparer.AreEquivalent(null, null),
            true);
    }

    private static void AssertParserProcessingDifferent(
        DisplayParserRule original,
        Action<DisplayParserRule> change,
        string name)
    {
        DisplayParserRule changed = original.Clone();
        change(changed);
        AssertEqual(
            "parser processing detects " + name,
            DisplayParserRuleProcessingComparer.AreEquivalent(original, changed),
            false);
    }

    private static void RunSearchInputVisibility()
    {
        AssertEqual(
            "main pane visible without active search",
            ViewerWindow.ShouldShowMainPaneForSearchInputs(0, firstSearchInputVisible: false),
            true);
        AssertEqual(
            "main pane hidden when first search input is hidden",
            ViewerWindow.ShouldShowMainPaneForSearchInputs(3, firstSearchInputVisible: false),
            false);
        AssertEqual(
            "main pane visible when first search input is shown",
            ViewerWindow.ShouldShowMainPaneForSearchInputs(3, firstSearchInputVisible: true),
            true);
        AssertEqual(
            "first result is controlled by second search input",
            ViewerWindow.GetInputControllerIndexForSearchResult(0, 3),
            1);
        AssertEqual(
            "second result is controlled by third search input",
            ViewerWindow.GetInputControllerIndexForSearchResult(1, 3),
            2);
        AssertEqual(
            "last active result is always visible",
            ViewerWindow.GetInputControllerIndexForSearchResult(2, 3),
            -1);
        AssertEqual(
            "single search result is always visible",
            ViewerWindow.GetInputControllerIndexForSearchResult(0, 1),
            -1);
        AssertEqual(
            "parser filters start collapsed",
            ViewerWindow.GetDefaultSearchInputVisibility(isParserFilter: true),
            false);
        AssertEqual(
            "manual searches start expanded",
            ViewerWindow.GetDefaultSearchInputVisibility(isParserFilter: false),
            true);
    }

    private static void RunViewportFocusRestore()
    {
        IntPtr mainHwnd = new(1);
        IntPtr paneHwnd = new(2);
        IntPtr otherControlHwnd = new(3);

        AssertEqual(
            "restore selected pane without a focused control",
            ViewerWindow.ShouldRestoreViewportFocus(false, false, true, true, paneHwnd, IntPtr.Zero, mainHwnd),
            true);
        AssertEqual(
            "restore selected pane when main window has focus",
            ViewerWindow.ShouldRestoreViewportFocus(false, false, true, true, paneHwnd, mainHwnd, mainHwnd),
            true);
        AssertEqual(
            "preserve another focused control",
            ViewerWindow.ShouldRestoreViewportFocus(false, false, true, true, paneHwnd, otherControlHwnd, mainHwnd),
            false);
        AssertEqual(
            "do not refocus an already focused pane",
            ViewerWindow.ShouldRestoreViewportFocus(false, false, true, true, paneHwnd, paneHwnd, mainHwnd),
            false);
        AssertEqual(
            "do not restore without selection",
            ViewerWindow.ShouldRestoreViewportFocus(false, false, true, false, paneHwnd, IntPtr.Zero, mainHwnd),
            false);
        AssertEqual(
            "do not restore a hidden pane",
            ViewerWindow.ShouldRestoreViewportFocus(false, false, false, true, paneHwnd, IntPtr.Zero, mainHwnd),
            false);
        AssertEqual(
            "do not restore a destroyed pane",
            ViewerWindow.ShouldRestoreViewportFocus(false, false, true, true, IntPtr.Zero, IntPtr.Zero, mainHwnd),
            false);
        AssertEqual(
            "do not restore while closing",
            ViewerWindow.ShouldRestoreViewportFocus(true, false, true, true, paneHwnd, IntPtr.Zero, mainHwnd),
            false);
        AssertEqual(
            "do not restore over a configuration window",
            ViewerWindow.ShouldRestoreViewportFocus(false, true, true, true, paneHwnd, IntPtr.Zero, mainHwnd),
            false);
    }

    private static void RunPanelDividerResize()
    {
        double[] startRatios = { 0.30d, 0.20d, 0.10d, 0.40d };
        AssertDividerPositions(
            "second divider moves left",
            ViewerWindow.ResizePanelRatiosAtDivider(startRatios, 1, 0.25d),
            0.15d,
            0.25d,
            0.40d);
        AssertDividerPositions(
            "second divider moves right",
            ViewerWindow.ResizePanelRatiosAtDivider(startRatios, 1, 0.75d),
            0.45d,
            0.75d,
            0.80d);
        AssertPanelRatios(
            "first divider preserves right proportions",
            ViewerWindow.ResizePanelRatiosAtDivider(new[] { 0.20d, 0.30d, 0.10d, 0.40d }, 0, 0.40d),
            0.40d,
            0.225d,
            0.075d,
            0.30d);
        AssertPanelRatios(
            "last divider preserves left proportions",
            ViewerWindow.ResizePanelRatiosAtDivider(new[] { 0.20d, 0.30d, 0.10d, 0.40d }, 2, 0.80d),
            0.26666666666666666d,
            0.40d,
            0.13333333333333333d,
            0.20d);
        AssertPanelRatios(
            "left endpoint collapses left panels",
            ViewerWindow.ResizePanelRatiosAtDivider(startRatios, 1, 0d),
            0d,
            0d,
            0.20d,
            0.80d);
        AssertPanelRatios(
            "right endpoint collapses right panels",
            ViewerWindow.ResizePanelRatiosAtDivider(startRatios, 1, 1d),
            0.60d,
            0.40d,
            0d,
            0d);
        AssertPanelRatios(
            "collapsed left side reopens evenly",
            ViewerWindow.ResizePanelRatiosAtDivider(new[] { 0d, 0d, 0.40d, 0.60d }, 1, 0.20d),
            0.10d,
            0.10d,
            0.32d,
            0.48d);
        AssertPanelRatios(
            "collapsed right side reopens evenly",
            ViewerWindow.ResizePanelRatiosAtDivider(new[] { 0.40d, 0.60d, 0d, 0d }, 1, 0.80d),
            0.32d,
            0.48d,
            0.10d,
            0.10d);
        AssertPanelRatios(
            "invalid ratios normalize before resize",
            ViewerWindow.ResizePanelRatiosAtDivider(new[] { double.NaN, -1d, 2d }, 1, 0.40d),
            0.20d,
            0.20d,
            0.60d);
        AssertPanelRatios(
            "invalid divider leaves normalized ratios unchanged",
            ViewerWindow.ResizePanelRatiosAtDivider(new[] { 2d, 1d }, 3, 0.50d),
            0.50d,
            0.50d);
        AssertPanelRatios(
            "invalid target keeps the original divider",
            ViewerWindow.ResizePanelRatiosAtDivider(startRatios, 1, double.NaN),
            startRatios);
        AssertPanelRatios(
            "visible-only ratios exclude hidden panels",
            ViewerWindow.ResizePanelRatiosAtDivider(new[] { 0.25d, 0.75d }, 0, 0.40d),
            0.40d,
            0.60d);
        AssertPanelRatios(
            "minimum panel size clamps the left endpoint",
            ViewerWindow.ResizePanelRatiosAtDivider(
                new[] { 0.25d, 0.25d, 0.25d, 0.25d },
                1,
                0d,
                0.10d),
            0.10d,
            0.10d,
            0.40d,
            0.40d);
        AssertPanelRatios(
            "minimum panel size clamps the right endpoint",
            ViewerWindow.ResizePanelRatiosAtDivider(
                new[] { 0.25d, 0.25d, 0.25d, 0.25d },
                1,
                1d,
                0.10d),
            0.40d,
            0.40d,
            0.10d,
            0.10d);
        AssertPanelRatios(
            "minimum panel size applies to the first divider",
            ViewerWindow.ResizePanelRatiosAtDivider(
                new[] { 0.25d, 0.25d, 0.25d, 0.25d },
                0,
                0d,
                0.10d),
            0.10d,
            0.30d,
            0.30d,
            0.30d);
        AssertPanelRatios(
            "minimum panel size applies to the last divider",
            ViewerWindow.ResizePanelRatiosAtDivider(
                new[] { 0.25d, 0.25d, 0.25d, 0.25d },
                2,
                1d,
                0.10d),
            0.30d,
            0.30d,
            0.30d,
            0.10d);
        AssertPanelRatios(
            "panel at minimum does not block another lower panel",
            ViewerWindow.ResizePanelRatiosAtDivider(
                new[] { 0.50d, 0.10d, 0.40d },
                0,
                0.70d,
                0.10d),
            0.70d,
            0.10d,
            0.20d);
        AssertPanelRatios(
            "divider stops only when all lower panels reach minimum",
            ViewerWindow.ResizePanelRatiosAtDivider(
                new[] { 0.50d, 0.10d, 0.40d },
                0,
                1d,
                0.10d),
            0.80d,
            0.10d,
            0.10d);
        AssertPanelRatios(
            "lower panels freeze successively",
            ViewerWindow.ResizePanelRatiosAtDivider(
                new[] { 0.30d, 0.10d, 0.15d, 0.45d },
                0,
                0.65d,
                0.10d),
            0.65d,
            0.10d,
            0.10d,
            0.15d);
        AssertPanelRatios(
            "upper panels use the same progressive minimum",
            ViewerWindow.ResizePanelRatiosAtDivider(
                new[] { 0.40d, 0.10d, 0.50d },
                1,
                0.30d,
                0.10d),
            0.20d,
            0.10d,
            0.70d);
        AssertPanelRatios(
            "expanding side releases a panel from minimum",
            ViewerWindow.ResizePanelRatiosAtDivider(
                new[] { 0.50d, 0.10d, 0.40d },
                0,
                0.40d,
                0.10d),
            0.40d,
            0.12d,
            0.48d);
        AssertPanelRatios(
            "main pane uses its smaller individual minimum",
            ViewerWindow.ResizePanelRatiosAtDivider(
                new[] { 0.50d, 0.25d, 0.25d },
                0,
                0d,
                new[] { 0.05d, 0.10d, 0.10d }),
            0.05d,
            0.475d,
            0.475d);
        AssertPanelRatios(
            "different result minimums clamp independently",
            ViewerWindow.ResizePanelRatiosAtDivider(
                new[] { 0.50d, 0.25d, 0.25d },
                0,
                1d,
                new[] { 0.05d, 0.10d, 0.15d }),
            0.75d,
            0.10d,
            0.15d);
        AssertPanelRatios(
            "window resize redistributes panels that cross their minimum",
            ViewerWindow.ConstrainPanelRatiosToMinimums(
                new[] { 0.70d, 0.10d, 0.20d },
                new[] { 0.10d, 0.20d, 0.20d }),
            0.60d,
            0.20d,
            0.20d);
        AssertDividerPositions(
            "minimum does not change a valid proportional resize",
            ViewerWindow.ResizePanelRatiosAtDivider(startRatios, 1, 0.25d, 0.05d),
            0.15d,
            0.25d,
            0.40d);
        AssertPanelRatios(
            "undersized starting panel is restored to the minimum",
            ViewerWindow.ResizePanelRatiosAtDivider(
                new[] { 0d, 0.50d, 0.25d, 0.25d },
                1,
                0.30d,
                0.10d),
            0.10d,
            0.20d,
            0.35d,
            0.35d);
        AssertEqual(
            "empty panel ratios stay empty",
            ViewerWindow.ResizePanelRatiosAtDivider(Array.Empty<double>(), 0, 0.50d).Length,
            0);
    }

    private static void RunViewportMinimumHeight()
    {
        AssertEqual(
            "raw viewport needs one line",
            ViewportPaneWindow.CalculateRequiredClientHeightForOneDataLine(
                lineHeight: 16,
                hasHeader: false),
            16);
        AssertEqual(
            "tabular viewport needs header and one data line",
            ViewportPaneWindow.CalculateRequiredClientHeightForOneDataLine(
                lineHeight: 16,
                hasHeader: true),
            32);
        AssertEqual(
            "minimum includes the existing border and horizontal scrollbar",
            ViewportPaneWindow.CalculateMinimumOuterHeight(
                requiredClientHeight: 32,
                currentOuterHeight: 34,
                currentClientHeight: 15),
            51);
        AssertEqual(
            "minimum ignores invalid negative chrome",
            ViewportPaneWindow.CalculateMinimumOuterHeight(
                requiredClientHeight: 16,
                currentOuterHeight: 10,
                currentClientHeight: 12),
            16);
        AssertEqual(
            "hidden viewport keeps scrollbar and border fallback",
            ViewportPaneWindow.CalculateMinimumOuterHeight(
                requiredClientHeight: 32,
                currentOuterHeight: 0,
                currentClientHeight: 0,
                fallbackChromeHeight: 19),
            51);
    }

    private static void RunUndersizedLayoutClipping()
    {
        const int topBarHeight = 30;
        int fixedSearchHeight = ViewerWindow.CalculateSearchFixedAreaHeight(
            searchLevelCount: 3,
            visibleResultCount: 2);
        AssertEqual(
            "fixed search layout keeps three rows followed by status",
            fixedSearchHeight,
            100);

        int[] minimumPanelHeights = { 35, 51, 51 };
        int minimumClientHeight = ViewerWindow.CalculateMinimumLayoutClientHeight(
            topBarHeight,
            fixedSearchHeight,
            minimumPanelHeights);
        AssertEqual(
            "minimum client height includes fixed controls and every pane",
            minimumClientHeight,
            267);
        AssertEqual(
            "window above minimum uses its actual height",
            ViewerWindow.CalculateLayoutClientHeight(
                actualClientHeight: 320,
                minimumClientHeight),
            320);
        AssertEqual(
            "window below minimum keeps a virtual minimum-height canvas",
            ViewerWindow.CalculateLayoutClientHeight(
                actualClientHeight: 220,
                minimumClientHeight),
            minimumClientHeight);
        AssertEqual(
            "extreme window height still keeps the full virtual canvas",
            ViewerWindow.CalculateLayoutClientHeight(
                actualClientHeight: 0,
                minimumClientHeight),
            minimumClientHeight);
        AssertEqual(
            "virtual canvas leaves every pane at its minimum",
            minimumClientHeight - topBarHeight - fixedSearchHeight,
            137);
        AssertEqual(
            "manually hidden pane stays outside the virtual canvas",
            ViewerWindow.CalculateMinimumLayoutClientHeight(
                topBarHeight,
                fixedSearchHeight,
                minimumPanelHeights: new[] { 35, 51 }),
            216);

        const double combinedPanelMinimum = 137d;
        AssertPanelRatios(
            "panes remain at their individual minimums in the virtual canvas",
            ViewerWindow.ConstrainPanelRatiosToMinimums(
                new[] { 0.70d, 0.10d, 0.20d },
                new[]
                {
                    35d / combinedPanelMinimum,
                    51d / combinedPanelMinimum,
                    51d / combinedPanelMinimum
                }),
            35d / combinedPanelMinimum,
            51d / combinedPanelMinimum,
            51d / combinedPanelMinimum);

        AssertEqual(
            "dividers are disabled at the combined minimum",
            ViewerWindow.HasPanelResizeCapacity(
                availableHeight: (int)combinedPanelMinimum,
                minimumPanelHeights),
            false);
        AssertEqual(
            "dividers return after recovering spare height",
            ViewerWindow.HasPanelResizeCapacity(
                availableHeight: (int)combinedPanelMinimum + 1,
                minimumPanelHeights),
            true);

        AssertEqual(
            "enabled result keeps its divider",
            ViewerWindow.GetSearchPanelDividerIndex(
                visibleSearchIndices: new[] { 0, 1 },
                includesMainPanel: true,
                searchResultIndex: 1),
            1);
        AssertEqual(
            "manually hidden result has no divider",
            ViewerWindow.GetSearchPanelDividerIndex(
                visibleSearchIndices: new[] { 0, 1 },
                includesMainPanel: true,
                searchResultIndex: 2),
            -1);
        AssertEqual(
            "first result without main has no upper divider",
            ViewerWindow.GetSearchPanelDividerIndex(
                visibleSearchIndices: new[] { 0, 1 },
                includesMainPanel: false,
                searchResultIndex: 0),
            -1);
        AssertEqual(
            "second result without main owns the internal divider",
            ViewerWindow.GetSearchPanelDividerIndex(
                visibleSearchIndices: new[] { 0, 1 },
                includesMainPanel: false,
                searchResultIndex: 1),
            0);
    }

    private static void AssertDividerPositions(
        string name,
        IReadOnlyList<double> panelRatios,
        params double[] expectedPositions)
    {
        AssertEqual(name + " panel count", panelRatios.Count, expectedPositions.Length + 1);
        double position = 0d;
        for (int i = 0; i < expectedPositions.Length; i++)
        {
            position += panelRatios[i];
            AssertNear(name + " divider " + i, position, expectedPositions[i], 0.000000001d);
        }
    }

    private static void AssertPanelRatios(
        string name,
        IReadOnlyList<double> actual,
        params double[] expected)
    {
        AssertEqual(name + " count", actual.Count, expected.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            AssertNear(name + " [" + i + "]", actual[i], expected[i], 0.000000001d);
        }
    }

    private static void RunOutputExport(string tempRoot)
    {
        string parsedSourcePath = WriteLog(
            tempRoot,
            "output-export-source.log",
            "{\"Value\":\"first\"}\r\n{\"Value\":\"second\"}\r\n");
        DisplayParserRule parser = JsonParser("${Value}\nparsed");
        string parsedOutputPath = Path.Combine(tempRoot, "output-export-parsed.log");
        File.WriteAllText(parsedOutputPath, "old content", Encoding.UTF8);
        using (var source = new LogRecordSource(parsedSourcePath, Encoding.UTF8, 0, parser))
        {
            LogOutputExporter.SaveParsedLog(source, parsedOutputPath);
        }

        AssertUtf8Bom("parsed export BOM", parsedOutputPath);
        AssertEqual(
            "parsed export content",
            File.ReadAllText(parsedOutputPath, Encoding.UTF8),
            "first\nparsed\r\nsecond\nparsed");

        string longOriginalLine = new('x', ProjectedViewport.VisibleSegmentChars + 25);
        string originalSourcePath = WriteLog(tempRoot, "output-export-original.log", longOriginalLine + "\nsecond");
        string originalOutputPath = Path.Combine(tempRoot, "output-export-original-result.log");
        using (var source = new LogRecordSource(originalSourcePath, Encoding.UTF8, 0))
        {
            LogOutputExporter.SaveParsedLog(source, originalOutputPath);
        }

        AssertEqual(
            "original export normalizes record separators",
            File.ReadAllText(originalOutputPath, Encoding.UTF8),
            longOriginalLine + "\r\nsecond");

        string searchSourcePath = WriteLog(
            tempRoot,
            "output-export-search.log",
            "INFO code=10\r\nERROR\tcode=42\r\n");
        string searchOutputPath = Path.Combine(tempRoot, "output-export-search.tsv");
        using (FilteredLogRecordSource source = LogSearchBuilder.BuildFilteredReader(
            searchSourcePath,
            Encoding.UTF8,
            0,
            new SearchOptions(@"ERROR\tcode=(?<code>\d+)", UseRegex: true, IgnoreCase: false)))
        {
            LogOutputExporter.SaveSearchResults(source, searchOutputPath);
        }

        AssertUtf8Bom("search export BOM", searchOutputPath);
        AssertEqual(
            "search export content",
            File.ReadAllText(searchOutputPath, Encoding.UTF8),
            "#\tText\tcode\r\n2\tERROR code=42\t42");

        string emptyOutputPath = Path.Combine(tempRoot, "output-export-empty.tsv");
        using (FilteredLogRecordSource source = LogSearchBuilder.BuildFilteredReader(
            searchSourcePath,
            Encoding.UTF8,
            0,
            new SearchOptions("missing", UseRegex: false, IgnoreCase: false)))
        {
            LogOutputExporter.SaveSearchResults(source, emptyOutputPath);
        }

        AssertEqual(
            "empty search export keeps headers",
            File.ReadAllText(emptyOutputPath, Encoding.UTF8),
            "#\tText");
        AssertEqual(
            "output export leaves no temporary files",
            Directory.GetFiles(tempRoot, ".*.tmp").Length,
            0);
    }

    private static void AssertUtf8Bom(string name, string path)
    {
        byte[] content = File.ReadAllBytes(path);
        bool hasBom = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF;
        AssertEqual(name, hasBom, true);
    }

    private static void RunCombinedParserProjection(string tempRoot)
    {
        string firstLine = "a[0]: {\"Level\":\"Inf\r\n";
        string path = WriteLog(
            tempRoot,
            "combined-parser.log",
            firstLine +
            "a[1]: o\",\"Message\":\"run\r\n" +
            "a[2]: ning\"}\r\n" +
            "plain\r\n");
        DisplayParserRule parser = new()
        {
            Name = "combined",
            Stages = new List<DisplayParserStage>
            {
                new()
                {
                    Mode = DisplayParserMode.Regex,
                    Rule = @": (?<json>.*)",
                    Template = "${json}"
                },
                new()
                {
                    Mode = DisplayParserMode.Json,
                    Rule = "${upper:Level} - ${Message}"
                }
            }
        };

        using var viewport = new ProjectedViewport(new LogRecordSource(path, Encoding.UTF8, 0, parser), wrapLongLines: true);
        viewport.ReadFromPercentage(0d, 4);
        AssertSequence("combined parser records", viewport.CurrentRows, "INFO - running", "plain");

        viewport.ReadFromOffset(Encoding.UTF8.GetByteCount(firstLine), 2);
        AssertSequence(
            "combined parser does not backscan from continuation",
            viewport.CurrentRows,
            "o\",\"Message\":\"run",
            "ning\"}");

        viewport.ReadPrevious(1);
        AssertSequence(
            "combined parser reparses when start scrolls into view",
            viewport.CurrentRows,
            "INFO - running",
            "plain");

        string firstFragment = "a[0]: {\"Level\":\"Inf\r\n";
        string secondFragment = "a[1]: o\",\"Message\":\"run\r\n";
        string thirdFragment = "a[2]: ning \r\n";
        string fourthFragment = "a[3]: done\"}\r\n";
        string splitPath = WriteLog(
            tempRoot,
            "combined-parser-group-start.log",
            firstFragment +
            secondFragment +
            thirdFragment +
            fourthFragment +
            "plain\r\n");
        long fourthOffset =
            Encoding.UTF8.GetByteCount(firstFragment) +
            Encoding.UTF8.GetByteCount(secondFragment) +
            Encoding.UTF8.GetByteCount(thirdFragment);
        using var splitViewport = new ProjectedViewport(new LogRecordSource(splitPath, Encoding.UTF8, 0, parser), wrapLongLines: true);
        splitViewport.ReadFromOffset(fourthOffset, 2);
        AssertSequence(
            "combined parser starts on final continuation",
            splitViewport.CurrentRows,
            "done\"}",
            "plain");

        splitViewport.ReadPrevious(2);
        AssertSequence(
            "combined parser reparses from group start",
            splitViewport.CurrentRows,
            "INFO - running done",
            "plain");
    }

    private static void RunMainProjection(string tempRoot)
    {
        string first = new('a', ProjectedViewport.VisibleSegmentChars + 3);
        string parsed = first + "\r\n\nend";
        string path = WriteLog(tempRoot, "main-projection.log", "{\"First\":\"" + first + "\",\"Last\":\"end\"}\r\n");
        DisplayParserRule parser = JsonParser("${First}\r\n\n${Last}");

        using var viewport = new ProjectedViewport(new LogRecordSource(path, Encoding.UTF8, 0, parser), wrapLongLines: true);
        viewport.ReadFromPercentage(0d, 10);

        AssertEqual("main projection is not a grid", viewport is IColumnViewportReader, false);
        using IViewportReader mainWorker = viewport.CloneForWorker();
        AssertEqual("main worker is not a grid", mainWorker is IColumnViewportReader, false);
        AssertEqual("main projected row count", viewport.CurrentRows.Count, 4);
        AssertEqual("main first segment length", viewport.CurrentRows[0].Length, ProjectedViewport.VisibleSegmentChars);
        AssertEqual("main second segment", viewport.CurrentRows[1], "aaa");
        AssertEqual("main empty explicit row", viewport.CurrentRows[2], string.Empty);
        AssertEqual("main final explicit row", viewport.CurrentRows[3], "end");

        IReadOnlyList<ViewportHighlightGroup> groups = viewport.ReadCurrentHighlightGroups();
        AssertEqual("main highlight group count", groups.Count, 1);
        AssertEqual("main highlight logical text", groups[0].Text, parsed);
        AssertEqual("main shared highlight key", viewport.CurrentHighlightGroupKeys[0], viewport.CurrentHighlightGroupKeys[3]);

        ViewportTextSegmentKey secondKey = viewport.CurrentTextSegmentKeys[1];
        AssertEqual("main second text segment index", secondKey.SegmentIndex, 1);
        AssertEqual("main text context available", viewport.TryReadTextSelectionContext(secondKey, out ViewportTextSelectionContext context), true);
        AssertEqual("main text context", context.Text, parsed);
        AssertEqual("main text segment count", context.Segments.Count, 4);
        AssertEqual("main second text start", context.Segments[1].Start, ProjectedViewport.VisibleSegmentChars);
        AssertEqual("main empty text start", context.Segments[2].Start, first.Length + 2);

        IReadOnlyList<ViewportSelectedRow> copied = viewport.ReadSelectedRows(
            selectAll: true,
            Array.Empty<ViewportRowSelectionRange>(),
            Array.Empty<ViewportRowSelectionKey>());
        AssertEqual("main copy record count", copied.Count, 1);
        AssertEqual("main copy preserves parser newlines", copied[0].Text, parsed);
    }

    private static void RunMemoryProjection()
    {
        string longValue = new('m', ProjectedViewport.VisibleSegmentChars + 5);
        const string secondLine = "match ç";
        string text = longValue + "\r\n" + secondLine + "\r\n";
        LogContentSource content = LogContentSource.FromMemory("Pasted text", CreateUtf8BomContent(text));
        DetectedEncodingInfo detected = LogEncodingDetector.DetectEncoding(content);

        using (var viewport = new ProjectedViewport(
            new LogRecordSource(content, detected.Encoding, detected.DataOffset),
            wrapLongLines: true))
        {
            viewport.ReadFromPercentage(0d, 4);
            AssertEqual("memory projection first segment", viewport.CurrentRows[0].Length, ProjectedViewport.VisibleSegmentChars);
            AssertEqual("memory projection wrapped tail", viewport.CurrentRows[1], "mmmmm");
            AssertEqual("memory projection unicode", viewport.CurrentRows[2], secondLine);
        }

        using FilteredLogRecordSource filtered = LogSearchBuilder.BuildFilteredReader(
            content,
            detected.Encoding,
            detected.DataOffset,
            new SearchOptions("match", UseRegex: false, IgnoreCase: false));
        using var searchViewport = new FilteredProjectedViewport(filtered.CloneForWorker());
        searchViewport.ReadFromPercentage(0d, 2);
        AssertSequence("memory search projection", searchViewport.CurrentRows, secondLine);
    }

    private static void RunPastedTextSerialization()
    {
        string[] samples =
        {
            string.Empty,
            "line-1\r\nlinha-ç\n終わり",
            new string('x', 1024 * 1024)
        };
        foreach (string sample in samples)
        {
            using var output = new MemoryStream();
            PastedTextWindowLauncher.WriteUtf8Async(output, sample, CancellationToken.None).GetAwaiter().GetResult();
            byte[] expected = CreateUtf8BomContent(sample);
            byte[] actual = output.ToArray();
            AssertByteSequence("pasted serialization", actual, expected);
        }

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool cancelled = false;
        try
        {
            using var output = new MemoryStream();
            PastedTextWindowLauncher.WriteUtf8Async(output, "cancel", cancellation.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        AssertEqual("pasted serialization cancellation", cancelled, true);
    }

    private static void RunPastedTextLaunchFailure()
    {
        string missingExecutable = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".exe");
        PastedTextLaunchResult failure = PastedTextWindowLauncher.LaunchAsync(
            missingExecutable,
            "--unused",
            "content",
            CancellationToken.None).GetAwaiter().GetResult();
        AssertEqual("pasted missing executable fails", failure.Success, false);
        AssertEqual("pasted missing executable is not cancellation", failure.Cancelled, false);
        AssertEqual("pasted missing executable has error", string.IsNullOrEmpty(failure.ErrorMessage), false);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        PastedTextLaunchResult cancelled = PastedTextWindowLauncher.LaunchAsync(
            missingExecutable,
            "--unused",
            "content",
            cancellation.Token).GetAwaiter().GetResult();
        AssertEqual("pasted cancelled launch", cancelled.Cancelled, true);
        AssertEqual("pasted cancelled launch has no process", cancelled.ProcessId, 0);
    }

    private static void RunPastedTextTransferState()
    {
        var beforeState = new PastedTextTransferState();
        using (var cancellation = new CancellationTokenSource())
        {
            bool terminated = false;
            bool inputClosed = false;
            using CancellationTokenRegistration registration = cancellation.Token.Register(() =>
                beforeState.Cancel(() => terminated = true));
            cancellation.Cancel();
            AssertThrowsCancellation(
                "paste cancellation before close",
                () => beforeState.Complete(() => inputClosed = true, cancellation.Token));
            AssertEqual("paste before close terminates", terminated, true);
            AssertEqual("paste before close keeps input open", inputClosed, false);
            AssertEqual("paste before close is incomplete", beforeState.IsCompleted, false);
        }

        var duringState = new PastedTextTransferState();
        using (var cancellation = new CancellationTokenSource())
        {
            bool terminated = false;
            bool inputClosed = false;
            using CancellationTokenRegistration registration = cancellation.Token.Register(() =>
                duringState.Cancel(() => terminated = true));
            AssertThrowsCancellation(
                "paste cancellation during close",
                () => duringState.Complete(
                    () =>
                    {
                        inputClosed = true;
                        cancellation.Cancel();
                    },
                    cancellation.Token));
            AssertEqual("paste during close closes input", inputClosed, true);
            AssertEqual("paste during close terminates", terminated, true);
            AssertEqual("paste during close is incomplete", duringState.IsCompleted, false);
        }

        var afterState = new PastedTextTransferState();
        using (var cancellation = new CancellationTokenSource())
        {
            bool terminated = false;
            using CancellationTokenRegistration registration = cancellation.Token.Register(() =>
                afterState.Cancel(() => terminated = true));
            afterState.Complete(() => { }, cancellation.Token);
            cancellation.Cancel();
            AssertEqual("paste after close is complete", afterState.IsCompleted, true);
            AssertEqual("paste after close does not terminate", terminated, false);
        }
    }

    private static void RunSearchProjection(string tempRoot)
    {
        string longValue = new('x', ProjectedViewport.VisibleSegmentChars + 100);
        string path = WriteLog(
            tempRoot,
            "search-projection.log",
            "{\"First\":\"" + longValue + "\",\"Second\":\"match\"}\r\n");
        DisplayParserRule parser = JsonParser("${First}\n${Second}");

        using FilteredLogRecordSource source = LogSearchBuilder.BuildFilteredReader(
            path,
            Encoding.UTF8,
            0,
            new SearchOptions("match", UseRegex: false, IgnoreCase: false),
            parser);
        using var viewport = new FilteredProjectedViewport(source.CloneForWorker());
        viewport.ReadFromPercentage(0d, 5);

        AssertEqual("search projection is a grid", viewport is IColumnViewportReader, true);
        using IViewportReader searchWorker = viewport.CloneForWorker();
        AssertEqual("search worker remains a grid", searchWorker is IColumnViewportReader, true);
        AssertEqual("search projected row count", viewport.CurrentRows.Count, 1);
        AssertEqual("search projected text", viewport.CurrentRows[0], "match");
        AssertEqual("search explicit selection index", viewport.CurrentRowSelectionKeys[0].SegmentIndex, 1);
        AssertEqual("search explicit text index", viewport.CurrentTextSegmentKeys[0].SegmentIndex, 1);
        AssertEqual(
            "search text context available",
            viewport.TryReadTextSelectionContext(viewport.CurrentTextSegmentKeys[0], out ViewportTextSelectionContext context),
            true);
        AssertEqual("search text context uses logical record", context.Text, longValue + "\nmatch");
        AssertEqual("search text context explicit rows", context.Segments.Count, 2);
        AssertEqual("search text context matching row", context.Segments[1].Key, viewport.CurrentTextSegmentKeys[0]);

        IReadOnlyList<ViewportHighlightGroup> groups = viewport.ReadCurrentHighlightGroups();
        AssertEqual("search highlight uses full logical text", groups[0].Text, longValue + "\nmatch");

        IReadOnlyList<ViewportSelectedRow> copied = viewport.ReadSelectedRows(
            selectAll: true,
            Array.Empty<ViewportRowSelectionRange>(),
            Array.Empty<ViewportRowSelectionKey>());
        AssertEqual("search copy only matched explicit row", copied[0].Text, "match");

        string rawLong = new string('q', ProjectedViewport.VisibleSegmentChars + 500) + "needle";
        string rawPath = WriteLog(tempRoot, "search-long-row.log", rawLong + "\r\n");
        using FilteredLogRecordSource rawSource = LogSearchBuilder.BuildFilteredReader(
            rawPath,
            Encoding.UTF8,
            0,
            new SearchOptions("needle", UseRegex: false, IgnoreCase: false));
        using var rawViewport = new FilteredProjectedViewport(rawSource.CloneForWorker());
        rawViewport.ReadFromPercentage(0d, 3);
        AssertEqual("search long row is not wrapped", rawViewport.CurrentRows.Count, 1);
        AssertEqual("search long row remains complete", rawViewport.CurrentRows[0], rawLong);

        string endPath = WriteLog(tempRoot, "search-end.log", "match-0\r\nmatch-1\r\nmatch-2\r\nmatch-3\r\n");
        using FilteredLogRecordSource endSource = LogSearchBuilder.BuildFilteredReader(
            endPath,
            Encoding.UTF8,
            0,
            new SearchOptions("match", UseRegex: false, IgnoreCase: false));
        using var endViewport = new FilteredProjectedViewport(endSource.CloneForWorker());
        endViewport.ReadFromPercentage(100d, 2);
        AssertSequence("search end includes final records", endViewport.CurrentRows, "match-2", "match-3");

        var percentageContent = new StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            percentageContent.Append("match-").Append(i).Append("\r\n");
        }

        string percentagePath = WriteLog(tempRoot, "search-percentage.log", percentageContent.ToString());
        using FilteredLogRecordSource percentageSource = LogSearchBuilder.BuildFilteredReader(
            percentagePath,
            Encoding.UTF8,
            0,
            new SearchOptions("match", UseRegex: false, IgnoreCase: false));
        using var percentageViewport = new FilteredProjectedViewport(percentageSource.CloneForWorker());
        percentageViewport.ReadFromPercentage(50d, 20);
        AssertEqual("search percentage uses max top", percentageViewport.TopRowOrdinal, 40L);
        AssertNear("search percentage round trip", percentageViewport.ScrollPercentage, 50d, 0.0001d);
    }

    private static void RunRecordNavigation(string tempRoot)
    {
        string longLine = new('z', (ProjectedViewport.VisibleSegmentChars * 2) + 8);
        string path = WriteLog(tempRoot, "navigation.log", longLine + "\r\nlast\r\n");
        using var viewport = new ProjectedViewport(new LogRecordSource(path, Encoding.UTF8, 0), wrapLongLines: true);

        viewport.ReadFromPercentage(0d, 2);
        AssertEqual("navigation initial first length", viewport.CurrentRows[0].Length, ProjectedViewport.VisibleSegmentChars);
        AssertEqual("navigation initial second length", viewport.CurrentRows[1].Length, ProjectedViewport.VisibleSegmentChars);

        viewport.ReadNext(1);
        AssertEqual("navigation inside record first length", viewport.CurrentRows[0].Length, ProjectedViewport.VisibleSegmentChars);
        AssertEqual("navigation inside record second tail", viewport.CurrentRows[1], "zzzzzzzz");

        viewport.ReadNext(1);
        AssertEqual("navigation crosses record first", viewport.CurrentRows[0], "zzzzzzzz");
        AssertEqual("navigation crosses record second", viewport.CurrentRows[1], "last");

        viewport.ReadPrevious(1);
        AssertEqual("navigation previous stays in logical record", viewport.CurrentRows[0].Length, ProjectedViewport.VisibleSegmentChars);

        viewport.ReadFromOffset(ProjectedViewport.VisibleSegmentChars + 20, 2);
        AssertEqual("navigation offset starts in middle segment", viewport.CurrentRows[0].Length, ProjectedViewport.VisibleSegmentChars);
        AssertEqual("navigation offset follows with tail", viewport.CurrentRows[1], "zzzzzzzz");

        viewport.ReadFromOffset(ProjectedViewport.VisibleSegmentChars, 2);
        AssertEqual("navigation exact boundary starts next segment", viewport.CurrentRows[0].Length, ProjectedViewport.VisibleSegmentChars);
        AssertEqual("navigation exact boundary follows with tail", viewport.CurrentRows[1], "zzzzzzzz");

        viewport.ReadFromPercentage(50d, 2);
        AssertEqual("navigation percentage starts in middle segment", viewport.CurrentRows[0].Length, ProjectedViewport.VisibleSegmentChars);
        AssertEqual("navigation percentage follows with tail", viewport.CurrentRows[1], "zzzzzzzz");

        viewport.ReadNext(2);
        viewport.ReadPrevious(3);
        AssertEqual("navigation page up clamps to first segment", viewport.CurrentRows[0].Length, ProjectedViewport.VisibleSegmentChars);
        AssertEqual("navigation page up keeps second segment", viewport.CurrentRows[1].Length, ProjectedViewport.VisibleSegmentChars);
    }

    private static void RunSelectionAcrossRecords(string tempRoot)
    {
        string wrapped = new string('w', ProjectedViewport.VisibleSegmentChars) + "tail";
        string path = WriteLog(tempRoot, "selection.log", wrapped + "\r\nline-1\r\nline-2\r\n");
        using var viewport = new ProjectedViewport(new LogRecordSource(path, Encoding.UTF8, 0), wrapLongLines: true);
        viewport.ReadFromPercentage(0d, 3);
        ViewportRowSelectionKey wrappedSegment = viewport.CurrentRowSelectionKeys[1];
        ViewportRowSelectionKey secondLine = viewport.CurrentRowSelectionKeys[2];

        IReadOnlyList<ViewportSelectedRow> wrappedRange = viewport.ReadSelectedRows(
            selectAll: false,
            new[] { new ViewportRowSelectionRange(wrappedSegment, secondLine) },
            Array.Empty<ViewportRowSelectionKey>());
        AssertSelectedRows("selection joins wrapped row", wrappedRange, wrapped, "line-1");

        viewport.ReadFromPercentage(100d, 2);
        ViewportRowSelectionKey last = viewport.CurrentRowSelectionKeys[1];
        IReadOnlyList<ViewportSelectedRow> fullRange = viewport.ReadSelectedRows(
            selectAll: false,
            new[] { new ViewportRowSelectionRange(wrappedSegment, last) },
            Array.Empty<ViewportRowSelectionKey>());
        AssertSelectedRows("selection spans unloaded records", fullRange, wrapped, "line-1", "line-2");

        IReadOnlyList<ViewportSelectedRow> excluded = viewport.ReadSelectedRows(
            selectAll: true,
            Array.Empty<ViewportRowSelectionRange>(),
            new[] { wrappedSegment });
        AssertSelectedRows("selection exclusion removes logical record", excluded, "line-1", "line-2");

        string capturePath = WriteLog(tempRoot, "selection-captures.log", "aaabccc xx aabcc\r\nplain\r\n");
        using FilteredLogRecordSource captureSource = LogSearchBuilder.BuildFilteredReader(
            capturePath,
            Encoding.UTF8,
            0,
            new SearchOptions("(a+)b(c+)", UseRegex: true, IgnoreCase: false));
        using var captureViewport = new FilteredProjectedViewport(captureSource.CloneForWorker());
        IReadOnlyList<ViewportSelectedRow> captured = captureViewport.ReadSelectedRows(
            selectAll: true,
            Array.Empty<ViewportRowSelectionRange>(),
            Array.Empty<ViewportRowSelectionKey>());
        AssertEqual("selection capture row count", captured.Count, 1);
        AssertSequence(
            "selection capture cells",
            captured[0].Cells ?? Array.Empty<string>(),
            "aaabccc xx aabcc",
            "aaa",
            "ccc");
    }

    private static void RunLargeLineScrollbar(string tempRoot)
    {
        string longLine = new('x', ProjectedViewport.VisibleSegmentChars * 1000);
        string path = WriteLog(tempRoot, "large-line-scrollbar.log", longLine + "\r\nnext\r\n");
        using var viewport = new ProjectedViewport(new LogRecordSource(path, Encoding.UTF8, 0), wrapLongLines: true);
        viewport.ReadFromPercentage(0d, 20);
        AssertNear("large line initial percentage", viewport.ScrollPercentage, 0d, 0.0001d);

        viewport.ReadNext(1);
        double firstMovement = viewport.ScrollPercentage;
        AssertEqual("large line first movement advances percentage", firstMovement > 0d, true);
        AssertEqual("large line first movement keeps physical offset", viewport.TopOffset, 0L);

        viewport.ReadNext(499);
        int segmentIndex = viewport.CurrentTextSegmentKeys[0].SegmentIndex;
        double middlePercentage = viewport.ScrollPercentage;
        AssertEqual("large line middle segment", segmentIndex, 500);
        AssertEqual("large line middle percentage", middlePercentage > 49d && middlePercentage < 51d, true);

        viewport.ReadFromPercentage(middlePercentage, 20);
        AssertEqual("large line percentage round trip segment", viewport.CurrentTextSegmentKeys[0].SegmentIndex, segmentIndex);
        AssertNear("large line percentage round trip value", viewport.ScrollPercentage, middlePercentage, 0.0001d);

        viewport.ReadFromPercentage(100d, 20);
        AssertNear("large line end percentage", viewport.ScrollPercentage, 100d, 0.0001d);
        viewport.ReadNext(19);
        AssertEqual("large line transitions to next record", viewport.CurrentRows[0], "next");

        var resizeContent = new StringBuilder();
        for (int i = 0; i < 50; i++)
        {
            resizeContent.Append("line-").Append(i).Append("\r\n");
        }

        string resizePath = WriteLog(tempRoot, "viewport-resize.log", resizeContent.ToString());
        using var resizeViewport = new ProjectedViewport(
            new LogRecordSource(resizePath, Encoding.UTF8, 0),
            wrapLongLines: true);
        resizeViewport.ReadFromPercentage(0d, 10);
        AssertEqual("viewport initial height", resizeViewport.CurrentRows.Count, 10);
        resizeViewport.ReadFromPercentage(0d, 30);
        AssertEqual("viewport grows loaded window", resizeViewport.CurrentRows.Count, 30);
        AssertEqual("viewport grown final row", resizeViewport.CurrentRows[29], "line-29");
    }

    private static void RunParsedScrollbar(string tempRoot)
    {
        string generated = new('p', ProjectedViewport.VisibleSegmentChars * 100);
        string path = WriteLog(tempRoot, "parsed-scrollbar.log", "{\"Value\":\"x\"}\r\nnext\r\n");
        DisplayParserRule parser = JsonParser(generated + "${Value}");
        using var viewport = new ProjectedViewport(new LogRecordSource(path, Encoding.UTF8, 0, parser), wrapLongLines: true);
        viewport.ReadFromPercentage(0d, 10);
        viewport.ReadNext(50);

        int segmentIndex = viewport.CurrentTextSegmentKeys[0].SegmentIndex;
        double percentage = viewport.ScrollPercentage;
        AssertEqual("parsed scrollbar segment", segmentIndex, 50);
        AssertEqual("parsed scrollbar percentage advances", percentage > 0d, true);
        viewport.ReadFromPercentage(percentage, 10);
        AssertEqual("parsed scrollbar round trip", viewport.CurrentTextSegmentKeys[0].SegmentIndex, segmentIndex);

        string fragment = new('j', ProjectedViewport.VisibleSegmentChars * 3);
        string firstHalf = fragment.Substring(0, fragment.Length / 2);
        string secondHalf = fragment.Substring(fragment.Length / 2);
        string combinedPath = WriteLog(
            tempRoot,
            "combined-parsed-scrollbar.log",
            "a[0]: {\"Value\":\"" + firstHalf + "\r\n" +
            "a[1]: " + secondHalf + "\"}\r\nnext\r\n");
        DisplayParserRule combinedParser = new()
        {
            Name = "combined-scroll",
            Stages = new List<DisplayParserStage>
            {
                new()
                {
                    Mode = DisplayParserMode.Regex,
                    Rule = @": (?<json>.*)",
                    Template = "${json}"
                },
                new()
                {
                    Mode = DisplayParserMode.Json,
                    Rule = "${Value}"
                }
            }
        };
        using var combinedViewport = new ProjectedViewport(
            new LogRecordSource(combinedPath, Encoding.UTF8, 0, combinedParser),
            wrapLongLines: true);
        combinedViewport.ReadFromPercentage(0d, 2);
        combinedViewport.ReadNext(1);
        int combinedSegment = combinedViewport.CurrentTextSegmentKeys[0].SegmentIndex;
        double combinedPercentage = combinedViewport.ScrollPercentage;
        combinedViewport.ReadFromPercentage(combinedPercentage, 2);
        AssertEqual("combined parser scrollbar round trip", combinedViewport.CurrentTextSegmentKeys[0].SegmentIndex, combinedSegment);
    }

    private static void RunEncodedScrollbarOffsets(string tempRoot)
    {
        string line = new string('a', ProjectedViewport.VisibleSegmentChars - 1) + "á" + new string('b', ProjectedViewport.VisibleSegmentChars);

        string utf8Path = Path.Combine(tempRoot, "scrollbar-utf8.log");
        File.WriteAllText(utf8Path, line + "\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        using (var utf8Viewport = new ProjectedViewport(
            new LogRecordSource(utf8Path, Encoding.UTF8, Encoding.UTF8.GetPreamble().Length),
            wrapLongLines: true))
        {
            long middleOfMultibyteCharacter = Encoding.UTF8.GetPreamble().Length + ProjectedViewport.VisibleSegmentChars;
            utf8Viewport.ReadFromOffset(middleOfMultibyteCharacter, 1);
            AssertEqual("utf8 partial character stays in prior segment", utf8Viewport.CurrentTextSegmentKeys[0].SegmentIndex, 0);

            long secondSegmentOffset = Encoding.UTF8.GetPreamble().Length + Encoding.UTF8.GetByteCount(line.AsSpan(0, ProjectedViewport.VisibleSegmentChars));
            utf8Viewport.ReadFromOffset(secondSegmentOffset, 1);
            AssertEqual("utf8 exact segment offset", utf8Viewport.CurrentTextSegmentKeys[0].SegmentIndex, 1);
            double percentage = utf8Viewport.ScrollPercentage;
            utf8Viewport.ReadFromPercentage(percentage, 1);
            AssertEqual("utf8 exact percentage round trip", utf8Viewport.CurrentTextSegmentKeys[0].SegmentIndex, 1);
        }

        string utf16Path = Path.Combine(tempRoot, "scrollbar-utf16.log");
        File.WriteAllText(utf16Path, line + "\r\n", new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
        using var utf16Viewport = new ProjectedViewport(
            new LogRecordSource(utf16Path, Encoding.Unicode, Encoding.Unicode.GetPreamble().Length),
            wrapLongLines: true);
        long utf16SecondSegmentOffset = Encoding.Unicode.GetPreamble().Length + Encoding.Unicode.GetByteCount(line.AsSpan(0, ProjectedViewport.VisibleSegmentChars));
        utf16Viewport.ReadFromOffset(utf16SecondSegmentOffset, 1);
        AssertEqual("utf16 exact segment offset", utf16Viewport.CurrentTextSegmentKeys[0].SegmentIndex, 1);
        double utf16Percentage = utf16Viewport.ScrollPercentage;
        utf16Viewport.ReadFromPercentage(utf16Percentage, 1);
        AssertEqual("utf16 exact percentage round trip", utf16Viewport.CurrentTextSegmentKeys[0].SegmentIndex, 1);
    }

    private static void RunBatchedNavigation()
    {
        var records = new List<LogViewportRecord>();
        for (int i = 0; i < 40; i++)
        {
            records.Add(new LogViewportRecord(
                new LogRecordKey(i * 10, (i * 10) + 5),
                (i * 10) + 10,
                "row-" + i,
                "row-" + i));
        }

        var source = new CountingRecordSource(records);
        using var viewport = new ProjectedViewport(source, wrapLongLines: true);
        viewport.ReadFromPercentage(0d, 10);

        source.ResetCalls();
        viewport.ReadNext(3);
        AssertEqual("batched wheel source calls", source.NextCalls, 1);
        AssertEqual("batched wheel source count", source.LastNextCount, 3);
        AssertEqual("batched wheel target", viewport.CurrentRows[0], "row-3");

        source.ResetCalls();
        viewport.ReadNext(10);
        AssertEqual("batched page down source calls", source.NextCalls, 1);
        AssertEqual("batched page down source count", source.LastNextCount, 10);
        AssertEqual("batched page down target", viewport.CurrentRows[0], "row-13");

        source.ResetCalls();
        viewport.ReadPrevious(10);
        AssertEqual("batched page up previous calls", source.PreviousCalls, 1);
        AssertEqual("batched page up total calls", source.PreviousCalls + source.NextCalls <= 2, true);
        AssertEqual("batched page up target", viewport.CurrentRows[0], "row-3");

        var searchSource = new CountingRecordSource(records);
        using var searchViewport = new ProjectedViewport(searchSource, wrapLongLines: false);
        searchViewport.ReadFromPercentage(0d, 10);
        searchSource.ResetCalls();
        searchViewport.ReadNext(10);
        AssertEqual("batched search page down calls", searchSource.NextCalls, 1);
        AssertEqual("batched search page down count", searchSource.LastNextCount, 10);

        string wrappedText = new('x', (ProjectedViewport.VisibleSegmentChars * 3) + 1);
        var wrappedSource = new CountingRecordSource(new[]
        {
            new LogViewportRecord(new LogRecordKey(0, wrappedText.Length), wrappedText.Length, wrappedText, wrappedText)
        });
        using var wrappedViewport = new ProjectedViewport(wrappedSource, wrapLongLines: true);
        wrappedViewport.ReadFromPercentage(0d, 2);
        wrappedSource.ResetCalls();
        wrappedViewport.ReadNext(1);
        AssertEqual("wrapped movement stays in projection", wrappedSource.NextCalls, 0);
        wrappedSource.ResetCalls();
        wrappedViewport.ReadPrevious(1);
        AssertEqual("wrapped reverse movement stays in projection", wrappedSource.PreviousCalls, 0);
    }

    private static void RunMixedWrappedBatchedNavigation()
    {
        int[] segmentCounts = { 3, 1, 4, 2, 3, 1, 4, 2, 3, 1, 4, 2 };
        const string labels = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var records = new List<LogViewportRecord>(segmentCounts.Length);
        int labelIndex = 0;
        for (int i = 0; i < segmentCounts.Length; i++)
        {
            records.Add(CreateSegmentedRecord(i, segmentCounts[i], labels, ref labelIndex));
        }

        var source = new CountingRecordSource(records);
        using var viewport = new ProjectedViewport(source, wrapLongLines: true);
        viewport.ReadFromPercentage(0d, 3);
        viewport.ReadNext(1);
        AssertEqual("mixed wrapped setup starts on second segment", viewport.CurrentRows[0][0], 'B');

        source.ResetCalls();
        viewport.ReadNext(5);
        AssertEqual("mixed wrapped page down target", viewport.CurrentRows[0][0], 'G');
        AssertEqual("mixed wrapped page down source calls", source.NextCalls, 1);

        source.ResetCalls();
        viewport.ReadPrevious(5);
        AssertEqual("mixed wrapped page up target", viewport.CurrentRows[0][0], 'B');
        AssertEqual("mixed wrapped page up source calls", source.PreviousCalls, 1);
        AssertEqual("mixed wrapped page up forward corrections", source.NextCalls, 0);

        viewport.ReadFromPercentage(0d, 3);
        source.ResetCalls();
        viewport.ReadNext(22);
        AssertEqual("mixed wrapped multi-batch page down target", viewport.CurrentRows[0][0], 'W');
        AssertEqual("mixed wrapped multi-batch page down source calls", source.NextCalls, 2);
        AssertEqual("mixed wrapped multi-batch avoids per-row calls", source.NextCalls < 22, true);

        source.ResetCalls();
        viewport.ReadPrevious(22);
        AssertEqual("mixed wrapped multi-batch page up target", viewport.CurrentRows[0][0], 'A');
        AssertEqual("mixed wrapped multi-batch page up source calls", source.PreviousCalls, 3);
        AssertEqual("mixed wrapped multi-batch page up forward corrections", source.NextCalls, 0);
        AssertEqual("mixed wrapped reverse avoids per-row calls", source.PreviousCalls < 22, true);
    }

    private static LogViewportRecord CreateSegmentedRecord(
        int recordIndex,
        int segmentCount,
        string labels,
        ref int labelIndex)
    {
        var text = new StringBuilder(segmentCount * ProjectedViewport.VisibleSegmentChars);
        for (int i = 0; i < segmentCount; i++)
        {
            text.Append(labels[labelIndex++], ProjectedViewport.VisibleSegmentChars);
        }

        long startOffset = recordIndex * 100_000L;
        long endOffset = startOffset + text.Length;
        string value = text.ToString();
        return new LogViewportRecord(
            new LogRecordKey(startOffset, endOffset),
            endOffset + 2,
            value,
            value);
    }

    private static void RunZeroProjection(string tempRoot)
    {
        string path = WriteLog(tempRoot, "zero.log", "one\r\ntwo\r\n");
        using var viewport = new ProjectedViewport(new LogRecordSource(path, Encoding.UTF8, 0), wrapLongLines: true);
        viewport.ReadFromPercentage(0d, 2);
        AssertEqual("zero setup rows", viewport.CurrentRows.Count, 2);

        viewport.MarkObservedZeroFileSize();
        AssertEqual("zero visual file size", viewport.FileSize, 0L);
        AssertEqual("zero visual rows", viewport.CurrentRows.Count, 0);

        viewport.ClearObservedZeroFileSize();
        AssertEqual("zero restored rows", viewport.CurrentRows.Count, 2);
        AssertEqual("zero restored first", viewport.CurrentRows[0], "one");
    }

    private static void RunWordAcrossSegmentBoundary(string tempRoot)
    {
        string text = new string(' ', ProjectedViewport.VisibleSegmentChars - 3) + "hello world";
        string path = WriteLog(tempRoot, "word-boundary.log", text + "\r\n");
        using var viewport = new ProjectedViewport(new LogRecordSource(path, Encoding.UTF8, 0), wrapLongLines: true);
        viewport.ReadFromPercentage(0d, 3);

        ViewportTextSegmentKey key = viewport.CurrentTextSegmentKeys[1];
        AssertEqual("word context available", viewport.TryReadTextSelectionContext(key, out ViewportTextSelectionContext context), true);
        int characterIndex = ProjectedViewport.VisibleSegmentChars;
        int start = characterIndex;
        while (start > 0 && !char.IsWhiteSpace(context.Text[start - 1]))
        {
            start--;
        }

        int end = characterIndex;
        while (end < context.Text.Length && !char.IsWhiteSpace(context.Text[end]))
        {
            end++;
        }

        AssertEqual("word crosses segment boundary", context.Text.Substring(start, end - start), "hello");
    }

    private static void RunWordTokenSelection()
    {
        AssertWordSelection(
            "word token dotted namespace",
            "Category=ATG.PricingEngine.Services.SecurityMaster, Level=Info",
            "PricingEngine",
            "ATG.PricingEngine.Services.SecurityMaster");
        AssertWordSelection(
            "word token job id",
            "Message=Strategy task JT67_48_250912145048_00064 is running",
            "250912",
            "JT67_48_250912145048_00064");
        AssertWordSelection(
            "word token date",
            "Timestamp=2026-06-09 05:50:02.3399",
            "06-09",
            "2026-06-09");
        AssertWordSelection(
            "word token time",
            "Timestamp=2026-06-09 05:50:02.3399",
            "50:02",
            "05:50:02.3399");
        AssertWordSelection(
            "word token json key",
            "{\"Level\":\"Info\"}",
            "Level",
            "Level");
        AssertWordSelection(
            "word token json value",
            "{\"Level\":\"Info\"}",
            "Info",
            "Info");
        AssertWordSelection(
            "word token equals key",
            "user=ana",
            "user",
            "user");
        AssertWordSelection(
            "word token equals value",
            "user=ana",
            "ana",
            "ana");
        AssertWordSelection(
            "word token url",
            "GET /api/orders/123 trace=user@example.com",
            "orders",
            "/api/orders/123");
        AssertEqual(
            "word token delimiter",
            ViewportPaneWindow.TryGetWordSelection("user=ana", "user=ana".IndexOf('=', StringComparison.Ordinal), out _, out _),
            false);
    }

    private static void RunWordDragSnapping()
    {
        AssertWordDragSelection(
            "word drag right",
            "hello world test",
            "hello",
            "or",
            "hello world");
        AssertWordDragSelection(
            "word drag left",
            "hello world test",
            "world",
            "ell",
            "hello world");
        AssertWordDragSelection(
            "word drag delimiter right",
            "hello world,test",
            "hello",
            ",",
            "hello world,");
        AssertWordDragSelection(
            "word drag immediate space right",
            "hello world",
            "hello",
            " ",
            "hello ");
        AssertWordDragSelection(
            "word drag log token",
            "Category=ATG.PricingEngine.Services.SecurityMaster Level=Info",
            "ATG",
            "SecurityMaster",
            "ATG.PricingEngine.Services.SecurityMaster");
        AssertWordDragSelection(
            "word drag inside original",
            "hello world",
            "hello",
            "ell",
            "hello");
        AssertNonWordDragSelection(
            "non-word drag right",
            "user=ana",
            "=",
            "ana",
            "=ana");
        AssertNonWordDragSelection(
            "non-word drag left",
            "user=ana",
            "=",
            "ser",
            "user=");
        AssertNonWordDragSelection(
            "non-word space drag right",
            "hello world",
            " ",
            "or",
            " world");
        AssertNonWordDragSelection(
            "non-word drag delimiter right",
            "user=ana,bob",
            "=",
            ",",
            "=ana,");
    }

    private static void RunNonWordDoubleClickSelection()
    {
        AssertSingleCharacterSelection("non-word equals", "user=ana", "=", "=");
        AssertSingleCharacterSelection("non-word space", "hello world", " ", " ");
        AssertSingleCharacterSelection("non-word quote", "{\"Level\":\"Info\"}", "\"", "\"");
        AssertSingleCharacterSelection("non-word comma", "a,b", ",", ",");
        AssertSingleCharacterSelectionAtIndex("non-word end clamps previous", "abc", 3, "c");
        AssertEqual(
            "non-word empty",
            ViewportPaneWindow.TryGetSingleCharacterSelection(string.Empty, 0, out _, out _),
            false);
    }

    private static void RunDisplayTextNormalization()
    {
        AssertEqual("display normalization valid text", ViewportPaneWindow.NormalizeDisplayText("abc-123"), "abc-123");
        AssertEqual("display normalization tab", ViewportPaneWindow.NormalizeDisplayText("a\tb"), "a b");
        AssertEqual("display normalization replacement", ViewportPaneWindow.NormalizeDisplayText("a\uFFFDb"), "a□b");
        AssertEqual("display normalization nul", ViewportPaneWindow.NormalizeDisplayText("a\0b"), "a□b");
        AssertEqual("display normalization control", ViewportPaneWindow.NormalizeDisplayText("a\u0001b"), "a□b");
        AssertEqual("display normalization high surrogate", ViewportPaneWindow.NormalizeDisplayText("a\uD800b"), "a□b");
        AssertEqual("display normalization low surrogate", ViewportPaneWindow.NormalizeDisplayText("a\uDC00b"), "a□b");
        string validSurrogatePair = "a\uD83D\uDE00b";
        string normalizedPair = ViewportPaneWindow.NormalizeDisplayText(validSurrogatePair);
        AssertEqual("display normalization valid surrogate pair", normalizedPair, validSurrogatePair);
        AssertEqual("display normalization length", ViewportPaneWindow.NormalizeDisplayText("a\t\uFFFD\u0001\uD800b").Length, 6);
    }

    private static void RunMultilineEditNormalization()
    {
        AssertEqual("multiline edit lf", MultilineEditText.Normalize("a\nb"), "a\r\nb");
        AssertEqual("multiline edit cr", MultilineEditText.Normalize("a\rb"), "a\r\nb");
        AssertEqual("multiline edit crlf", MultilineEditText.Normalize("a\r\nb"), "a\r\nb");
        AssertEqual(
            "multiline edit mixed",
            MultilineEditText.Normalize("a\rb\nc\r\nd"),
            "a\r\nb\r\nc\r\nd");

        DisplayParserStage replacement = new()
        {
            Mode = DisplayParserMode.RegexReplace,
            Rule = "x",
            Template = @"\n"
        };
        string replacementOutput = ParserStageEditorWindow.EvaluateStagePreview(
            "x",
            replacement,
            hasPreviousFilter: false);
        AssertEqual("replacement keeps parser lf", replacementOutput, "\n");
        AssertEqual("replacement preview renders crlf", MultilineEditText.Normalize(replacementOutput), "\r\n");

        DisplayParserStage display = new()
        {
            Mode = DisplayParserMode.Regex,
            Rule = "(.*)",
            Template = @"$0\n"
        };
        string displayOutput = ParserStageEditorWindow.EvaluateStagePreview(
            "x",
            display,
            hasPreviousFilter: false);
        AssertEqual("regex display keeps parser lf", displayOutput, "x\n");
        AssertEqual("regex display preview renders crlf", MultilineEditText.Normalize(displayOutput), "x\r\n");

        DisplayParserStage json = new()
        {
            Mode = DisplayParserMode.Json,
            Rule = @"${Value}\nnext"
        };
        string jsonOutput = ParserStageEditorWindow.EvaluateStagePreview(
            "{\"Value\":\"value\"}",
            json,
            hasPreviousFilter: false);
        AssertEqual("json template keeps parser lf", jsonOutput, "value\nnext");
        AssertEqual("json template preview renders crlf", MultilineEditText.Normalize(jsonOutput), "value\r\nnext");
    }

    private static void RunClipboardHeaders()
    {
        string[] headers = ["#", "Text", "Value"];
        AssertEqual(
            "clipboard all headers",
            ViewportPaneWindow.FormatClipboardHeader(headers),
            "#\tText\tValue");
        AssertEqual(
            "clipboard selected headers",
            ViewportPaneWindow.FormatClipboardHeader(headers, [1, 2]),
            "Text\tValue");
        AssertEqual(
            "clipboard ignores invalid header columns",
            ViewportPaneWindow.FormatClipboardHeader(headers, [-1, 2, 9]),
            "Value");
        AssertEqual(
            "clipboard preserves empty header position",
            ViewportPaneWindow.FormatClipboardHeader(["", "Text"]),
            "\tText");
        AssertEqual(
            "clipboard normalizes header tabs",
            ViewportPaneWindow.FormatClipboardHeader(["Header\tName"]),
            "Header Name");
    }

    private static void RunWindowStateStore(string tempRoot)
    {
        string path = Path.Combine(tempRoot, "window-state.json");
        WindowStateSettings settings = new()
        {
            Left = 12,
            Top = 34,
            Width = 1024,
            Height = 768,
            WindowState = WindowStateStore.MaximizedState
        };

        WindowStateStore.Save(path, settings);
        byte[] savedBytes = File.ReadAllBytes(path);
        AssertEqual("window state no bom", savedBytes.Length >= 3 && savedBytes[0] == 0xEF && savedBytes[1] == 0xBB && savedBytes[2] == 0xBF, false);

        WindowStateSettings? loaded = WindowStateStore.Load(path, _ => true);
        AssertEqual("window state loaded", loaded is not null, true);
        AssertEqual("window state left", loaded!.Left, settings.Left);
        AssertEqual("window state top", loaded.Top, settings.Top);
        AssertEqual("window state width", loaded.Width, settings.Width);
        AssertEqual("window state height", loaded.Height, settings.Height);
        AssertEqual("window state state", loaded.WindowState, settings.WindowState);

        AssertEqual(
            "window state monitor reject",
            WindowStateStore.Load(path, _ => false) is null,
            true);

        settings.Width = 20;
        WindowStateStore.Save(path, settings);
        AssertEqual("window state rejects small width", WindowStateStore.Load(path, _ => true) is null, true);

        settings.Width = 1024;
        settings.WindowState = "Minimized";
        WindowStateStore.Save(path, settings);
        AssertEqual("window state rejects unsupported state", WindowStateStore.Load(path, _ => true) is null, true);

        settings.Left = int.MaxValue;
        settings.WindowState = WindowStateStore.NormalState;
        WindowStateStore.Save(path, settings);
        AssertEqual("window state rejects overflow", WindowStateStore.Load(path, _ => true) is null, true);

        File.WriteAllText(path, "{not-json", Encoding.UTF8);
        AssertEqual("window state corrupt json", WindowStateStore.Load(path, _ => true) is null, true);
    }

    private static DisplayParserRule JsonParser(string template) => new()
    {
        Name = "smoke",
        Stages = new List<DisplayParserStage>
        {
            new()
            {
                Mode = DisplayParserMode.Json,
                Rule = template
            }
        }
    };

    private static string WriteLog(string tempRoot, string name, string content)
    {
        string path = Path.Combine(tempRoot, name);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
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

    private static void AssertByteSequence(string name, byte[] actual, byte[] expected)
    {
        AssertEqual(name + " length", actual.Length, expected.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            if (actual[i] != expected[i])
            {
                throw new InvalidOperationException($"{name} byte {i}: expected '{expected[i]}', got '{actual[i]}'.");
            }
        }
    }

    private static void AssertThrowsCancellation(string name, Action action)
    {
        try
        {
            action();
        }
        catch (OperationCanceledException)
        {
            return;
        }

        throw new InvalidOperationException(name + ": expected cancellation.");
    }

    private static void AssertWordSelection(string name, string text, string clickNeedle, string expected)
    {
        int charIndex = text.IndexOf(clickNeedle, StringComparison.Ordinal);
        if (charIndex < 0)
        {
            throw new InvalidOperationException(name + ": click needle not found.");
        }

        AssertEqual(
            name + " selected",
            ViewportPaneWindow.TryGetWordSelection(text, charIndex, out int start, out int end),
            true);
        AssertEqual(name, text.Substring(start, end - start), expected);
    }

    private static void AssertWordDragSelection(string name, string text, string initialNeedle, string focusNeedle, string expected)
    {
        int initialIndex = text.IndexOf(initialNeedle, StringComparison.Ordinal);
        if (initialIndex < 0)
        {
            throw new InvalidOperationException(name + ": initial needle not found.");
        }

        int focusIndex = text.IndexOf(focusNeedle, StringComparison.Ordinal);
        if (focusIndex < 0)
        {
            throw new InvalidOperationException(name + ": focus needle not found.");
        }

        if (!ViewportPaneWindow.TryGetWordSelection(text, initialIndex, out int wordStart, out int wordEnd))
        {
            throw new InvalidOperationException(name + ": initial word not found.");
        }

        ViewportPaneWindow.SnapWordSelectionForDrag(text, wordStart, wordEnd, focusIndex, out int anchor, out int focus);
        int start = Math.Min(anchor, focus);
        int end = Math.Max(anchor, focus);
        AssertEqual(name, text.Substring(start, end - start), expected);
    }

    private static void AssertNonWordDragSelection(string name, string text, string initialNeedle, string focusNeedle, string expected)
    {
        int initialIndex = text.IndexOf(initialNeedle, StringComparison.Ordinal);
        if (initialIndex < 0)
        {
            throw new InvalidOperationException(name + ": initial needle not found.");
        }

        int focusIndex = text.IndexOf(focusNeedle, StringComparison.Ordinal);
        if (focusIndex < 0)
        {
            throw new InvalidOperationException(name + ": focus needle not found.");
        }

        if (!ViewportPaneWindow.TryGetSingleCharacterSelection(text, initialIndex, out int charStart, out int charEnd))
        {
            throw new InvalidOperationException(name + ": initial character not found.");
        }

        ViewportPaneWindow.SnapWordSelectionForDrag(text, charStart, charEnd, focusIndex, out int anchor, out int focus);
        int start = Math.Min(anchor, focus);
        int end = Math.Max(anchor, focus);
        AssertEqual(name, text.Substring(start, end - start), expected);
    }

    private static void AssertSingleCharacterSelection(string name, string text, string clickNeedle, string expected)
    {
        int charIndex = text.IndexOf(clickNeedle, StringComparison.Ordinal);
        if (charIndex < 0)
        {
            throw new InvalidOperationException(name + ": click needle not found.");
        }

        AssertSingleCharacterSelectionAtIndex(name, text, charIndex, expected);
    }

    private static void AssertSingleCharacterSelectionAtIndex(string name, string text, int charIndex, string expected)
    {
        AssertEqual(
            name + " selected",
            ViewportPaneWindow.TryGetSingleCharacterSelection(text, charIndex, out int start, out int end),
            true);
        AssertEqual(name, text.Substring(start, end - start), expected);
    }

    private static void AssertEqual<T>(string name, T actual, T expected)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            throw new InvalidOperationException($"{name}: expected '{expected}', got '{actual}'.");
        }
    }

    private static void AssertNear(string name, double actual, double expected, double tolerance)
    {
        if (Math.Abs(actual - expected) > tolerance)
        {
            throw new InvalidOperationException($"{name}: expected '{expected}' +/- '{tolerance}', got '{actual}'.");
        }
    }

    private static void AssertSequence(string name, IReadOnlyList<string> actual, params string[] expected)
    {
        AssertEqual(name + " count", actual.Count, expected.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            AssertEqual(name + " [" + i + "]", actual[i], expected[i]);
        }
    }

    private static void AssertSelectedRows(
        string name,
        IReadOnlyList<ViewportSelectedRow> actual,
        params string[] expected)
    {
        AssertEqual(name + " count", actual.Count, expected.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            AssertEqual(name + " [" + i + "]", actual[i].Text, expected[i]);
        }
    }
}

internal sealed class CountingRecordSource : ILogRecordSource
{
    private readonly IReadOnlyList<LogViewportRecord> _records;
    private readonly List<LogViewportRecord> _current = new();
    private int _top;
    private int _windowCount;

    public CountingRecordSource(IReadOnlyList<LogViewportRecord> records)
    {
        _records = records;
    }

    public int NextCalls { get; private set; }
    public int PreviousCalls { get; private set; }
    public int LastNextCount { get; private set; }
    public string SourceName => "memory.log";
    public string EncodingName => "memory";
    public long DataOffset => 0;
    public long FileSize => _records.Count == 0 ? 0 : _records[^1].NextOffset;
    public long ConfirmedFileSize => FileSize;
    public long TopOffset => _current.Count == 0 ? 0 : _current[0].Key.StartOffset;
    public long ViewportBytes => _current.Count == 0 ? 0 : _current[^1].NextOffset - TopOffset;
    public double ScrollPercentage
    {
        get
        {
            int maxTop = Math.Max(0, _records.Count - Math.Max(1, _windowCount));
            return maxTop == 0 ? 0d : (_top * 100d) / maxTop;
        }
    }
    public bool HasContent => _records.Count > 0;
    public bool IsAtEnd => _top + _current.Count >= _records.Count;
    public int AnchorCharacterIndex => 0;
    public IReadOnlyList<string> ColumnHeaders => Array.Empty<string>();
    public IReadOnlyList<LogViewportRecord> CurrentRecords => _current;

    public IReadOnlyList<LogViewportRecord> ReadNextRecords(int count)
    {
        NextCalls++;
        LastNextCount = count;
        int maxTop = Math.Max(0, _records.Count - Math.Max(1, _windowCount));
        Load(Math.Min(maxTop, _top + Math.Max(1, count)), _windowCount);
        return CurrentRecords;
    }

    public IReadOnlyList<LogViewportRecord> ReadPreviousRecords(int count)
    {
        PreviousCalls++;
        Load(Math.Max(0, _top - Math.Max(1, count)), _windowCount);
        return CurrentRecords;
    }

    public IReadOnlyList<LogViewportRecord> ReadFromPercentage(double percentage, int count)
    {
        int windowCount = Math.Max(1, count);
        int maxTop = Math.Max(0, _records.Count - windowCount);
        int top = percentage >= 100d
            ? maxTop
            : (int)((Math.Clamp(percentage, 0d, 100d) / 100d) * maxTop);
        Load(top, windowCount);
        return CurrentRecords;
    }

    public IEnumerable<LogViewportRecord> EnumerateRecords(LogRecordKey? start, LogRecordKey? end)
    {
        for (int i = 0; i < _records.Count; i++)
        {
            LogViewportRecord record = _records[i];
            if (start.HasValue && record.Key.CompareTo(start.Value) < 0)
            {
                continue;
            }

            if (end.HasValue && record.Key.CompareTo(end.Value) > 0)
            {
                yield break;
            }

            yield return record;
        }
    }

    public ILogRecordSource CloneForWorker()
    {
        var clone = new CountingRecordSource(_records);
        clone.Load(_top, _windowCount);
        return clone;
    }

    public void ResetCalls()
    {
        NextCalls = 0;
        PreviousCalls = 0;
        LastNextCount = 0;
    }

    public void Dispose()
    {
        _current.Clear();
    }

    private void Load(int top, int count)
    {
        _top = Math.Clamp(top, 0, Math.Max(0, _records.Count - 1));
        _windowCount = Math.Max(1, count);
        _current.Clear();
        int end = Math.Min(_records.Count, _top + _windowCount);
        for (int i = _top; i < end; i++)
        {
            _current.Add(_records[i]);
        }
    }
}
