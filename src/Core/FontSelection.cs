using System;
using System.Collections.Generic;

namespace ProbablyFonts.Core
{
    internal enum ScriptKind
    {
        Other,
        Latin,
        Cyrillic,
        Han,
        Kana,
        Hangul,
        Bopomofo,
        CommonCjk
    }

    internal enum ScriptFontFamily
    {
        Chinese,
        Japanese,
        Korean
    }

    internal enum NativeFontRole
    {
        Standard,
        Handwritten,
        ScienceFiction
    }

    internal enum RoleFace
    {
        Standard,
        Handwritten,
        ScienceFiction
    }

    internal readonly struct TextScriptProfile
    {
        public readonly int HanCount;
        public readonly int KanaCount;
        public readonly int HangulCount;
        public readonly int BopomofoCount;
        public readonly int CommonCjkCount;
        public readonly int LatinCount;
        public readonly int CyrillicCount;

        public TextScriptProfile(
            int hanCount,
            int kanaCount,
            int hangulCount,
            int bopomofoCount,
            int commonCjkCount,
            int latinCount,
            int cyrillicCount)
        {
            HanCount = hanCount;
            KanaCount = kanaCount;
            HangulCount = hangulCount;
            BopomofoCount = bopomofoCount;
            CommonCjkCount = commonCjkCount;
            LatinCount = latinCount;
            CyrillicCount = cyrillicCount;
        }

        public bool HasCjk => HanCount != 0 || KanaCount != 0 || HangulCount != 0 ||
                              BopomofoCount != 0 || CommonCjkCount != 0;
    }

    internal sealed class TextFamilyAnalysis
    {
        public static readonly TextFamilyAnalysis Empty =
            new TextFamilyAnalysis(0, Array.Empty<uint>(), Array.Empty<int>());

        public readonly int TotalCharacters;
        public readonly uint[] CodePoints;
        public readonly int[] CharacterCounts;

        public TextFamilyAnalysis(int totalCharacters, uint[] codePoints, int[] characterCounts)
        {
            TotalCharacters = totalCharacters;
            CodePoints = codePoints;
            CharacterCounts = characterCounts;
        }
    }

    internal sealed class TextScriptAnalysis
    {
        public readonly TextScriptProfile Profile;
        private readonly TextFamilyAnalysis chinese;
        private readonly TextFamilyAnalysis japanese;
        private readonly TextFamilyAnalysis korean;

        public TextScriptAnalysis(
            TextScriptProfile profile,
            TextFamilyAnalysis chinese,
            TextFamilyAnalysis japanese,
            TextFamilyAnalysis korean)
        {
            Profile = profile;
            this.chinese = chinese;
            this.japanese = japanese;
            this.korean = korean;
        }

        public TextFamilyAnalysis GetFamily(ScriptFontFamily family)
        {
            switch (family)
            {
                case ScriptFontFamily.Chinese: return chinese;
                case ScriptFontFamily.Japanese: return japanese;
                case ScriptFontFamily.Korean: return korean;
                default: return TextFamilyAnalysis.Empty;
            }
        }
    }

    internal sealed class RevisionedTextSelectionCache<TContext, TSelection>
        where TSelection : class
    {
        private readonly List<ContextSelection> selections = new List<ContextSelection>(2);
        private TextScriptAnalysis analysis;
        private int revision;
        private int generation;
        private bool hasAnalysis;

        public TSelection Get(
            string input,
            int inputRevision,
            int currentGeneration,
            TContext context,
            Func<string, TextScriptAnalysis> analyze,
            Func<TextScriptAnalysis, TContext, TSelection> select)
        {
            if (!hasAnalysis || revision != inputRevision || generation != currentGeneration)
            {
                TextScriptAnalysis nextAnalysis = analyze(input);
                analysis = nextAnalysis;
                revision = inputRevision;
                generation = currentGeneration;
                selections.Clear();
                hasAnalysis = true;
            }

            for (int index = 0; index < selections.Count; index++)
            {
                ContextSelection existing = selections[index];
                if (EqualityComparer<TContext>.Default.Equals(existing.Context, context))
                {
                    return existing.Selection;
                }
            }

            TSelection selection = select(analysis, context);
            selections.Add(new ContextSelection(context, selection));
            return selection;
        }

        private readonly struct ContextSelection
        {
            public readonly TContext Context;
            public readonly TSelection Selection;

            public ContextSelection(TContext context, TSelection selection)
            {
                Context = context;
                Selection = selection;
            }
        }
    }

