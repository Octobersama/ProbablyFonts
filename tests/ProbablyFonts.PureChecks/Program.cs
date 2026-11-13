using System;
using System.Collections.Generic;
using System.Text;
using ProbablyFonts.Core;

internal static class Program
{
    private static int failures;

    private static int Main()
    {
        Run("script classification and stable Han choice", ScriptClassificationAndChoice);
        Run("role face selection", RoleFaceSelection);
        Run("explicit bold weight selects a weighted face", ExplicitBoldWeight);
        Run("complete candidate selection", CompleteCandidateSelection);
        Run("fallback deduplication and cycle exclusion", FallbackDeduplicationAndCycles);
        Run("owned fallback restoration", OwnedFallbackRestoration);
        Run("asynchronous startup gate", StartupGate);
        Run("mutable TMP inputs are snapshotted by accepted slice", MutableInputSnapshots);
        Run("revision and context text-selection cache", RevisionAndContextTextSelectionCache);
        return failures == 0 ? 0 : 1;
    }

    private static void MutableInputSnapshots()
    {
        var state = new TextInputSnapshot();
        char[] text = "L漢字R".ToCharArray();
        True(state.Capture(text, 1, 2), "array slice accepted");
        Equal("漢字", state.Text, "capture only the accepted slice");
        int firstRevision = state.Revision;
        text[1] = '中';
        Equal("漢字", state.Text, "later array mutation cannot change the active snapshot");
        True(state.Capture(text, 1, 2), "new array contents accepted");
        Equal("中字", state.Text, "capture sees the new input revision");
        True(state.Revision != firstRevision, "changed input invalidates cached selection");

        var builder = new StringBuilder("A日本語Z");
        True(state.Capture(builder, 1, 3), "builder slice accepted");
        builder[1] = '中';
        Equal("日本語", state.Text, "later builder mutation cannot change the snapshot");
        int unchangedRevision = state.Revision;
        True(state.Capture("日本語", 0, 3), "same immutable text accepted");
        Equal(unchangedRevision, state.Revision, "equal input does not invalidate cached selection");
        True(!state.Capture("short", 9, 1), "invalid range requests actual-buffer recovery");
        Equal("日本語", state.Text, "invalid capture cannot invent a different input");
    }

    private static void RevisionAndContextTextSelectionCache()
    {
        var cache = new RevisionedTextSelectionCache<string, SelectionResult>();
        int analysisCalls = 0;
        int selectionCalls = 0;

        TextScriptAnalysis Analyze(string input)
        {
            analysisCalls++;
            return FontSelection.AnalyzeText(input);
        }

        SelectionResult Select(TextScriptAnalysis analysis, string context)
        {
            selectionCalls++;
            return new SelectionResult(analysis, context);
        }

        SelectionResult body = cache.Get("漢字", 4, 10, "body", Analyze, Select);
        SelectionResult inline = cache.Get("漢字", 4, 10, "inline", Analyze, Select);
        Same(body, cache.Get("漢字", 4, 10, "body", Analyze, Select), "return to the cached body selection");
        True(!ReferenceEquals(body, inline), "different font contexts keep separate selections");
        Same(body.Analysis, inline.Analysis, "font contexts share the cached text analysis");
        Equal("body", body.Context, "body selection keeps its font context");
        Equal("inline", inline.Context, "inline selection keeps its font context");
        Equal(2, body.Analysis.Profile.HanCount, "cache uses production script analysis");
        Equal(1, analysisCalls, "font-context changes analyze the revision only once");
        Equal(2, selectionCalls, "each font context is selected once");

        SelectionResult revised = cache.Get("中文", 5, 10, "body", Analyze, Select);
        True(!ReferenceEquals(body, revised), "a new input revision drops prior selections");
        True(!ReferenceEquals(body.Analysis, revised.Analysis), "a new revision replaces the analysis");
        Equal(2, analysisCalls, "a new input revision is analyzed");
        Equal(3, selectionCalls, "a new revision selects its context again");

        SelectionResult regenerated = cache.Get("中文", 5, 11, "body", Analyze, Select);
        True(!ReferenceEquals(revised, regenerated), "a new generation drops prior selections");
        True(!ReferenceEquals(revised.Analysis, regenerated.Analysis), "a new generation replaces the analysis");
        Equal(3, analysisCalls, "a new generation is analyzed");
        Equal(4, selectionCalls, "a new generation selects its context again");
    }

