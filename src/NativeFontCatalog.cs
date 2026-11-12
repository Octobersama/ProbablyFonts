using System.Collections.Generic;
using ProbablyFonts.Core;
using UnityEngine;
using TMPro;

namespace ProbablyFonts
{
    internal sealed class NativeFontCatalog
    {
        public readonly TMP_FontAsset Western;
        public readonly TMP_FontAsset Japanese;
        public readonly TMP_FontAsset Korean;
        public readonly TMP_FontAsset Chinese;
        public readonly TMP_FontAsset ChineseHandwritten;
        public readonly TMP_FontAsset KoreanHandwritten;
        public readonly TMP_FontAsset KoreanScienceFiction;
        public readonly TMP_FontAsset JapaneseHandwritten;
        public readonly TMP_FontAsset JapaneseScienceFiction;
        public readonly TMP_FontAsset NotoSans;
        public readonly TMP_FontAsset NotoSansNormal;
        public readonly TMP_FontAsset NotoSerif;
        public readonly TMP_FontAsset NotoSerifMedium;
        public readonly TMP_FontAsset Rubik;
        public readonly TMP_FontAsset Caveat;
        public readonly TMP_FontAsset Lubrifont;
        public readonly TMP_FontAsset Tektur;
        public readonly TMP_FontAsset TekturBold;
        public readonly TMP_FontAsset NotoSansBold;
        public readonly TMP_FontAsset NotoSerifBold;
        public readonly TMP_FontAsset PolishDisplay;

        public NativeFontCatalog(FontLoader loader)
        {
            Western = loader.westernFont;
            Japanese = loader.japaneseFont;
            Korean = loader.koreanFont;
            Chinese = loader.simplifiedChineseFont;
            ChineseHandwritten = loader.simplifiedChineseHandwrittenFont;
            KoreanHandwritten = loader.koreanHandwrittenFont;
            KoreanScienceFiction = loader.koreanScifiFont;
            JapaneseHandwritten = loader.japaneseHandwrittenFont;
            JapaneseScienceFiction = loader.japaneseScifiFont;
            NotoSans = loader.notoSans;
            NotoSansNormal = loader.notoSans_Normal;
            NotoSerif = loader.notoSerif;
            NotoSerifMedium = loader.notoSerif_Medium;
            Rubik = loader.rubik;
            Caveat = loader.caveat;
            Lubrifont = loader.lubrifont;
            Tektur = loader.tektur;
            TekturBold = loader.tektur_Bold;
            NotoSansBold = loader.notoSans_Bold;
            NotoSerifBold = loader.notoSerif_Bold;
            PolishDisplay = loader.polishDisplayFont;
        }

        public void GetSelectionInfo(
            TMP_Text text,
            TMP_FontAsset primaryFont,
            bool primaryIsInline,
            out NativeFontRole role,
            out TMP_FontAsset roleFont,
            out bool hasLocalizedRole)
        {
            if (primaryIsInline)
            {
                role = GetAssetRole(primaryFont);
                roleFont = primaryFont;
                hasLocalizedRole = false;
                return;
            }

            TMPLocalizer localizer = text == null ? null : text.GetComponent<TMPLocalizer>();
            hasLocalizedRole = localizer != null;
            if (ReferenceEquals(primaryFont, PolishDisplay))
            {
                role = NativeFontRole.Standard;
                roleFont = PolishDisplay;
                return;
            }

            if (localizer != null)
            {
                role = FromGameRole(localizer.fontRole);
                roleFont = GetRoleFont(localizer.fontRole);
                return;
            }

            role = GetAssetRole(primaryFont);
            roleFont = TryGetScriptFamily(primaryFont, out _) ? null : primaryFont;
        }

        public void FillCjkCandidates(
            TMP_FontAsset primaryFont,
            TMP_FontAsset roleFont,
            bool hasLocalizedRole,
            bool primaryIsInline,
            ScriptFontFamily family,
            NativeFontRole role,
            List<TMP_FontAsset> output)
        {
            output.Clear();
            if (primaryIsInline) Add(output, primaryFont);
            Add(output, roleFont);

            if (!primaryIsInline && TryGetScriptFamily(primaryFont, out ScriptFontFamily primaryFamily))
            {
                if (primaryFamily == family) Add(output, primaryFont);
            }
            else if (!primaryIsInline && !hasLocalizedRole)
            {
                Add(output, primaryFont);
            }

            RoleFace face = FontSelection.SelectRoleFace(role, family);
            switch (family)
            {
                case ScriptFontFamily.Chinese:
                    if (face == RoleFace.Handwritten) Add(output, ChineseHandwritten);
                    else if (face == RoleFace.ScienceFiction) Add(output, Lubrifont);
                    Add(output, Chinese);
                    Add(output, Japanese);
                    Add(output, Korean);
                    break;

                case ScriptFontFamily.Japanese:
                    if (face == RoleFace.Handwritten) Add(output, JapaneseHandwritten);
                    else if (face == RoleFace.ScienceFiction) Add(output, JapaneseScienceFiction);
                    Add(output, Japanese);
                    Add(output, Chinese);
                    Add(output, Korean);
                    break;

                case ScriptFontFamily.Korean:
                    if (face == RoleFace.Handwritten) Add(output, KoreanHandwritten);
                    else if (face == RoleFace.ScienceFiction) Add(output, KoreanScienceFiction);
                    Add(output, Korean);
                    Add(output, Chinese);
                    Add(output, Japanese);
                    break;
            }
        }

