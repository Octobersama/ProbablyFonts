using System;
using System.Collections.Generic;
using ProbablyFonts.Core;
using TMPro;
using UnityEngine;

namespace ProbablyFonts
{
    internal sealed class FontCloneStore
    {
        private readonly Dictionary<CloneKey, TMP_FontAsset> staticCopies = new Dictionary<CloneKey, TMP_FontAsset>();
        private readonly Dictionary<CloneKey, TMP_FontAsset> dynamicCopies = new Dictionary<CloneKey, TMP_FontAsset>();
        private readonly List<TMP_FontAsset> ownedFonts = new List<TMP_FontAsset>();
        private readonly List<Material> ownedMaterials = new List<Material>();
        private readonly List<TMP_FontAsset> ownedDynamicFonts = new List<TMP_FontAsset>();
        private readonly HashSet<TMP_FontAsset> ownedFontSet = new HashSet<TMP_FontAsset>(ReferenceEqualityComparer<TMP_FontAsset>.Instance);
        private readonly HashSet<Material> ownedMaterialSet = new HashSet<Material>(ReferenceEqualityComparer<Material>.Instance);
        private readonly Dictionary<FallbackMaterialKey, FallbackMaterialEntry> fallbackMaterials = new Dictionary<FallbackMaterialKey, FallbackMaterialEntry>();

        public TMP_FontAsset GetStaticCopy(
            TMP_FontAsset source,
            FontWeightProfile weights,
            bool normalizeSyntheticWeights = true)
        {
            if (source == null || source.material == null) return null;
            CloneKey key = new CloneKey(source, weights, false, normalizeSyntheticWeights);
            if (staticCopies.TryGetValue(key, out TMP_FontAsset existing)) return existing;

            TMP_FontAsset copy = UnityEngine.Object.Instantiate(source);
            if (copy == null)
            {
                staticCopies.Add(key, null);
                return null;
            }

            Material material = null;
            try
            {
                copy.name = source.name + " (ProbablyFonts fallback)";
                copy.atlasPopulationMode = AtlasPopulationMode.Static;
                copy.fallbackFontAssetTable = new List<TMP_FontAsset>();
                material = new Material(source.material)
                {
                    name = source.material.name + " (ProbablyFonts fallback)"
                };
                copy.material = material;
                copy.ReadFontAssetDefinition();
                if (normalizeSyntheticWeights)
                {
                    NormalizeWeight(copy, material, weights);
                    UseSyntheticWeights(copy);
                }
            }
            catch
            {
                if (material != null) UnityEngine.Object.Destroy(material);
                UnityEngine.Object.Destroy(copy);
                staticCopies.Add(key, null);
                throw;
            }

            staticCopies.Add(key, copy);
            ownedFonts.Add(copy);
            ownedFontSet.Add(copy);
            ownedMaterialSet.Add(material);
            ownedMaterials.Add(material);
            return copy;
        }