    internal readonly struct FontWeightProfile
    {
        public readonly float Normal;
        public readonly float Bold;

        public FontWeightProfile(float normal, float bold)
        {
            Normal = normal;
            Bold = bold;
        }

        public static FontWeightProfile Match(float normal, float bold) => new FontWeightProfile(normal, bold);
    }

    internal static class FontWeightSelectionPolicy
    {
        public const int BoldAlternateWeight = 700;

        public static bool RequiresBoldAlternate(int requestedWeight)
        {
            return requestedWeight == BoldAlternateWeight;
        }
    }

    internal static class StartupPolicy
    {
        public static bool IsReady(bool fontLoaderReady, bool localizationReady, bool tmpSettingsReady)
        {
            return fontLoaderReady && localizationReady && tmpSettingsReady;
        }

        public static bool CanStartAfterOwnAssets(bool ownAssetsReady, bool fontLoaderReady, bool localizationReady, bool tmpSettingsReady)
        {
            return ownAssetsReady && IsReady(fontLoaderReady, localizationReady, tmpSettingsReady);
        }
    }

    internal static class FontSelection
    {
        public static ScriptKind Classify(uint codePoint)
        {
            if (IsKana(codePoint)) return ScriptKind.Kana;
            if (IsHangul(codePoint)) return ScriptKind.Hangul;
            if (IsBopomofo(codePoint)) return ScriptKind.Bopomofo;
            if (IsHan(codePoint)) return ScriptKind.Han;
            if (IsCommonCjk(codePoint)) return ScriptKind.CommonCjk;
            if (IsCyrillic(codePoint)) return ScriptKind.Cyrillic;
            if (IsLatin(codePoint)) return ScriptKind.Latin;
            return ScriptKind.Other;
        }

        public static bool IsCjk(uint codePoint)
        {
            ScriptKind kind = Classify(codePoint);
            return kind == ScriptKind.Han || kind == ScriptKind.Kana || kind == ScriptKind.Hangul ||
                   kind == ScriptKind.Bopomofo || kind == ScriptKind.CommonCjk;
        }

        public static TextScriptProfile Analyze(string text)
        {
            int han = 0;
            int kana = 0;
            int hangul = 0;
            int bopomofo = 0;
            int commonCjk = 0;
            int latin = 0;
            int cyrillic = 0;

            if (string.IsNullOrEmpty(text))
            {
                return new TextScriptProfile(0, 0, 0, 0, 0, 0, 0);
            }

            for (int index = 0; index < text.Length; index++)
            {
                uint codePoint = text[index];
                if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                {
                    codePoint = (uint)char.ConvertToUtf32(text[index], text[++index]);
                }

                switch (Classify(codePoint))
                {
                    case ScriptKind.Han: han++; break;
                    case ScriptKind.Kana: kana++; break;
                    case ScriptKind.Hangul: hangul++; break;
                    case ScriptKind.Bopomofo: bopomofo++; break;
                    case ScriptKind.CommonCjk: commonCjk++; break;
                    case ScriptKind.Latin: latin++; break;
                    case ScriptKind.Cyrillic: cyrillic++; break;
                }
            }

            return new TextScriptProfile(han, kana, hangul, bopomofo, commonCjk, latin, cyrillic);
        }

