using System.Collections;
using HarmonyLib;
using TMPro;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;

namespace ProbablyFonts
{
    public sealed class ProbablyFontsMod : IMod
    {
        private const string ModId = "probably_fonts";

        private ModLog log;
        private bool enabled;
        private bool assetsReady;
        private bool localeSubscribed;
        private bool initializationQueued;

        public void Init(ModManifest manifest)
        {
            log = new ModLog(manifest);
            log.Log("Font compatibility initialized.");
        }

        public void OnEnable()
        {
            enabled = true;
            assetsReady = false;
            ModHook.OnModAssetsLoaded += OnModAssetsLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        public void OnDisable()
        {
            enabled = false;
            assetsReady = false;
            initializationQueued = false;
            ModHook.OnModAssetsLoaded -= OnModAssetsLoaded;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (localeSubscribed)
            {
                LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
                localeSubscribed = false;
            }

            ModHelper.StopAllCoroutines(ModId);
            FontRuntime.Stop();
        }

        private void OnModAssetsLoaded(LoadedMod loadedMod)
        {
            if (!enabled || loadedMod == null || loadedMod.Manifest.ID != ModId) return;

            assetsReady = true;
            ModHook.OnModAssetsLoaded -= OnModAssetsLoaded;
            QueueInitialization();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!enabled || !assetsReady) return;

            FontRuntime.Suspend();
            QueueInitialization();
        }

        private void OnLocaleChanged(Locale selectedLocale)
        {
            if (!enabled) return;
            if (!TryGetReadyState(out FontLoader loader, out Locale locale, out TMP_Settings settings))
            {
                FontRuntime.Suspend();
                QueueInitialization();
                return;
            }

            FontRuntime.Reconcile(loader, locale, settings);
        }

        private void QueueInitialization()
        {
            if (!enabled || !assetsReady || initializationQueued) return;
            initializationQueued = true;
            if (ModHelper.StartCoroutine(ModId, WaitForReady()) == null)
            {
                initializationQueued = false;
                if (log != null) log.Warning("The font setup coroutine could not start after this mod's assets loaded.");
            }
        }

        private IEnumerator WaitForReady()
        {
            while (enabled && !TryGetReadyState(out _, out _, out _))
            {
                yield return null;
            }

            initializationQueued = false;
            if (!enabled || !TryGetReadyState(out FontLoader loader, out Locale locale, out TMP_Settings settings))
            {
                yield break;
            }

            if (!localeSubscribed)
            {
                LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
                localeSubscribed = true;
            }

            FontRuntime.Start(loader, locale, settings, log);
        }

        private bool TryGetReadyState(out FontLoader loader, out Locale locale, out TMP_Settings settings)
        {
            loader = FontLoader.Instance;
            locale = LocalizationSettings.SelectedLocale;
            settings = TMP_Settings.GetSettings();
            return Core.StartupPolicy.CanStartAfterOwnAssets(
                assetsReady, loader != null, locale != null, settings != null);
        }
    }

    [HarmonyPatch(typeof(TMP_Text), "GetTextElement")]
    internal static class CjkTextElementPatch
    {
        private static bool Prefix(
            TMP_Text __instance,
            uint __0,
            TMP_FontAsset __1,
            FontStyles __2,
            FontWeight __3,
            ref bool __4,
            ref TMP_TextElement __result)
        {
            if (!FontRuntime.IsReady) return true;

            try
            {
                if (!FontRuntime.TryGetCharacter(
                    __instance, __0, __1, __2, __3, out TMP_TextElement element, out bool alternateTypeface))
                {
                    return true;
                }

                __result = element;
                __4 = alternateTypeface;
                return false;
            }
            catch (System.Exception exception)
            {
                FontRuntime.ReportPrefixFailure(exception);
                return true;
            }
        }
    }
    [HarmonyPatch(typeof(TMP_MaterialManager), "GetFallbackMaterial", new System.Type[] { typeof(UnityEngine.Material), typeof(UnityEngine.Material) })]
    internal static class OwnedFallbackMaterialPatch
    {
        private static bool Prefix(UnityEngine.Material __0, UnityEngine.Material __1, ref UnityEngine.Material __result)
        {
            if (!FontRuntime.IsReady) return true;

            try
            {
                if (!FontRuntime.TryGetFallbackMaterial(__0, __1, out UnityEngine.Material material)) return true;
                __result = material;
                return false;
            }
            catch (System.Exception exception)
            {
                FontRuntime.ReportPrefixFailure(exception);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(TMP_MaterialManager), "GetFallbackMaterial", new System.Type[] { typeof(TMP_FontAsset), typeof(UnityEngine.Material), typeof(int) })]
    internal static class OwnedFontFallbackMaterialPatch
    {
        private static bool Prefix(
            TMP_FontAsset __0,
            UnityEngine.Material __1,
            int __2,
            ref UnityEngine.Material __result)
        {
            if (!FontRuntime.IsReady) return true;

            try
            {
                if (!FontRuntime.TryGetFallbackMaterial(__0, __1, __2, out UnityEngine.Material material)) return true;
                __result = material;
                return false;
            }
            catch (System.Exception exception)
            {
                FontRuntime.ReportPrefixFailure(exception);
                return true;
            }
        }
    }
}
