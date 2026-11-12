using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ProbablyFonts.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;

namespace ProbablyFonts
{
    internal static class FontRuntime
    {
        private static readonly OwnedListEntries<TMP_FontAsset> ownedGlobalFallbacks = new OwnedListEntries<TMP_FontAsset>();
        private static readonly List<TMP_FontAsset> proposedGlobalFallbacks = new List<TMP_FontAsset>(8);
        private static readonly List<TMP_FontAsset> filteredGlobalFallbacks = new List<TMP_FontAsset>(8);
        private static readonly List<TMP_FontAsset> ownedGlobalAssets = new List<TMP_FontAsset>(8);
        private static readonly List<TMP_FontAsset> nativeCandidateScratch = new List<TMP_FontAsset>(8);
        private static readonly List<TMP_FontAsset> faceCandidateScratch = new List<TMP_FontAsset>(16);
        private static readonly List<uint> codePointScratch = new List<uint>(16);
        private static readonly HashSet<TMP_FontAsset> warnedAssets = new HashSet<TMP_FontAsset>(ReferenceEqualityComparer<TMP_FontAsset>.Instance);
        private static readonly bool[] warnedIncompleteBoldFace = new bool[9];
        private static readonly bool[] warnedIncompleteFace = new bool[9];
        private static readonly Func<TMP_FontAsset, int, bool> hasCharacter = HasCharacter;
        private static readonly Func<TMP_FontAsset, IEnumerable<TMP_FontAsset>> getFallbacks = GetFallbacks;
        private static readonly IEqualityComparer<TMP_FontAsset> fontComparer = ReferenceEqualityComparer<TMP_FontAsset>.Instance;

        private static ConditionalWeakTable<TMP_Text, TextSelectionCache> textSelections =
            new ConditionalWeakTable<TMP_Text, TextSelectionCache>();
        private static uint[] codePointArray = Array.Empty<uint>();
        private static FontCloneStore clones = new FontCloneStore();
        private static FontLoader fontLoader;
        private static Locale locale;
        private static TMP_Settings tmpSettings;
        private static NativeFontCatalog catalog;
        private static List<TMP_FontAsset> globalFallbackList;
        private static ModLog log;
        private static int generation;
        private static bool active;
        private static bool ready;
        private static bool prefixFailureLogged;
        private static bool refreshQueued;
        private static bool refreshFailureLogged;

        public static bool IsReady => active && ready;

        public static void Start(FontLoader loader, Locale selectedLocale, TMP_Settings settings, ModLog modLog)
        {
            FontInputSnapshots.Start();
            active = true;
            log = modLog;
            Reconcile(loader, selectedLocale, settings);
        }

        public static void Suspend()
        {
            ready = false;
            generation++;
            ownedGlobalFallbacks.Restore(globalFallbackList);
            RefreshExistingText();
            globalFallbackList = null;
            ownedGlobalAssets.Clear();
            fontLoader = null;
            locale = null;
            tmpSettings = null;
            catalog = null;
            textSelections = new ConditionalWeakTable<TMP_Text, TextSelectionCache>();
        }

        public static void Reconcile(
            FontLoader loader,
            Locale selectedLocale,
            TMP_Settings settings,
            bool refreshExistingText = true)
        {
            if (!active) return;
            if (!StartupPolicy.IsReady(loader != null, selectedLocale != null, settings != null))
            {
                ready = false;
                return;
            }

            ownedGlobalFallbacks.Restore(globalFallbackList);
            globalFallbackList = TMP_Settings.fallbackFontAssets;
            fontLoader = loader;
            locale = selectedLocale;
            tmpSettings = settings;
            catalog = new NativeFontCatalog(loader);
            generation++;
            textSelections = new ConditionalWeakTable<TMP_Text, TextSelectionCache>();

            proposedGlobalFallbacks.Clear();
            filteredGlobalFallbacks.Clear();
            ownedGlobalAssets.Clear();
            if (globalFallbackList != null)
            {
                catalog.FillGlobalFallbacks(proposedGlobalFallbacks);
                FallbackPlanner.BuildOwnedOrder(
                    globalFallbackList,
                    proposedGlobalFallbacks,
                    getFallbacks,
                    filteredGlobalFallbacks,
                    fontComparer);

                for (int index = 0; index < filteredGlobalFallbacks.Count; index++)
                {
                    TMP_FontAsset source = filteredGlobalFallbacks[index];
                    try
                    {
                        FontWeightProfile nativeWeights = ReadNativeWeights(source);
                        TMP_FontAsset staticCopy = clones.GetStaticCopy(source, nativeWeights, normalizeSyntheticWeights: false);
                        AddUnique(ownedGlobalAssets, staticCopy);

                        bool dynamicFallback = source.atlasPopulationMode == AtlasPopulationMode.Dynamic ||
                                               ReferenceEquals(source, catalog.Western) || ReferenceEquals(source, catalog.NotoSans);
                        if (dynamicFallback && source.sourceFontFile != null)
                        {
                            TMP_FontAsset dynamicCopy = clones.GetDynamicCopy(source, nativeWeights, normalizeSyntheticWeights: false);
                            AddUnique(ownedGlobalAssets, dynamicCopy);
                        }
                    }
                    catch (Exception exception)
                    {
                        WarnAssetFailure(source, exception);
                    }
                }

                ownedGlobalFallbacks.AddMissing(globalFallbackList, ownedGlobalAssets);
            }

            ready = true;
            if (refreshExistingText) RefreshExistingText();
        }