        public static ScriptFontFamily SelectFamily(ScriptKind kind, TextScriptProfile profile)
        {
            if (kind == ScriptKind.Kana) return ScriptFontFamily.Japanese;
            if (kind == ScriptKind.Hangul) return ScriptFontFamily.Korean;
            if (kind == ScriptKind.Bopomofo) return ScriptFontFamily.Chinese;

            if (profile.BopomofoCount != 0) return ScriptFontFamily.Chinese;
            if (profile.KanaCount > profile.HangulCount) return ScriptFontFamily.Japanese;
            if (profile.HangulCount > profile.KanaCount) return ScriptFontFamily.Korean;

            // Han-only text is ambiguous, so prefer one stable Chinese face rather than
            // changing glyph shapes when the game locale changes.
            return ScriptFontFamily.Chinese;
        }

        public static RoleFace SelectRoleFace(NativeFontRole role, ScriptFontFamily family)
        {
            if (role == NativeFontRole.Handwritten) return RoleFace.Handwritten;
            if (role == NativeFontRole.ScienceFiction) return RoleFace.ScienceFiction;
            return RoleFace.Standard;
        }

        public static TextScriptAnalysis AnalyzeText(string text)
        {
            string value = text ?? string.Empty;
            TextScriptProfile profile = Analyze(value);
            var frequencies = new Dictionary<uint, int>();
            var chineseCodePoints = new List<uint>();
            var japaneseCodePoints = new List<uint>();
            var koreanCodePoints = new List<uint>();
            int chineseCharacters = 0;
            int japaneseCharacters = 0;
            int koreanCharacters = 0;

            for (int index = 0; index < value.Length; index++)
            {
                uint codePoint = value[index];
                if (char.IsHighSurrogate(value[index]) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                {
                    codePoint = (uint)char.ConvertToUtf32(value[index], value[++index]);
                }

                ScriptKind kind = Classify(codePoint);
                if (!IsCjk(kind)) continue;

                switch (SelectFamily(kind, profile))
                {
                    case ScriptFontFamily.Chinese:
                        chineseCharacters++;
                        RecordCodePoint(codePoint, chineseCodePoints, frequencies);
                        break;
                    case ScriptFontFamily.Japanese:
                        japaneseCharacters++;
                        RecordCodePoint(codePoint, japaneseCodePoints, frequencies);
                        break;
                    case ScriptFontFamily.Korean:
                        koreanCharacters++;
                        RecordCodePoint(codePoint, koreanCodePoints, frequencies);
                        break;
                }
            }

            return new TextScriptAnalysis(
                profile,
                CreateFamilyAnalysis(chineseCodePoints, chineseCharacters, frequencies),
                CreateFamilyAnalysis(japaneseCodePoints, japaneseCharacters, frequencies),
                CreateFamilyAnalysis(koreanCodePoints, koreanCharacters, frequencies));
        }

        private static void RecordCodePoint(uint codePoint, List<uint> output, Dictionary<uint, int> frequencies)
        {
            if (frequencies.TryGetValue(codePoint, out int count))
            {
                frequencies[codePoint] = count + 1;
                return;
            }

            frequencies.Add(codePoint, 1);
            output.Add(codePoint);
        }

        private static TextFamilyAnalysis CreateFamilyAnalysis(
            List<uint> codePoints,
            int totalCharacters,
            Dictionary<uint, int> frequencies)
        {
            if (codePoints.Count == 0)
            {
                return TextFamilyAnalysis.Empty;
            }

            uint[] uniqueCodePoints = codePoints.ToArray();
            int[] characterCounts = new int[uniqueCodePoints.Length];
            for (int index = 0; index < uniqueCodePoints.Length; index++)
            {
                characterCounts[index] = frequencies[uniqueCodePoints[index]];
            }

            return new TextFamilyAnalysis(totalCharacters, uniqueCodePoints, characterCounts);
        }

        private static bool IsCjk(ScriptKind kind)
        {
            return kind == ScriptKind.Han || kind == ScriptKind.Kana || kind == ScriptKind.Hangul ||
                   kind == ScriptKind.Bopomofo || kind == ScriptKind.CommonCjk;
        }

        private static bool IsKana(uint codePoint)
        {
            return InRange(codePoint, 0x3040, 0x30FF) || InRange(codePoint, 0x31F0, 0x31FF) ||
                   InRange(codePoint, 0xFF66, 0xFF9D) || InRange(codePoint, 0x1B000, 0x1B16F);
        }

        private static bool IsHangul(uint codePoint)
        {
            return InRange(codePoint, 0x1100, 0x11FF) || InRange(codePoint, 0x3130, 0x318F) ||
                   InRange(codePoint, 0xA960, 0xA97F) || InRange(codePoint, 0xAC00, 0xD7AF) ||
                   InRange(codePoint, 0xD7B0, 0xD7FF);
        }

        private static bool IsBopomofo(uint codePoint)
        {
            return InRange(codePoint, 0x3100, 0x312F) || InRange(codePoint, 0x31A0, 0x31BF);
        }

        private static bool IsHan(uint codePoint)
        {
            return InRange(codePoint, 0x3400, 0x4DBF) || InRange(codePoint, 0x4E00, 0x9FFF) ||
                   InRange(codePoint, 0xF900, 0xFAFF) || InRange(codePoint, 0x20000, 0x2EBEF) ||
                   InRange(codePoint, 0x2EBF0, 0x2EE5D) || InRange(codePoint, 0x2F800, 0x2FA1F) ||
                   InRange(codePoint, 0x30000, 0x3134F) || InRange(codePoint, 0x31350, 0x323AF);
        }

        private static bool IsCommonCjk(uint codePoint)
        {
            return InRange(codePoint, 0x2E80, 0x2EFF) || InRange(codePoint, 0x2F00, 0x2FDF) ||
                   InRange(codePoint, 0x2FF0, 0x2FFF) || InRange(codePoint, 0x3000, 0x303F) ||
                   InRange(codePoint, 0x31C0, 0x31EF) || InRange(codePoint, 0xFE30, 0xFE4F) ||
                   InRange(codePoint, 0xFF00, 0xFF65) || InRange(codePoint, 0xFF9E, 0xFFEF);
        }

        private static bool IsCyrillic(uint codePoint)
        {
            return InRange(codePoint, 0x0400, 0x052F) || InRange(codePoint, 0x1C80, 0x1C8F) ||
                   InRange(codePoint, 0x2DE0, 0x2DFF) || InRange(codePoint, 0xA640, 0xA69F);
        }

        private static bool IsLatin(uint codePoint)
        {
            return InRange(codePoint, 0x0041, 0x005A) || InRange(codePoint, 0x0061, 0x007A) ||
                   InRange(codePoint, 0x00C0, 0x02AF) || InRange(codePoint, 0x1E00, 0x1EFF) ||
                   InRange(codePoint, 0xA720, 0xA7FF);
        }

        private static bool InRange(uint value, uint minimum, uint maximum)
        {
            return value >= minimum && value <= maximum;
        }
    }

    internal struct CompleteCandidateSelector<T> where T : class
    {
        private readonly TextFamilyAnalysis family;
        private readonly Func<T, int, bool> hasCharacter;

        public T Selected { get; private set; }
        public int TotalCharacters => family.TotalCharacters;
        public int BestCoverage { get; private set; }

        public CompleteCandidateSelector(TextFamilyAnalysis family, Func<T, int, bool> hasCharacter)
        {
            this.family = family;
            this.hasCharacter = hasCharacter;
        }

        public bool Consider(T candidate)
        {
            if (Selected != null) return true;
            if (candidate == null || TotalCharacters == 0) return false;

            int coverage = 0;
            for (int index = 0; index < family.CodePoints.Length; index++)
            {
                if (hasCharacter(candidate, (int)family.CodePoints[index]))
                {
                    coverage += family.CharacterCounts[index];
                }
            }

            if (coverage > BestCoverage) BestCoverage = coverage;
            if (coverage != TotalCharacters) return false;

            Selected = candidate;
            return true;
        }
    }
}
