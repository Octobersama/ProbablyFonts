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
        private static readonly HashSet<TMP_FontAsset> warnedAssets = new HashSet<TMP_FontAsset>(ReferenceEqualityComparer<TMP_FontAsset>.Instance);
        private static readonly bool[] warnedIncompleteBoldFace = new bool[9];
        private static readonly bool[] warnedIncompleteFace = new bool[9];
        private static readonly Func<TMP_FontAsset, int, bool> hasCharacter = HasCharacter;
        private static readonly Func<TMP_FontAsset, IEnumerable<TMP_FontAsset>> getFallbacks = GetFallbacks;
        private static readonly IEqualityComparer<TMP_FontAsset> fontComparer = ReferenceEqualityComparer<TMP_FontAsset>.Instance;
        private static readonly Func<string, TextScriptAnalysis> analyzeText = FontSelection.AnalyzeText;
        private static readonly Func<TextScriptAnalysis, FontSelectionContext, TextSelectionCache> selectTextContext = SelectTextContext;
        private static readonly ConditionalWeakTable<
            TMP_Text,
            RevisionedTextSelectionCache<FontSelectionContext, TextSelectionCache>
        >.CreateValueCallback createTextCache = CreateTextCache;
        private static ConditionalWeakTable<
            TMP_Text,
            RevisionedTextSelectionCache<FontSelectionContext, TextSelectionCache>
        > textSelections = CreateTextSelections();
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
            TMP_Settings currentSettings = TMP_Settings.GetSettings();
            ownedGlobalFallbacks.Unmount(globalFallbackList, GetGlobalFallbackList(currentSettings));
            RefreshExistingText();
            globalFallbackList = null;
            ownedGlobalAssets.Clear();
            fontLoader = null;
            locale = null;
            tmpSettings = null;
            catalog = null;
            textSelections = CreateTextSelections();
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

            List<TMP_FontAsset> currentFallbackList = GetGlobalFallbackList(settings);
            ownedGlobalFallbacks.Unmount(globalFallbackList, currentFallbackList);
            globalFallbackList = currentFallbackList;
            fontLoader = loader;
            locale = selectedLocale;
            tmpSettings = settings;
            catalog = new NativeFontCatalog(loader);
            generation++;
            textSelections = CreateTextSelections();

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
            TMP_Settings currentSettings = TMP_Settings.GetSettings();
            ownedGlobalFallbacks.Restore(globalFallbackList, GetGlobalFallbackList(currentSettings));
            textSelections = CreateTextSelections();
            RefreshExistingText();
            FontInputSnapshots.Stop();
            clones.Release();
            proposedGlobalFallbacks.Clear();
            ownedGlobalAssets.Clear();
            filteredGlobalFallbacks.Clear();
            nativeCandidateScratch.Clear();
            warnedAssets.Clear();
            Array.Clear(warnedIncompleteFace, 0, warnedIncompleteFace.Length);
            Array.Clear(warnedIncompleteBoldFace, 0, warnedIncompleteBoldFace.Length);
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

            TextFamilyAnalysis familyAnalysis = selection.Analysis.GetFamily(family);
            if (familyAnalysis.TotalCharacters == 0)
            {
                selection.SetBoldFont(family, null);
                return false;
            }

            TMP_FontAsset regularFace = selection.GetFont(family);
            TMP_FontAsset alternate = clones.GetBoldAlternate(
                regularFace,
                ReadNativeWeights(regularFace).Bold,
                familyAnalysis.CodePoints);
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

        private static List<TMP_FontAsset> GetGlobalFallbackList(TMP_Settings settings)
        {
            return settings == null ? null : TMP_Settings.fallbackFontAssets;
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
            var context = new FontSelectionContext(
                primaryFont,
                role,
                roleFont,
                hasLocalizedRole,
                primaryIsInline);
            RevisionedTextSelectionCache<FontSelectionContext, TextSelectionCache> cache =
                textSelections.GetValue(text, createTextCache);
            return cache.Get(input, inputRevision, generation, context, analyzeText, selectTextContext);
        }

        private static ConditionalWeakTable<
            TMP_Text,
            RevisionedTextSelectionCache<FontSelectionContext, TextSelectionCache>
        > CreateTextSelections()
        {
            return new ConditionalWeakTable<
                TMP_Text,
                RevisionedTextSelectionCache<FontSelectionContext, TextSelectionCache>>();
        }

        private static RevisionedTextSelectionCache<FontSelectionContext, TextSelectionCache> CreateTextCache(TMP_Text text)
        {
            return new RevisionedTextSelectionCache<FontSelectionContext, TextSelectionCache>();
        }

        private static TextSelectionCache SelectTextContext(TextScriptAnalysis analysis, FontSelectionContext context)
        {
            var selection = new TextSelectionCache(analysis, context);
            selection.Chinese = SelectFontForFamily(selection, ScriptFontFamily.Chinese);
            selection.Japanese = SelectFontForFamily(selection, ScriptFontFamily.Japanese);
            selection.Korean = SelectFontForFamily(selection, ScriptFontFamily.Korean);
            return selection;
        }


        private static TMP_FontAsset SelectFontForFamily(TextSelectionCache selection, ScriptFontFamily family)
        {
            TextFamilyAnalysis familyAnalysis = selection.Analysis.GetFamily(family);
            if (familyAnalysis.TotalCharacters == 0) return null;

            catalog.FillCjkCandidates(
                selection.PrimaryFont,
                selection.RoleFont,
                selection.HasLocalizedRole,
                selection.PrimaryIsInline,
                family,
                selection.Role,
                nativeCandidateScratch);
            var selector = new CompleteCandidateSelector<TMP_FontAsset>(familyAnalysis, hasCharacter);

            for (int sourceIndex = 0; sourceIndex < nativeCandidateScratch.Count; sourceIndex++)
            {
                TMP_FontAsset source = nativeCandidateScratch[sourceIndex];
                if (!NativeFontCatalog.HasFontData(source)) continue;

                FontWeightProfile nativeWeights;
                TMP_FontAsset staticCopy;
                try
                {
                    nativeWeights = ReadNativeWeights(source);
                    staticCopy = clones.GetStaticCopy(source, nativeWeights);
                }
                catch (Exception exception)
                {
                    WarnAssetFailure(source, exception);
                    continue;
                }

                if (ConsiderCandidate(ref selector, staticCopy, source)) break;
                if (source.sourceFontFile == null) continue;

                TMP_FontAsset dynamicCopy;
                try
                {
                    dynamicCopy = clones.GetDynamicCopy(source, nativeWeights);
                    if (dynamicCopy == null) continue;
                    dynamicCopy.TryAddCharacters(familyAnalysis.CodePoints, false);
                }
                catch (Exception exception)
                {
                    WarnAssetFailure(source, exception);
                    continue;
                }

                if (ConsiderCandidate(ref selector, dynamicCopy, source)) break;
            }

            TMP_FontAsset selected = selector.Selected;
            if (selected == null)
            {
                WarnIncompleteFace(selection.Role, family, selector.BestCoverage, familyAnalysis.TotalCharacters);
            }

            return selected;
        }

        private static bool ConsiderCandidate(
            ref CompleteCandidateSelector<TMP_FontAsset> selector,
            TMP_FontAsset candidate,
            TMP_FontAsset source)
        {
            try
            {
                return selector.Consider(candidate);
            }
            catch (Exception exception)
            {
                WarnAssetFailure(source, exception);
                throw;
            }
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

        private readonly struct FontSelectionContext : IEquatable<FontSelectionContext>
        {
            public readonly TMP_FontAsset PrimaryFont;
            public readonly NativeFontRole Role;
            public readonly TMP_FontAsset RoleFont;
            public readonly bool HasLocalizedRole;
            public readonly bool PrimaryIsInline;

            public FontSelectionContext(
                TMP_FontAsset primaryFont,
                NativeFontRole role,
                TMP_FontAsset roleFont,
                bool hasLocalizedRole,
                bool primaryIsInline)
            {
                PrimaryFont = primaryFont;
                Role = role;
                RoleFont = roleFont;
                HasLocalizedRole = hasLocalizedRole;
                PrimaryIsInline = primaryIsInline;
            }

            public bool Equals(FontSelectionContext other)
            {
                return ReferenceEquals(PrimaryFont, other.PrimaryFont) && Role == other.Role &&
                       ReferenceEquals(RoleFont, other.RoleFont) && HasLocalizedRole == other.HasLocalizedRole &&
                       PrimaryIsInline == other.PrimaryIsInline;
            }

            public override bool Equals(object value)
            {
                return value is FontSelectionContext && Equals((FontSelectionContext)value);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = ReferenceEquals(PrimaryFont, null) ? 0 : RuntimeHelpers.GetHashCode(PrimaryFont);
                    hash = (hash * 397) ^ (int)Role;
                    hash = (hash * 397) ^ (ReferenceEquals(RoleFont, null) ? 0 : RuntimeHelpers.GetHashCode(RoleFont));
                    hash = (hash * 397) ^ (HasLocalizedRole ? 1 : 0);
                    return (hash * 397) ^ (PrimaryIsInline ? 1 : 0);
                }
            }
        }

        private sealed class TextSelectionCache
        {
            public readonly TextScriptAnalysis Analysis;
            public readonly TMP_FontAsset PrimaryFont;
            public readonly NativeFontRole Role;
            public readonly TMP_FontAsset RoleFont;
            public readonly bool HasLocalizedRole;
            public readonly bool PrimaryIsInline;
            public TextScriptProfile Profile => Analysis.Profile;
            public TMP_FontAsset Chinese;
            public TMP_FontAsset Japanese;
            public TMP_FontAsset Korean;
            public TMP_FontAsset ChineseBold;
            public TMP_FontAsset JapaneseBold;
            public TMP_FontAsset KoreanBold;
            private bool ChineseBoldPrepared;
            private bool JapaneseBoldPrepared;
            private bool KoreanBoldPrepared;

            public TextSelectionCache(TextScriptAnalysis analysis, FontSelectionContext context)
            {
                Analysis = analysis;
                PrimaryFont = context.PrimaryFont;
                Role = context.Role;
                RoleFont = context.RoleFont;
                HasLocalizedRole = context.HasLocalizedRole;
                PrimaryIsInline = context.PrimaryIsInline;
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