        public static void Stop()
        {
            active = false;
            ready = false;
            ownedGlobalFallbacks.Restore(globalFallbackList);
            RefreshExistingText();
            clones.Release();
            FontInputSnapshots.Stop();
            textSelections = new ConditionalWeakTable<TMP_Text, TextSelectionCache>();
            proposedGlobalFallbacks.Clear();
            ownedGlobalAssets.Clear();
            filteredGlobalFallbacks.Clear();
            nativeCandidateScratch.Clear();
            faceCandidateScratch.Clear();
            codePointScratch.Clear();
            warnedAssets.Clear();
            Array.Clear(warnedIncompleteFace, 0, warnedIncompleteFace.Length);
            Array.Clear(warnedIncompleteBoldFace, 0, warnedIncompleteBoldFace.Length);
            codePointArray = Array.Empty<uint>();
            globalFallbackList = null;
            fontLoader = null;
            locale = null;
            tmpSettings = null;
            catalog = null;
            log = null;
            generation++;
            prefixFailureLogged = false;
            refreshQueued = false;
            refreshFailureLogged = false;
        }

        public static bool TryGetCharacter(
            TMP_Text text,
            uint codePoint,
            TMP_FontAsset primaryFont,
            FontStyles fontStyle,
            FontWeight fontWeight,
            out TMP_TextElement result,
            out bool alternateTypeface)
        {
            result = null;
            alternateTypeface = false;
            if (!IsReady || text == null || primaryFont == null || !FontSelection.IsCjk(codePoint)) return false;

            EnsureCurrent();
            if (!IsReady || catalog == null) return false;
            if (!FontInputSnapshots.TryGet(text, out string input, out int inputRevision)) return false;

            bool primaryIsInline = !ReferenceEquals(primaryFont, text.font);
            catalog.GetSelectionInfo(
                text,
                primaryFont,
                primaryIsInline,
                out NativeFontRole role,
                out TMP_FontAsset roleFont,
                out bool hasLocalizedRole);
            TextSelectionCache selection = GetTextSelection(
                text, input, inputRevision, primaryFont, role, roleFont, hasLocalizedRole, primaryIsInline);
            ScriptFontFamily family = FontSelection.SelectFamily(FontSelection.Classify(codePoint), selection.Profile);
            TMP_FontAsset selectedFont = selection.GetFont(family);
            if (selectedFont == null) return false;

            if (FontWeightSelectionPolicy.RequiresBoldAlternate((int)fontWeight))
            {
                if (!PrepareBoldAlternate(selection, family))
                {
                    WarnIncompleteBoldFace(selection.Role, family);
                    return false;
                }

                // A synthetic bold copy is still a regular face; TMP must retain synthetic italic.
                selectedFont = selection.GetBoldFont(family);
            }

            TMP_Character character = TMP_FontAssetUtilities.GetCharacterFromFontAsset(
                codePoint,
                selectedFont,
                false,
                fontStyle,
                fontWeight,
                out alternateTypeface);
            if (character == null) return false;

            result = character;
            return true;
        }

        private static bool PrepareBoldAlternate(TextSelectionCache selection, ScriptFontFamily family)
        {
            if (selection.IsBoldPrepared(family)) return selection.GetBoldFont(family) != null;

            codePointScratch.Clear();
            int uniqueCount = FontSelection.CollectFamilyCodePoints(
                selection.Text,
                selection.Profile,
                family,
                codePointScratch);
            if (uniqueCount == 0)
            {
                selection.SetBoldFont(family, null);
                return false;
            }

            if (codePointArray.Length != uniqueCount) codePointArray = new uint[uniqueCount];
            codePointScratch.CopyTo(codePointArray, 0);
            TMP_FontAsset regularFace = selection.GetFont(family);
            TMP_FontAsset alternate = clones.GetBoldAlternate(regularFace, ReadNativeWeights(regularFace).Bold, codePointArray);
            selection.SetBoldFont(family, alternate);
            return alternate != null;
        }


