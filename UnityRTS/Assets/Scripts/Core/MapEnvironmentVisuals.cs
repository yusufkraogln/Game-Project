using System;
using System.Collections.Generic;
using UnityEngine;

namespace PoliticalMayhem.Core
{
    [Serializable]
    public struct ColoredRendererBinding
    {
        public Renderer renderer;
        public Color color;
        public float smoothness;
        public Texture2D texture;
    }

    /// <summary>
    /// Ensures static map terrain, ocean, roads, and city landmark buildings retain valid non-pink
    /// URP/Built-in materials and crisp colors in both Edit Mode and Play Mode.
    /// </summary>
    [ExecuteAlways]
    public class MapEnvironmentVisuals : MonoBehaviour
    {
        [SerializeField] private List<ColoredRendererBinding> _bindings = new List<ColoredRendererBinding>();

        public void ClearBindings()
        {
            _bindings.Clear();
        }

        public void Register(Renderer renderer, Color color, float smoothness = 0.25f, Texture2D texture = null)
        {
            if (renderer == null) return;
            _bindings.Add(new ColoredRendererBinding
            {
                renderer = renderer,
                color = color,
                smoothness = smoothness,
                texture = texture
            });
        }

        private void Awake()
        {
            ApplyAll();
        }

        private void OnEnable()
        {
            ApplyAll();
        }

        public void ApplyAll()
        {
            for (int i = 0; i < _bindings.Count; i++)
            {
                ColoredRendererBinding b = _bindings[i];
                if (b.renderer == null) continue;

                if (!Application.isPlaying && MaterialHelper.IsValidForActivePipeline(b.renderer.sharedMaterial))
                    continue;

                if (b.texture != null)
                {
                    if (MaterialHelper.IsValidForActivePipeline(b.renderer.sharedMaterial) &&
                        MaterialHelper.HasBaseTexture(b.renderer.sharedMaterial))
                    {
                        continue;
                    }

                    Material texturedMat = MaterialHelper.GetOrCreateSharedTexturedMaterial(
                        b.texture,
                        b.color == default ? Color.white : b.color,
                        b.smoothness,
                        $"{b.renderer.gameObject.name}_TexMat");
                    b.renderer.sharedMaterial = texturedMat;
                    continue;
                }

                if (Application.isPlaying &&
                    MaterialHelper.IsValidForActivePipeline(b.renderer.sharedMaterial) &&
                    MaterialHelper.HasBaseTexture(b.renderer.sharedMaterial))
                {
                    continue;
                }

                MaterialHelper.ApplyColor(b.renderer, b.color, b.smoothness);
            }
        }
    }
}