        public TMP_FontAsset GetDynamicCopy(
            TMP_FontAsset source,
            FontWeightProfile weights,
            bool normalizeSyntheticWeights = true)
        {
            if (source == null || source.sourceFontFile == null) return null;
            CloneKey key = new CloneKey(source, weights, true, normalizeSyntheticWeights);
            if (dynamicCopies.TryGetValue(key, out TMP_FontAsset existing)) return existing;

            TMP_FontAsset copy;
            try
            {
                int pointSize = source.creationSettings.pointSize;
                if (pointSize <= 0) pointSize = (int)source.faceInfo.pointSize;
                if (pointSize <= 0) pointSize = 90;

                int padding = source.atlasPadding;
                if (padding <= 0) padding = source.creationSettings.padding;
                if (padding <= 0) padding = 9;

                int width = source.atlasWidth;
                if (width <= 0) width = source.creationSettings.atlasWidth;
                if (width <= 0) width = 1024;

                int height = source.atlasHeight;
                if (height <= 0) height = source.creationSettings.atlasHeight;
                if (height <= 0) height = 1024;

                copy = TMP_FontAsset.CreateFontAsset(
                    source.sourceFontFile,
                    pointSize,
                    padding,
                    source.atlasRenderMode,
                    width,
                    height,
                    AtlasPopulationMode.Dynamic,
                    true);
            }
            catch
            {
                dynamicCopies.Add(key, null);
                throw;
            }

            if (copy == null || copy.material == null)
            {
                if (copy != null) DestroyDynamicCopy(copy);
                dynamicCopies.Add(key, null);
                return null;
            }

            if (normalizeSyntheticWeights)
            {
                NormalizeWeight(copy, copy.material, weights);
                UseSyntheticWeights(copy);
            }
            else
            {
                CopyNativeWeights(copy, source);
            }

            ownedDynamicFonts.Add(copy);

            dynamicCopies.Add(key, copy);
            ownedFonts.Add(copy);
            ownedMaterials.Add(copy.material);
            ownedFontSet.Add(copy);
            ownedMaterialSet.Add(copy.material);
            return copy;
        }

        public void Release()
        {
            for (int index = 0; index < ownedDynamicFonts.Count; index++)
            {
                DestroyAtlasTextures(ownedDynamicFonts[index]);
            }

            for (int index = 0; index < ownedFonts.Count; index++)
            {
                if (ownedFonts[index] != null) UnityEngine.Object.Destroy(ownedFonts[index]);
            }

            for (int index = 0; index < ownedMaterials.Count; index++)
            {
                if (ownedMaterials[index] != null) UnityEngine.Object.Destroy(ownedMaterials[index]);
            }

            staticCopies.Clear();
            dynamicCopies.Clear();
            ownedFonts.Clear();
            ownedDynamicFonts.Clear();
            ownedMaterials.Clear();
            fallbackMaterials.Clear();
            ownedFontSet.Clear();
            ownedMaterialSet.Clear();
        }

        public bool IsOwnedFont(TMP_FontAsset font)
        {
            return font != null && ownedFontSet.Contains(font);
        }

        public bool IsOwnedMaterial(Material material)
        {
            return material != null && ownedMaterialSet.Contains(material);
        }

        public bool TryGetFallbackMaterial(Material sourceMaterial, Material targetMaterial, out Material fallbackMaterial)
        {
            fallbackMaterial = null;
            if (sourceMaterial == null || targetMaterial == null || !IsOwnedMaterial(targetMaterial)) return false;

            return TryGetFallbackMaterial(
                sourceMaterial,
                targetMaterial,
                targetMaterial,
                targetMaterial.GetTexture(ShaderUtilities.ID_MainTex),
                0,
                false,
                out fallbackMaterial);
        }

        public bool TryGetFallbackMaterial(
            TMP_FontAsset fontAsset,
            Material sourceMaterial,
            int atlasIndex,
            out Material fallbackMaterial)
        {
            fallbackMaterial = null;
            if (sourceMaterial == null || !IsOwnedFont(fontAsset)) return false;

            Texture2D[] textures = fontAsset.atlasTextures;
            if (textures == null || atlasIndex < 0 || atlasIndex >= textures.Length || textures[atlasIndex] == null)
            {
                return false;
            }

            Material targetMaterial = fontAsset.material;
            if (targetMaterial == null) return false;
            return TryGetFallbackMaterial(
                sourceMaterial,
                targetMaterial,
                fontAsset,
                textures[atlasIndex],
                atlasIndex,
                true,
                out fallbackMaterial);
        }