        public static bool TryGetFallbackMaterial(
            Material sourceMaterial,
            Material targetMaterial,
            out Material fallbackMaterial)
        {
            fallbackMaterial = null;
            return IsReady && clones.TryGetFallbackMaterial(sourceMaterial, targetMaterial, out fallbackMaterial);
        }

        public static bool TryGetFallbackMaterial(
            TMP_FontAsset fontAsset,
            Material sourceMaterial,
            int atlasIndex,
            out Material fallbackMaterial)
        {
            fallbackMaterial = null;
            return IsReady && clones.TryGetFallbackMaterial(fontAsset, sourceMaterial, atlasIndex, out fallbackMaterial);
        }

        public static void ReportPrefixFailure(Exception exception)
        {
            if (prefixFailureLogged) return;
            prefixFailureLogged = true;
            if (log != null)
            {
                log.Warning("The font compatibility patch fell back to native TMP after " + exception.GetType().Name + ".");
            }
        }

        private static void EnsureCurrent()
        {
            FontLoader currentLoader = FontLoader.Instance;
            Locale currentLocale = LocalizationSettings.SelectedLocale;
            TMP_Settings currentSettings = TMP_Settings.GetSettings();
            if (!StartupPolicy.IsReady(currentLoader != null, currentLocale != null, currentSettings != null))
            {
                ready = false;
                return;
            }

            List<TMP_FontAsset> currentFallbacks = TMP_Settings.fallbackFontAssets;
            if (!ReferenceEquals(currentLoader, fontLoader) || !ReferenceEquals(currentLocale, locale) ||
                !ReferenceEquals(currentSettings, tmpSettings) || !ReferenceEquals(currentFallbacks, globalFallbackList) ||
                ownedGlobalFallbacks.HasMissingFrom(currentFallbacks))
            {
                Reconcile(currentLoader, currentLocale, currentSettings, refreshExistingText: false);
                QueueMeshRefresh();
            }
        }
        public static void QueueMeshRefresh()
        {
            if (!active || !ready || refreshQueued) return;
            refreshQueued = true;
            if (ModHelper.StartCoroutine("probably_fonts", RefreshAfterFrame()) == null)
            {
                refreshQueued = false;
                if (log != null) log.Warning("The text refresh coroutine could not start.");
            }
        }

        private static IEnumerator RefreshAfterFrame()
        {
            yield return null;
            refreshQueued = false;
            if (active && ready) RefreshExistingText();
        }

        private static void RefreshExistingText()
        {
            TMP_Text[] texts = Resources.FindObjectsOfTypeAll<TMP_Text>();
            for (int index = 0; index < texts.Length; index++)
            {
                TMP_Text text = texts[index];
                if (text == null || text.gameObject == null) continue;
                Scene scene = text.gameObject.scene;
                if (!scene.IsValid() || !scene.isLoaded) continue;

                try
                {
                    text.ForceMeshUpdate(true, true);
                }
                catch (Exception exception)
                {
                    if (refreshFailureLogged) continue;
                    refreshFailureLogged = true;
                    if (log != null) log.Warning("A TMP text mesh could not be refreshed (" + exception.GetType().Name + ").");
                }
            }
        }

        private static TextSelectionCache GetTextSelection(
            TMP_Text text,
            string input,
            int inputRevision,
            TMP_FontAsset primaryFont,
            NativeFontRole role,
            TMP_FontAsset roleFont,
            bool hasLocalizedRole,
            bool primaryIsInline)
        {
            string value = input;
            TextSelectionCache selection;
            if (!textSelections.TryGetValue(text, out selection))
            {
                selection = new TextSelectionCache();
                textSelections.Add(text, selection);
            }

            if (selection.Matches(value, inputRevision, primaryFont, role, roleFont, hasLocalizedRole, primaryIsInline, generation))
            {
                return selection;
            }

            selection.SetSource(value, inputRevision, primaryFont, generation, primaryIsInline);
            selection.Role = role;
            selection.RoleFont = roleFont;
            selection.HasLocalizedRole = hasLocalizedRole;
            selection.Profile = FontSelection.Analyze(value);
            selection.Chinese = SelectFontForFamily(selection, ScriptFontFamily.Chinese);
            selection.Japanese = SelectFontForFamily(selection, ScriptFontFamily.Japanese);
            selection.Korean = SelectFontForFamily(selection, ScriptFontFamily.Korean);
            return selection;
        }


