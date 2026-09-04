using System.Collections.Generic;
using UnityEngine;

namespace AsadoSimulator.Interaction
{
    /// <summary>
    /// Component that automatically adds a bold comic cartoon black outline to any GameObject
    /// WITHOUT modifying, replacing, or destroying its original materials and textures.
    /// Works with Custom/ComicOutlineOnly or Custom/ComicOutline shader.
    /// </summary>
    [DisallowMultipleComponent]
    public class ComicOutline : MonoBehaviour
    {
        [Header("Comic Outline Appearance")]
        [Tooltip("Ink outline color.")]
        [SerializeField] private Color outlineColor = new Color(0.02f, 0.02f, 0.02f, 1f);

        [Tooltip("Outline thickness (recommended: 0.012 to 0.035 for clear, bold cartoon lines).")]
        [Range(0.002f, 0.06f)]
        [SerializeField] private float outlineThickness = 0.018f;

        [Header("Target Options")]
        [Tooltip("If true, also applies outlines to child renderers (e.g. multi-part food models).")]
        [SerializeField] private bool includeChildren = true;

        private Material _outlineMaterial;
        private readonly List<RendererMaterialEntry> _entries = new List<RendererMaterialEntry>();

        private struct RendererMaterialEntry
        {
            public Renderer renderer;
            public Material[] originalMaterials;
        }

        private static readonly int OutlineColorProp = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthProp = Shader.PropertyToID("_OutlineWidth");

        private void Awake()
        {
            CreateOutlineMaterial();
            ApplyOutline();
        }

        private void OnEnable()
        {
            if (_entries.Count == 0)
            {
                ApplyOutline();
            }
        }

        private void OnDisable()
        {
            RemoveOutline();
        }

        private void OnDestroy()
        {
            RemoveOutline();
            if (_outlineMaterial != null)
            {
                Destroy(_outlineMaterial);
            }
        }

        private void OnValidate()
        {
            UpdateMaterialProperties();
        }

        private void CreateOutlineMaterial()
        {
            if (_outlineMaterial != null) return;

            Shader shader = Shader.Find("Custom/ComicOutlineOnly");
            if (shader == null)
            {
                shader = Shader.Find("Custom/ComicOutline");
            }

            if (shader != null)
            {
                _outlineMaterial = new Material(shader)
                {
                    name = "ComicOutline_Runtime"
                };
                UpdateMaterialProperties();
            }
        }

        private void UpdateMaterialProperties()
        {
            if (_outlineMaterial != null)
            {
                _outlineMaterial.SetColor(OutlineColorProp, outlineColor);
                _outlineMaterial.SetFloat(OutlineWidthProp, outlineThickness);
            }
        }

        /// <summary>
        /// Appends the outline pass material to all renderers without altering original materials.
        /// </summary>
        public void ApplyOutline()
        {
            CreateOutlineMaterial();
            if (_outlineMaterial == null) return;

            Renderer[] renderers = includeChildren ? GetComponentsInChildren<Renderer>(true) : GetComponents<Renderer>();

            foreach (var r in renderers)
            {
                // Skip LineRenderer or other non-mesh effects
                if (r is LineRenderer || r is TrailRenderer || r is ParticleSystemRenderer) continue;

                Material[] currentMats = r.sharedMaterials;
                // Check if already appended
                bool alreadyHas = false;
                for (int i = 0; i < currentMats.Length; i++)
                {
                    if (currentMats[i] != null && (currentMats[i].shader.name == "Custom/ComicOutlineOnly" || currentMats[i].name.Contains("ComicOutline")))
                    {
                        alreadyHas = true;
                        break;
                    }
                }

                if (!alreadyHas)
                {
                    _entries.Add(new RendererMaterialEntry
                    {
                        renderer = r,
                        originalMaterials = currentMats
                    });

                    Material[] newMats = new Material[currentMats.Length + 1];
                    for (int i = 0; i < currentMats.Length; i++)
                    {
                        newMats[i] = currentMats[i];
                    }
                    newMats[currentMats.Length] = _outlineMaterial;
                    r.materials = newMats;
                }
            }
        }

        /// <summary>
        /// Restores the original material arrays.
        /// </summary>
        public void RemoveOutline()
        {
            foreach (var entry in _entries)
            {
                if (entry.renderer != null && entry.originalMaterials != null)
                {
                    entry.renderer.materials = entry.originalMaterials;
                }
            }
            _entries.Clear();
        }

        public void SetThickness(float thickness)
        {
            outlineThickness = thickness;
            UpdateMaterialProperties();
        }

        public void SetColor(Color color)
        {
            outlineColor = color;
            UpdateMaterialProperties();
        }
    }
}