    private static void ScriptClassificationAndChoice()
    {
        TextScriptProfile chinese = FontSelection.Analyze("文本条目");
        Equal(4, chinese.HanCount, "Han count");
        Equal(ScriptFontFamily.Chinese, FontSelection.SelectFamily(ScriptKind.Han, chinese), "Han-only family");

        TextScriptProfile japanese = FontSelection.Analyze("日本語かな");
        Equal(ScriptFontFamily.Japanese, FontSelection.SelectFamily(ScriptKind.Han, japanese), "Kana guides Han choice");
        Equal(ScriptFontFamily.Japanese, FontSelection.SelectFamily(ScriptKind.Kana, japanese), "Kana family");

        TextScriptProfile korean = FontSelection.Analyze("한국어한");
        Equal(ScriptFontFamily.Korean, FontSelection.SelectFamily(ScriptKind.Han, korean), "Hangul guides Han choice");
        Equal(ScriptFontFamily.Korean, FontSelection.SelectFamily(ScriptKind.Hangul, korean), "Hangul family");

        TextScriptProfile traditional = FontSelection.Analyze("ㄅ繁體中文");
        Equal(ScriptFontFamily.Chinese, FontSelection.SelectFamily(ScriptKind.Han, traditional), "Bopomofo guides Han choice");
        Equal(ScriptKind.Han, FontSelection.Classify(0x20000), "supplementary Han");
        Equal(ScriptKind.Han, FontSelection.Classify(0x31350), "newer supplementary Han");
        Equal(ScriptKind.CommonCjk, FontSelection.Classify(0x2E80), "CJK radicals");
        Equal(ScriptKind.Cyrillic, FontSelection.Classify('ё'), "Cyrillic extension");
        Equal(ScriptKind.Latin, FontSelection.Classify('é'), "accented Latin");
    }

    private static void RoleFaceSelection()
    {
        Equal(RoleFace.Handwritten,
            FontSelection.SelectRoleFace(NativeFontRole.Handwritten, ScriptFontFamily.Japanese),
            "Japanese handwritten role");
        Equal(RoleFace.ScienceFiction,
            FontSelection.SelectRoleFace(NativeFontRole.ScienceFiction, ScriptFontFamily.Korean),
            "Korean science-fiction role");
        Equal(RoleFace.ScienceFiction,
            FontSelection.SelectRoleFace(NativeFontRole.ScienceFiction, ScriptFontFamily.Chinese),
            "Chinese science-fiction role uses native Lubrifont");
        Equal(RoleFace.Standard,
            FontSelection.SelectRoleFace(NativeFontRole.Standard, ScriptFontFamily.Chinese),
            "standard role");
    }

    private static void ExplicitBoldWeight()
    {
        True(FontWeightSelectionPolicy.RequiresBoldAlternate(700), "bold weight works without a separate style flag");
        True(!FontWeightSelectionPolicy.RequiresBoldAlternate(400), "regular text keeps its normal-weight face");
    }

    private static void CompleteCandidateSelection()
    {
        TextScriptAnalysis analysis = FontSelection.AnalyzeText("文文本文");
        TextFamilyAnalysis family = analysis.GetFamily(ScriptFontFamily.Chinese);
        Equal(4, family.TotalCharacters, "repeated script characters are counted for coverage");
        Equal(2, family.CodePoints.Length, "font preparation uses unique codepoints");
        Equal((uint)'文', family.CodePoints[0], "unique codepoints retain input order");
        Equal(3, family.CharacterCounts[0], "coverage retains repeated-character weight");

        TextScriptAnalysis japanese = FontSelection.AnalyzeText("日本かな");
        Equal(4, japanese.GetFamily(ScriptFontFamily.Japanese).TotalCharacters, "Kana selects Japanese for Han");
        Equal(0, japanese.GetFamily(ScriptFontFamily.Chinese).TotalCharacters, "Japanese text does not enter Chinese family");
        TextScriptAnalysis korean = FontSelection.AnalyzeText("漢한국");
        Equal(3, korean.GetFamily(ScriptFontFamily.Korean).TotalCharacters, "Hangul selects Korean for Han");
        TextScriptAnalysis traditional = FontSelection.AnalyzeText("ㄅ繁體");
        Equal(3, traditional.GetFamily(ScriptFontFamily.Chinese).TotalCharacters, "Bopomofo keeps traditional text Chinese");

        FontCandidate partial = new FontCandidate('文');
        FontCandidate complete = new FontCandidate('文', '本');
        FontCandidate laterComplete = new FontCandidate('文', '本');
        int characterProbes = 0;
        bool ProbeCharacter(FontCandidate candidate, int codePoint)
        {
            characterProbes++;
            return HasCharacter(candidate, codePoint);
        }

        var selector = new CompleteCandidateSelector<FontCandidate>(family, ProbeCharacter);
        True(!selector.Consider(partial), "keep searching after an incomplete face");
        Equal(2, characterProbes, "coverage checks unique codepoints rather than repeated text characters");
        True(selector.Consider(complete), "stop on a complete face");
        True(selector.Consider(laterComplete), "the selector remains complete");
        Same(complete, selector.Selected, "the first complete candidate wins");
        Equal(4, selector.TotalCharacters, "coverage denominator includes repeated codepoints");
        Equal(4, selector.BestCoverage, "complete coverage is retained");
        Equal(4, characterProbes, "the first complete candidate is the last candidate examined");

        selector = new CompleteCandidateSelector<FontCandidate>(family, HasCharacter);
        True(!selector.Consider(partial), "do not select an incomplete face");
        Same(null, selector.Selected, "no incomplete face is selected");
        Equal(3, selector.BestCoverage, "best incomplete coverage is retained");
    }