        private static TMP_FontAsset SelectFontForFamily(TextSelectionCache selection, ScriptFontFamily family)
        {
            int totalCharacters = FontSelection.CountFamilyCharacters(selection.Text, selection.Profile, family);
            if (totalCharacters == 0) return null;

            int uniqueCount = FontSelection.CollectFamilyCodePoints(
                selection.Text,
                selection.Profile,
                family,
                codePointScratch);
            if (uniqueCount == 0) return null;

            if (codePointArray.Length != uniqueCount)
            {
                codePointArray = new uint[uniqueCount];
            }

            codePointScratch.CopyTo(codePointArray, 0);
            catalog.FillCjkCandidates(
                selection.PrimaryFont,
                selection.RoleFont,
                selection.HasLocalizedRole,
                selection.PrimaryIsInline,
                family,
                selection.Role,
                nativeCandidateScratch);
            faceCandidateScratch.Clear();

            for (int sourceIndex = 0; sourceIndex < nativeCandidateScratch.Count; sourceIndex++)
            {
                TMP_FontAsset source = nativeCandidateScratch[sourceIndex];
                if (!NativeFontCatalog.HasFontData(source)) continue;

                try
                {
                    FontWeightProfile nativeWeights = ReadNativeWeights(source);
                    TMP_FontAsset staticCopy = clones.GetStaticCopy(source, nativeWeights);
                    AddUnique(faceCandidateScratch, staticCopy);
                    if (CountCovered(staticCopy, codePointArray) == uniqueCount) break;
                    if (source.sourceFontFile == null) continue;

                    TMP_FontAsset dynamicCopy = clones.GetDynamicCopy(source, nativeWeights);
                    if (dynamicCopy == null) continue;
                    dynamicCopy.TryAddCharacters(codePointArray, false);
                    AddUnique(faceCandidateScratch, dynamicCopy);
                    if (CountCovered(dynamicCopy, codePointArray) == uniqueCount) break;
                }
                catch (Exception exception)
                {
                    WarnAssetFailure(source, exception);
                }
            }

            TMP_FontAsset selected = FontSelection.SelectCompleteCandidate(
                selection.Text,
                selection.Profile,
                family,
                faceCandidateScratch,
                hasCharacter,
                out _,
                out int bestCoverage);

            if (selected == null)
            {
                WarnIncompleteFace(selection.Role, family, bestCoverage, totalCharacters);
            }

            return selected;
        }

        private static int CountCovered(TMP_FontAsset font, uint[] codePoints)
        {
            if (font == null) return 0;
            int covered = 0;
            for (int index = 0; index < codePoints.Length; index++)
            {
                if (font.HasCharacter((int)codePoints[index])) covered++;
            }

            return covered;
        }

        private static void AddUnique(List<TMP_FontAsset> fonts, TMP_FontAsset candidate)
        {
            if (candidate == null) return;
            for (int index = 0; index < fonts.Count; index++)
            {
                if (ReferenceEquals(fonts[index], candidate)) return;
            }

            fonts.Add(candidate);
        }

        private static FontWeightProfile ReadNativeWeights(TMP_FontAsset font)
        {
            float normal = font.normalStyle;
            float bold = font.boldStyle;
            Material sourceMaterial = font.material;
            if (sourceMaterial != null)
            {
                if (sourceMaterial.HasProperty(ShaderUtilities.ID_WeightNormal))
                {
                    normal = sourceMaterial.GetFloat(ShaderUtilities.ID_WeightNormal);
                }

                if (sourceMaterial.HasProperty(ShaderUtilities.ID_WeightBold))
                {
                    bold = sourceMaterial.GetFloat(ShaderUtilities.ID_WeightBold);
                }
            }

            return FontWeightProfile.Match(normal, bold);
        }

        private static bool HasCharacter(TMP_FontAsset font, int codePoint)
        {
            return font != null && font.HasCharacter(codePoint);
        }

        private static IEnumerable<TMP_FontAsset> GetFallbacks(TMP_FontAsset font)
        {
            return font == null ? null : font.fallbackFontAssetTable;
        }

        private static void WarnAssetFailure(TMP_FontAsset source, Exception exception)
        {
            if (source == null || !warnedAssets.Add(source)) return;
            if (log != null)
            {
                log.Warning("Could not prepare native font '" + source.name + "' for CJK fallback (" + exception.GetType().Name + ").");
            }
        }

