using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PoliticalMayhem.Core
{
    /// <summary>
    /// Ensures 3D materials never turn magenta/pink in either Unity 6 URP or Built-in RP.
    /// Clones the active pipeline's default primitive material and sets both _BaseColor (URP) and _Color (Built-in).
    /// </summary>
    public static class MaterialHelper
    {
        private struct MaterialCacheKey : System.IEquatable<MaterialCacheKey>
        {
            public Texture2D baseMap;
            public Texture2D emissionMap;
            public Color32 tint;
            public int smoothnessBucket;

            public bool Equals(MaterialCacheKey other)
            {
                return baseMap == other.baseMap
                    && emissionMap == other.emissionMap
                    && tint.r == other.tint.r
                    && tint.g == other.tint.g
                    && tint.b == other.tint.b
                    && tint.a == other.tint.a
                    && smoothnessBucket == other.smoothnessBucket;
            }

            public override bool Equals(object obj) => obj is MaterialCacheKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = baseMap != null ? baseMap.GetHashCode() : 0;
                    h = (h * 397) ^ (emissionMap != null ? emissionMap.GetHashCode() : 0);
                    h = (h * 397) ^ (tint.r | (tint.g << 8) | (tint.b << 16) | (tint.a << 24));
                    h = (h * 397) ^ smoothnessBucket;
                    return h;
                }
            }
        }

        private static Material _cachedTemplateMaterial;
        private static readonly Dictionary<MaterialCacheKey, Material> _cachedTexturedMaterials = new Dictionary<MaterialCacheKey, Material>();

        public static bool IsUsingSrp()
        {
            return GraphicsSettings.currentRenderPipeline != null
                || GraphicsSettings.defaultRenderPipeline != null
                || QualitySettings.renderPipeline != null
                || Shader.Find("Universal Render Pipeline/Lit") != null;
        }

        public static Material GetTemplateMaterial()
        {
            if (_cachedTemplateMaterial != null && IsValidForActivePipeline(_cachedTemplateMaterial))
                return _cachedTemplateMaterial;

            _cachedTemplateMaterial = null;
            bool srp = IsUsingSrp();

            // Unity automatically equips newly created primitives with the active Render Pipeline's Lit material,
            // but explicitly reject Built-in Standard if SRP/URP is active.
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            temp.hideFlags = HideFlags.HideAndDontSave;
            Renderer rend = temp.GetComponent<Renderer>();
            if (rend != null && rend.sharedMaterial != null && IsValidForActivePipeline(rend.sharedMaterial))
            {
                string primShader = rend.sharedMaterial.shader != null ? rend.sharedMaterial.shader.name : string.Empty;
                if (!srp || primShader != "Standard")
                {
                    _cachedTemplateMaterial = new Material(rend.sharedMaterial)
                    {
                        name = "PM_PipelineTemplate",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                }
            }
            Object.DestroyImmediate(temp);

            if (_cachedTemplateMaterial == null)
            {
                Shader shader = srp
                    ? (Shader.Find("Universal Render Pipeline/Lit")
                       ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                       ?? Shader.Find("Sprites/Default"))
                    : (Shader.Find("Standard")
                       ?? Shader.Find("Universal Render Pipeline/Lit"));

                if (shader == null)
                    shader = Shader.Find("Hidden/Internal-Colored");

                _cachedTemplateMaterial = new Material(shader)
                {
                    name = "PM_FallbackTemplate",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            return _cachedTemplateMaterial;
        }

        public static bool IsValidForActivePipeline(Material mat)
        {
            if (mat == null || mat.shader == null) return false;
            string sName = mat.shader.name ?? string.Empty;
            if (string.IsNullOrEmpty(sName) || sName.Contains("Error") || sName.Contains("InternalError")) return false;
            if (IsUsingSrp() && (sName == "Standard" || sName.StartsWith("Legacy Shaders/"))) return false;
            return true;
        }

        public static Material CreateColoredMaterial(Color color, float smoothness = 0.25f, string materialName = null)
        {
            Material mat = new Material(GetTemplateMaterial());
            mat.hideFlags = HideFlags.None;
            if (!string.IsNullOrEmpty(materialName))
                mat.name = materialName;
            SetMaterialColor(mat, color, smoothness);
            return mat;
        }

        public static bool HasBaseTexture(Material mat)
        {
            if (mat == null) return false;
            if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null) return true;
            if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null) return true;
            return mat.mainTexture != null;
        }

        public static void SetMaterialTextures(Material mat, Texture2D baseMap, Texture2D emissionMap = null)
        {
            if (mat == null) return;
            if (baseMap != null)
            {
                mat.mainTexture = baseMap;
                if (mat.HasProperty("_BaseMap"))
                    mat.SetTexture("_BaseMap", baseMap);
                if (mat.HasProperty("_MainTex"))
                    mat.SetTexture("_MainTex", baseMap);
            }

            if (emissionMap != null && mat.HasProperty("_EmissionMap"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetTexture("_EmissionMap", emissionMap);
                if (mat.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", Color.white * 0.6f);
            }
        }

        public static Material CreateTexturedMaterial(
            Texture2D baseMap,
            Color tint,
            float smoothness = 0.2f,
            string materialName = null,
            Texture2D emissionMap = null)
        {
            Material mat = new Material(GetTemplateMaterial());
            mat.hideFlags = HideFlags.None;
            if (!string.IsNullOrEmpty(materialName))
                mat.name = materialName;
            SetMaterialColor(mat, tint, smoothness);
            SetMaterialTextures(mat, baseMap, emissionMap);
            return mat;
        }

        public static Material GetOrCreateSharedTexturedMaterial(
            Texture2D baseMap,
            Color tint,
            float smoothness = 0.2f,
            string materialName = null,
            Texture2D emissionMap = null)
        {
            MaterialCacheKey key = new MaterialCacheKey
            {
                baseMap = baseMap,
                emissionMap = emissionMap,
                tint = tint,
                smoothnessBucket = Mathf.RoundToInt(smoothness * 100f)
            };

            if (_cachedTexturedMaterials.TryGetValue(key, out Material cached) &&
                cached != null &&
                IsValidForActivePipeline(cached) &&
                (baseMap == null || cached.mainTexture == baseMap))
            {
                return cached;
            }

            Material mat = CreateTexturedMaterial(baseMap, tint, smoothness, materialName, emissionMap);
            _cachedTexturedMaterials[key] = mat;
            return mat;
        }

        public static void ApplyTexturedMaterialToHierarchy(
            GameObject root,
            Texture2D baseMap,
            Color tint,
            float smoothness = 0.2f,
            string materialName = null,
            Texture2D emissionMap = null)
        {
            if (root == null) return;
            Material shared = GetOrCreateSharedTexturedMaterial(baseMap, tint, smoothness, materialName, emissionMap);
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer rend = renderers[i];
                if (rend == null) continue;
                int slotCount = rend.sharedMaterials != null && rend.sharedMaterials.Length > 0
                    ? rend.sharedMaterials.Length
                    : 1;
                if (slotCount == 1)
                {
                    rend.sharedMaterial = shared;
                }
                else
                {
                    Material[] mats = new Material[slotCount];
                    for (int j = 0; j < slotCount; j++)
                        mats[j] = shared;
                    rend.sharedMaterials = mats;
                }
            }
        }

        public static void SetMaterialColor(Material mat, Color color, float smoothness = 0.25f)
        {
            if (mat == null) return;
            color.a = Mathf.Clamp01(color.a);
            mat.color = color;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness"))
                mat.SetFloat("_Glossiness", smoothness);
        }

        public static void ApplyColor(Renderer renderer, Color color, float smoothness = 0.25f)
        {
            if (renderer == null) return;

            if (!Application.isPlaying)
            {
                if (!IsValidForActivePipeline(renderer.sharedMaterial))
                {
                    renderer.sharedMaterial = CreateColoredMaterial(color, smoothness, $"{renderer.gameObject.name}_Mat");
                }
                else
                {
                    Material mat = new Material(renderer.sharedMaterial);
                    SetMaterialColor(mat, color, smoothness);
                    renderer.sharedMaterial = mat;
                }
                return;
            }

            if (!IsValidForActivePipeline(renderer.sharedMaterial))
            {
                renderer.sharedMaterial = CreateColoredMaterial(color, smoothness, $"{renderer.gameObject.name}_Mat");
            }

            Material inst = renderer.material;
            SetMaterialColor(inst, color, smoothness);
        }
    }
}
