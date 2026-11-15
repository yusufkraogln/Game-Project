using UnityEngine;

namespace PoliticalMayhem.Core
{
    /// <summary>
    /// Loads curated .fbx model assets from Resources/Models/, normalizes scale and ground Y-offset
    /// via combined Renderer bounds, strips sub-colliders, and applies URP-valid textured materials.
    /// </summary>
    public static class ModelVisualHelper
    {
        public static GameObject InstantiateNormalizedModel(
            string modelResourcePath,
            string textureResourcePath,
            string wrapperName,
            Transform parent,
            Vector3 localPosition,
            float localYawDegrees,
            float targetSize,
            bool normalizeByHeight,
            float groundLocalY,
            Color tint,
            float smoothness = 0.20f,
            bool stripColliders = true,
            string emissionResourcePath = null)
        {
            if (string.IsNullOrEmpty(modelResourcePath))
                return null;

            GameObject prefab = Resources.Load<GameObject>(modelResourcePath);
            if (prefab == null)
                return null;

            Texture2D baseTex = !string.IsNullOrEmpty(textureResourcePath)
                ? Resources.Load<Texture2D>(textureResourcePath)
                : null;
            Texture2D emissionTex = !string.IsNullOrEmpty(emissionResourcePath)
                ? Resources.Load<Texture2D>(emissionResourcePath)
                : null;

            GameObject wrapper = new GameObject(string.IsNullOrEmpty(wrapperName) ? prefab.name : wrapperName);
            if (parent != null)
                wrapper.transform.SetParent(parent, false);
            wrapper.transform.localPosition = new Vector3(localPosition.x, groundLocalY, localPosition.z);
            wrapper.transform.localRotation = Quaternion.Euler(0f, localYawDegrees, 0f);
            wrapper.transform.localScale = Vector3.one;

            // Instantiate unparented at origin so world-space Bounds measure cleanly without parent transforms
            GameObject modelInstance = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            modelInstance.name = prefab.name;

            if (stripColliders)
                StripColliders(modelInstance);

            DisableUnusedAnimators(modelInstance);

            MaterialHelper.ApplyTexturedMaterialToHierarchy(
                modelInstance,
                baseTex,
                tint,
                smoothness,
                $"{prefab.name}_Mat",
                emissionTex);

            if (TryComputeCombinedBounds(modelInstance, out Bounds bounds))
            {
                float currentDim = normalizeByHeight
                    ? bounds.size.y
                    : Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);

                float scaleFactor = currentDim > 0.001f ? (targetSize / currentDim) : 1f;
                modelInstance.transform.SetParent(wrapper.transform, false);
                modelInstance.transform.localScale = Vector3.one * scaleFactor;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localPosition = new Vector3(
                    -bounds.center.x * scaleFactor,
                    -bounds.min.y * scaleFactor,
                    -bounds.center.z * scaleFactor);
            }
            else
            {
                modelInstance.transform.SetParent(wrapper.transform, false);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = Vector3.one;
            }

            return wrapper;
        }

        public static void StripColliders(GameObject root)
        {
            if (root == null) return;
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null) continue;
                if (Application.isPlaying)
                    Object.Destroy(colliders[i]);
                else
                    Object.DestroyImmediate(colliders[i]);
            }
        }

        private static void DisableUnusedAnimators(GameObject root)
        {
            if (root == null) return;
            Animator[] animators = root.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                if (animators[i] != null && animators[i].runtimeAnimatorController == null)
                    animators[i].enabled = false;
            }
        }

        public static bool TryComputeCombinedBounds(GameObject root, out Bounds combined)
        {
            combined = default;
            if (root == null) return false;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer rend = renderers[i];
                if (rend == null) continue;

                if (rend is SkinnedMeshRenderer smr)
                {
                    smr.updateWhenOffscreen = true;
                }

                Bounds b = rend.bounds;
                if (b.size.sqrMagnitude < 0.00001f && rend is SkinnedMeshRenderer fallbackSmr && fallbackSmr.sharedMesh != null)
                {
                    b = fallbackSmr.sharedMesh.bounds;
                }

                if (!hasBounds)
                {
                    combined = b;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(b);
                }
            }

            return hasBounds;
        }
    }
}