        private static void WarnIncompleteBoldFace(NativeFontRole role, ScriptFontFamily family)
        {
            int key = (int)role * 3 + (int)family;
            if (warnedIncompleteBoldFace[key]) return;
            warnedIncompleteBoldFace[key] = true;
            if (log != null)
            {
                log.Warning("No compatible bold " + family + " font was available for the " + role +
                            " role; TMP's original path remains active for that text.");
            }
        }

        private static void WarnIncompleteFace(NativeFontRole role, ScriptFontFamily family, int covered, int total)
        {
            int key = (int)role * 3 + (int)family;
            if (warnedIncompleteFace[key]) return;
            warnedIncompleteFace[key] = true;
            if (log != null)
            {
                log.Warning("No single native " + family + " font covers this text's CJK characters for the " + role +
                            " role (" + covered + "/" + total + "); TMP's original fallback chain remains active for it.");
            }
        }

        private sealed class TextSelectionCache
        {
            public string Text;
            public int InputRevision;
            public TMP_FontAsset PrimaryFont;
            public int Generation;
            public NativeFontRole Role;
            public TMP_FontAsset RoleFont;
            public bool HasLocalizedRole;
            public bool PrimaryIsInline;
            public TextScriptProfile Profile;
            public TMP_FontAsset Chinese;
            public TMP_FontAsset Japanese;
            public TMP_FontAsset Korean;
            public TMP_FontAsset ChineseBold;
            public TMP_FontAsset JapaneseBold;
            public TMP_FontAsset KoreanBold;
            private bool ChineseBoldPrepared;
            private bool JapaneseBoldPrepared;
            private bool KoreanBoldPrepared;

            public bool Matches(
                string text,
                int inputRevision,
                TMP_FontAsset primaryFont,
                NativeFontRole role,
                TMP_FontAsset roleFont,
                bool hasLocalizedRole,
                bool primaryIsInline,
                int generation)
            {
                return ReferenceEquals(Text, text) && InputRevision == inputRevision &&
                       ReferenceEquals(PrimaryFont, primaryFont) &&
                       Role == role && ReferenceEquals(RoleFont, roleFont) && HasLocalizedRole == hasLocalizedRole &&
                       PrimaryIsInline == primaryIsInline && Generation == generation;
            }

            public void SetSource(
                string text,
                int inputRevision,
                TMP_FontAsset primaryFont,
                int generation,
                bool primaryIsInline)
            {
                Text = text;
                InputRevision = inputRevision;
                PrimaryFont = primaryFont;
                Generation = generation;
                PrimaryIsInline = primaryIsInline;
                Chinese = null;
                Japanese = null;
                Korean = null;
                ChineseBold = null;
                JapaneseBold = null;
                KoreanBold = null;
                ChineseBoldPrepared = false;
                JapaneseBoldPrepared = false;
                KoreanBoldPrepared = false;
            }

            public TMP_FontAsset GetFont(ScriptFontFamily family)
            {
                switch (family)
                {
                    case ScriptFontFamily.Chinese: return Chinese;
                    case ScriptFontFamily.Japanese: return Japanese;
                    case ScriptFontFamily.Korean: return Korean;
                    default: return null;
                }
            }
            public bool IsBoldPrepared(ScriptFontFamily family)
            {
                switch (family)
                {
                    case ScriptFontFamily.Chinese: return ChineseBoldPrepared;
                    case ScriptFontFamily.Japanese: return JapaneseBoldPrepared;
                    case ScriptFontFamily.Korean: return KoreanBoldPrepared;
                    default: return true;
                }
            }

            public TMP_FontAsset GetBoldFont(ScriptFontFamily family)
            {
                switch (family)
                {
                    case ScriptFontFamily.Chinese: return ChineseBold;
                    case ScriptFontFamily.Japanese: return JapaneseBold;
                    case ScriptFontFamily.Korean: return KoreanBold;
                    default: return null;
                }
            }

            public void SetBoldFont(ScriptFontFamily family, TMP_FontAsset font)
            {
                switch (family)
                {
                    case ScriptFontFamily.Chinese:
                        ChineseBold = font;
                        ChineseBoldPrepared = true;
                        break;
                    case ScriptFontFamily.Japanese:
                        JapaneseBold = font;
                        JapaneseBoldPrepared = true;
                        break;
                    case ScriptFontFamily.Korean:
                        KoreanBold = font;
                        KoreanBoldPrepared = true;
                        break;
                }
            }
        }
    }
}