        private bool TryGetFallbackMaterial(
            Material sourceMaterial,
            Material targetMaterial,
            object targetIdentity,
            Texture targetTexture,
            int atlasIndex,
            bool fontTarget,
            out Material fallbackMaterial)
        {
            FallbackMaterialKey key = new FallbackMaterialKey(
                sourceMaterial, targetIdentity, targetMaterial, atlasIndex, fontTarget);
            int sourceCrc = sourceMaterial.ComputeCRC();
            if (fallbackMaterials.TryGetValue(key, out FallbackMaterialEntry entry))
            {
                if (entry.SourceCrc != sourceCrc)
                {
                    TMP_MaterialManager.CopyMaterialPresetProperties(sourceMaterial, entry.Material);
                    entry.SourceCrc = sourceCrc;
                }

                fallbackMaterial = entry.Material;
                return true;
            }

            Material copy = new Material(targetMaterial)
            {
                name = targetMaterial.name + " (ProbablyFonts matched fallback)",
                hideFlags = HideFlags.HideAndDontSave
            };
            if (targetTexture != null && copy.HasProperty(ShaderUtilities.ID_MainTex))
            {
                copy.SetTexture(ShaderUtilities.ID_MainTex, targetTexture);
            }

            TMP_MaterialManager.CopyMaterialPresetProperties(sourceMaterial, copy);
            fallbackMaterials.Add(key, new FallbackMaterialEntry(copy, sourceCrc));
            ownedMaterials.Add(copy);
            ownedMaterialSet.Add(copy);
            fallbackMaterial = copy;
            return true;
        }
        public TMP_FontAsset GetBoldAlternate(TMP_FontAsset regularFace, float boldWeight, uint[] codePoints)
        {
            if (regularFace == null || codePoints == null || codePoints.Length == 0) return null;

            FontWeightProfile boldWeights = FontWeightProfile.Match(boldWeight, boldWeight);
            TMP_FontAsset alternate = regularFace.atlasPopulationMode == AtlasPopulationMode.Dynamic
                ? GetDynamicCopy(regularFace, boldWeights)
                : GetStaticCopy(regularFace, boldWeights);
            if (alternate == null) return null;

            if (alternate.atlasPopulationMode == AtlasPopulationMode.Dynamic)
            {
                alternate.TryAddCharacters(codePoints, false);
            }

            for (int index = 0; index < codePoints.Length; index++)
            {
                if (!alternate.HasCharacter((int)codePoints[index])) return null;
            }

            return alternate;
        }

        private static void DestroyDynamicCopy(TMP_FontAsset font)
        {
            DestroyAtlasTextures(font);
            if (font.material != null) UnityEngine.Object.Destroy(font.material);
            UnityEngine.Object.Destroy(font);
        }

        private static void DestroyAtlasTextures(TMP_FontAsset font)
        {
            if (font == null) return;
            Texture2D[] textures = font.atlasTextures;
            if (textures == null) return;

            for (int index = 0; index < textures.Length; index++)
            {
                Texture2D texture = textures[index];
                if (texture == null) continue;

                bool alreadyDestroyed = false;
                for (int previous = 0; previous < index; previous++)
                {
                    if (ReferenceEquals(textures[previous], texture))
                    {
                        alreadyDestroyed = true;
                        break;
                    }
                }

                if (!alreadyDestroyed) UnityEngine.Object.Destroy(texture);
            }
        }

        private static void NormalizeWeight(TMP_FontAsset font, Material material, FontWeightProfile weights)
        {
            font.normalStyle = weights.Normal;
            font.boldStyle = weights.Bold;

            if (material.HasProperty(ShaderUtilities.ID_WeightNormal))
            {
                material.SetFloat(ShaderUtilities.ID_WeightNormal, weights.Normal);
            }

            if (material.HasProperty(ShaderUtilities.ID_WeightBold))
            {
                material.SetFloat(ShaderUtilities.ID_WeightBold, weights.Bold);
            }
        }
        private static void CopyNativeWeights(TMP_FontAsset copy, TMP_FontAsset source)
        {
            float normal = source.normalStyle;
            float bold = source.boldStyle;
            Material sourceMaterial = source.material;
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

            NormalizeWeight(copy, copy.material, FontWeightProfile.Match(normal, bold));
        }

