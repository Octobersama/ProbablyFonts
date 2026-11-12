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

        public static T SelectCompleteCandidate<T>(
            string text,
            TextScriptProfile profile,
            ScriptFontFamily family,
            IList<T> candidates,
            Func<T, int, bool> hasCharacter,
            out int totalCharacters,
            out int bestCoverage) where T : class
        {
            totalCharacters = CountFamilyCharacters(text, profile, family);
            bestCoverage = 0;
            if (totalCharacters == 0 || candidates == null || candidates.Count == 0)
            {
                return null;
            }

            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                T candidate = candidates[candidateIndex];
                if (candidate == null) continue;

                int coverage = CountFamilyCharacters(text, profile, family, candidate, hasCharacter);
                if (coverage > bestCoverage) bestCoverage = coverage;
                if (coverage == totalCharacters) return candidate;
            }

            return null;
        }

        public static int CollectFamilyCodePoints(
            string text,
            TextScriptProfile profile,
            ScriptFontFamily family,
            List<uint> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            if (string.IsNullOrEmpty(text)) return 0;

            for (int index = 0; index < text.Length; index++)
            {
                uint codePoint = text[index];
                if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                {
                    codePoint = (uint)char.ConvertToUtf32(text[index], text[++index]);
                }

                ScriptKind kind = Classify(codePoint);
                if (IsCjk(kind) && SelectFamily(kind, profile) == family && !output.Contains(codePoint))
                {
                    output.Add(codePoint);
                }
            }

            return output.Count;
        }

        public static int CountFamilyCharacters(string text, TextScriptProfile profile, ScriptFontFamily family)
        {
            return CountFamilyCharacters<object>(text, profile, family, null, null);
        }

        private static int CountFamilyCharacters<T>(
            string text,
            TextScriptProfile profile,
            ScriptFontFamily family,
            T candidate,
            Func<T, int, bool> hasCharacter)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            int count = 0;
            for (int index = 0; index < text.Length; index++)
            {
                uint codePoint = text[index];
                if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                {
                    codePoint = (uint)char.ConvertToUtf32(text[index], text[++index]);
                }

                ScriptKind kind = Classify(codePoint);
                if (!IsCjk(kind) || SelectFamily(kind, profile) != family) continue;
                if (hasCharacter == null || hasCharacter(candidate, (int)codePoint)) count++;
            }

            return count;
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
}