        private TMP_FontAsset GetRoleFont(FontLoader.FontRole role)
        {
            switch (role)
            {
                case FontLoader.FontRole.Noto_Sans: return NotoSans;
                case FontLoader.FontRole.Noto_Serif: return NotoSerif;
                case FontLoader.FontRole.Rubik: return Rubik;
                case FontLoader.FontRole.Caveat: return Caveat;
                case FontLoader.FontRole.Lubrifont: return Lubrifont;
                case FontLoader.FontRole.Tektur: return Tektur;
                case FontLoader.FontRole.Tektur_Bold: return TekturBold;
                case FontLoader.FontRole.Noto_Sans_Normal: return NotoSansNormal;
                case FontLoader.FontRole.Noto_Serif_Medium: return NotoSerifMedium;
                case FontLoader.FontRole.Noto_Sans_Bold: return NotoSansBold;
                case FontLoader.FontRole.Noto_Serif_Bold: return NotoSerifBold;
                default: return null;
            }
        }

        private bool TryGetScriptFamily(TMP_FontAsset font, out ScriptFontFamily family)
        {
            if (ReferenceEquals(font, Chinese) || ReferenceEquals(font, ChineseHandwritten))
            {
                family = ScriptFontFamily.Chinese;
                return true;
            }

            if (ReferenceEquals(font, Japanese) || ReferenceEquals(font, JapaneseHandwritten) ||
                ReferenceEquals(font, JapaneseScienceFiction))
            {
                family = ScriptFontFamily.Japanese;
                return true;
            }

            if (ReferenceEquals(font, Korean) || ReferenceEquals(font, KoreanHandwritten) ||
                ReferenceEquals(font, KoreanScienceFiction))
            {
                family = ScriptFontFamily.Korean;
                return true;
            }

            family = default;
            return false;
        }


        public void FillGlobalFallbacks(List<TMP_FontAsset> output)
        {
            output.Clear();
            AddUsable(output, Western);
            AddUsable(output, NotoSans);
            AddUsable(output, Japanese);
            AddUsable(output, Chinese);
            AddUsable(output, Korean);
        }

        public static bool HasFontData(TMP_FontAsset font)
        {
            if (font == null) return false;
            if (font.sourceFontFile != null || font.atlas != null) return true;

            Texture2D[] textures = font.atlasTextures;
            if (textures == null) return false;
            for (int index = 0; index < textures.Length; index++)
            {
                if (textures[index] != null) return true;
            }

            return false;
        }

        private NativeFontRole GetAssetRole(TMP_FontAsset font)
        {
            if (font == null) return NativeFontRole.Standard;
            if (ReferenceEquals(font, Caveat) || ReferenceEquals(font, ChineseHandwritten) ||
                ReferenceEquals(font, KoreanHandwritten) || ReferenceEquals(font, JapaneseHandwritten))
            {
                return NativeFontRole.Handwritten;
            }

            if (ReferenceEquals(font, Lubrifont) || ReferenceEquals(font, Tektur) ||
                ReferenceEquals(font, TekturBold) || ReferenceEquals(font, KoreanScienceFiction) ||
                ReferenceEquals(font, JapaneseScienceFiction))
            {
                return NativeFontRole.ScienceFiction;
            }

            return NativeFontRole.Standard;
        }

        private static NativeFontRole FromGameRole(FontLoader.FontRole role)
        {
            switch (role)
            {
                case FontLoader.FontRole.Caveat:
                    return NativeFontRole.Handwritten;
                case FontLoader.FontRole.Lubrifont:
                case FontLoader.FontRole.Tektur:
                case FontLoader.FontRole.Tektur_Bold:
                    return NativeFontRole.ScienceFiction;
                default:
                    return NativeFontRole.Standard;
            }
        }

        private static void AddUsable(List<TMP_FontAsset> output, TMP_FontAsset font)
        {
            if (HasFontData(font)) Add(output, font);
        }

        private static void Add(List<TMP_FontAsset> output, TMP_FontAsset font)
        {
            if (font == null) return;
            for (int index = 0; index < output.Count; index++)
            {
                if (ReferenceEquals(output[index], font)) return;
            }

            output.Add(font);
        }
    }
}