        private static void UseSyntheticWeights(TMP_FontAsset font)
        {
            TMP_FontWeightPair[] table = font.fontWeightTable;
            if (table == null) return;

            for (int index = 0; index < table.Length; index++)
            {
                TMP_FontWeightPair pair = table[index];
                pair.regularTypeface = null;
                pair.italicTypeface = null;
                table[index] = pair;
            }
        }

        private sealed class FallbackMaterialEntry
        {
            public readonly Material Material;
            public int SourceCrc;

            public FallbackMaterialEntry(Material material, int sourceCrc)
            {
                Material = material;
                SourceCrc = sourceCrc;
            }
        }

        private readonly struct FallbackMaterialKey : IEquatable<FallbackMaterialKey>
        {
            private readonly Material sourceMaterial;
            private readonly object targetIdentity;
            private readonly Material targetMaterial;
            private readonly int atlasIndex;
            private readonly bool targetIsFontAsset;

            public FallbackMaterialKey(
                Material sourceMaterial,
                object targetIdentity,
                Material targetMaterial,
                int atlasIndex,
                bool targetIsFontAsset)
            {
                this.sourceMaterial = sourceMaterial;
                this.targetIdentity = targetIdentity;
                this.targetMaterial = targetMaterial;
                this.atlasIndex = atlasIndex;
                this.targetIsFontAsset = targetIsFontAsset;
            }

            public bool Equals(FallbackMaterialKey other)
            {
                return ReferenceEquals(sourceMaterial, other.sourceMaterial) &&
                       ReferenceEquals(targetIdentity, other.targetIdentity) &&
                       ReferenceEquals(targetMaterial, other.targetMaterial) && atlasIndex == other.atlasIndex &&
                       targetIsFontAsset == other.targetIsFontAsset;
            }

            public override bool Equals(object obj)
            {
                return obj is FallbackMaterialKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = ReferenceEqualityComparer<Material>.Instance.GetHashCode(sourceMaterial);
                    hash = (hash * 397) ^ (targetIdentity == null ? 0 : ReferenceEqualityComparer<object>.Instance.GetHashCode(targetIdentity));
                    hash = (hash * 397) ^ ReferenceEqualityComparer<Material>.Instance.GetHashCode(targetMaterial);
                    hash = (hash * 397) ^ atlasIndex;
                    return (hash * 397) ^ targetIsFontAsset.GetHashCode();
                }
            }
        }

        private readonly struct CloneKey : IEquatable<CloneKey>
        {
            private readonly TMP_FontAsset source;
            private readonly float normalWeight;
            private readonly float boldWeight;
            private readonly bool dynamic;
            private readonly bool normalizeSyntheticWeights;

            public CloneKey(TMP_FontAsset source, FontWeightProfile weights, bool dynamic, bool normalizeSyntheticWeights)
            {
                this.source = source;
                normalWeight = weights.Normal;
                boldWeight = weights.Bold;
                this.dynamic = dynamic;
                this.normalizeSyntheticWeights = normalizeSyntheticWeights;
            }

            public bool Equals(CloneKey other)
            {
                return ReferenceEquals(source, other.source) && normalWeight.Equals(other.normalWeight) &&
                       boldWeight.Equals(other.boldWeight) && dynamic == other.dynamic &&
                       normalizeSyntheticWeights == other.normalizeSyntheticWeights;
            }

            public override bool Equals(object obj)
            {
                return obj is CloneKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = ReferenceEqualityComparer<TMP_FontAsset>.Instance.GetHashCode(source);
                    hash = (hash * 397) ^ normalWeight.GetHashCode();
                    hash = (hash * 397) ^ boldWeight.GetHashCode();
                    hash = (hash * 397) ^ dynamic.GetHashCode();
                    return (hash * 397) ^ normalizeSyntheticWeights.GetHashCode();
                }
            }
        }
    }
}