    private static void FallbackDeduplicationAndCycles()
    {
        var graph = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["current"] = new[] { "current-child" },
            ["safe"] = new[] { "safe-child" },
            ["cycle-a"] = new[] { "cycle-b" },
            ["cycle-b"] = new[] { "cycle-a" }
        };
        IEnumerable<string> Children(string value)
        {
            return graph.TryGetValue(value, out string[] children) ? children : Array.Empty<string>();
        }

        var output = new List<string>();
        FallbackPlanner.BuildOwnedOrder(
            new[] { "current" },
            new[] { "current-child", "safe", "safe", "safe-child", "cycle-a" },
            Children,
            output,
            StringComparer.Ordinal);

        Equal(2, output.Count, "flattened new roots must not hide a child that was not appended");
        Equal("safe", output[0], "preserve candidate order after filtering");
        Equal("safe-child", output[1], "the child's original parent loses its local graph in the owned copy");
    }

    private static void OwnedFallbackRestoration()
    {
        var original = new object();
        var owned = new object();
        var list = new List<object> { original };
        var entries = new OwnedListEntries<object>();

        entries.AddMissing(list, new[] { owned, original, owned });
        Equal(2, list.Count, "add a unique owned reference");
        Same(owned, list[1], "owned reference appended");

        list.Add(owned);
        entries.Restore(list);
        Equal(1, list.Count, "remove every occurrence of an owned reference");
        Same(original, list[0], "preserve the pre-existing entry");

        var oldList = new List<object> { original };
        entries.AddMissing(oldList, new[] { owned });
        entries.Unmount(oldList);
        Equal(1, oldList.Count, "suspend removes the owned mount");
        Equal(1, entries.Count, "suspend retains ownership until reconciliation or stop");

        var replacementList = new List<object> { original, owned };
        var nextOwned = new object();
        entries.AddMissing(replacementList, new[] { owned, nextOwned });
        Equal(3, replacementList.Count, "remount existing and new owned assets without duplicating an existing reference");
        Same(owned, replacementList[1], "the previous generation clone can be remounted");
        Same(nextOwned, replacementList[2], "the current generation clone is mounted");
        Equal(2, entries.Count, "ownership includes assets from both generations");
        True(entries.HasMissingFrom(oldList), "missing active mounts are detected despite retained ownership");
        True(!entries.HasMissingFrom(replacementList), "remounted assets satisfy the active mount check");

        var reconciledList = new List<object> { original };
        entries.Unmount(replacementList, reconciledList);
        Equal(1, replacementList.Count, "reconciliation unmounts the previous list");
        Equal(1, reconciledList.Count, "track a new list before it contains an owned clone");
        True(!entries.HasMissingFrom(reconciledList), "unmounting historical assets clears active mount expectations");

        var otherModsFont = new object();
        reconciledList.Add(owned);
        reconciledList.Add(nextOwned);
        reconciledList.Add(otherModsFont);
        entries.Restore(replacementList, reconciledList);
        Equal(1, oldList.Count, "final restore cleans the original fallback list");
        Equal(1, replacementList.Count, "final restore keeps the reconciled list clean");
        Equal(2, reconciledList.Count, "final restore removes reinserted old and current clones");
        Same(original, reconciledList[0], "preserve the original font");
        Same(otherModsFont, reconciledList[1], "preserve another mod's font");
        Equal(0, entries.Count, "final restore forgets ownership only after cleanup");
    }

    private static void StartupGate()
    {
        True(!StartupPolicy.CanStartAfterOwnAssets(false, true, true, true), "wait for this mod's assets-ready callback");
        True(!StartupPolicy.CanStartAfterOwnAssets(true, false, true, true), "wait before font loader");
        True(!StartupPolicy.CanStartAfterOwnAssets(true, true, false, true), "wait before localization");
        True(!StartupPolicy.CanStartAfterOwnAssets(true, true, true, false), "wait before TMP settings");
        True(StartupPolicy.CanStartAfterOwnAssets(true, true, true, true), "proceed when all prerequisites are ready");
    }

    private static bool HasCharacter(FontCandidate candidate, int codePoint)
    {
        return candidate.Characters.Contains((uint)codePoint);
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
        }
        catch (Exception exception)
        {
            failures++;
            Console.Error.WriteLine(name + ": " + exception.Message);
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(message + " (expected " + expected + ", got " + actual + ")");
        }
    }

    private static void Same(object expected, object actual, string message)
    {
        if (!ReferenceEquals(expected, actual)) throw new InvalidOperationException(message);
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FontCandidate
    {
        public readonly HashSet<uint> Characters = new HashSet<uint>();

        public FontCandidate(params char[] characters)
        {
            foreach (char character in characters) Characters.Add(character);
        }
    }

    private sealed class SelectionResult
    {
        public readonly TextScriptAnalysis Analysis;
        public readonly string Context;

        public SelectionResult(TextScriptAnalysis analysis, string context)
        {
            Analysis = analysis;
            Context = context;
        }
    }
}
