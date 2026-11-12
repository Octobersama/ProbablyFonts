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
        const string text = "文本条目";
        TextScriptProfile profile = FontSelection.Analyze(text);
        FontCandidate partial = new FontCandidate('文', '本', '条');
        FontCandidate complete = new FontCandidate('文', '本', '条', '目');
        var candidates = new List<FontCandidate> { partial, complete };

        FontCandidate selected = FontSelection.SelectCompleteCandidate(
            text, profile, ScriptFontFamily.Chinese, candidates, HasCharacter,
            out int total, out int bestCoverage);

        Same(complete, selected, "choose one complete face instead of a partial primary");
        Equal(4, total, "characters required for the script group");
        Equal(4, bestCoverage, "complete coverage");

        candidates.RemoveAt(1);
        selected = FontSelection.SelectCompleteCandidate(
            text, profile, ScriptFontFamily.Chinese, candidates, HasCharacter,
            out total, out bestCoverage);
        Same(null, selected, "do not choose an incomplete face");
        Equal(3, bestCoverage, "best incomplete coverage reported");
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

        list.Add(owned); // Another modifier may independently retain the same reference.
        entries.Restore(list);
        Equal(2, list.Count, "remove only one owned occurrence");
        Same(original, list[0], "preserve the pre-existing entry");
        Same(owned, list[1], "preserve the other modifier's occurrence");
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
}
