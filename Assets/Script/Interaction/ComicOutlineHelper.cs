using UnityEngine;

namespace AsadoSimulator.Interaction
{
    /// <summary>
    /// Convenient helper component attached to props to customize and apply comic outline parameters.
    /// Works with Custom/ComicOutline shader.
    /// </summary>
    [DisallowMultipleComponent]
    public class ComicOutlineHelper : MonoBehaviour
    {
        [Header("Comic Outline Parameters")]
        [Tooltip("Ink outline color.")]
        [SerializeField] private Color outlineColor = new Color(0.05f, 0.05f, 0.05f, 1f);

        [Tooltip("Ink outline thickness in screen pixels (recommended: 1.2 to 2.5).")]
        [Range(0.5f, 5.0f)]
        [SerializeField] private float outlineWidth = 1.8f;

        private Renderer _renderer;
        private MaterialPropertyBlock _propBlock;

        private static readonly int OutlineColorProp = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthProp = Shader.PropertyToID("_OutlineWidth");

        private void Awake()
        {
            _renderer = GetComponentInChildren<Renderer>();
            _propBlock = new MaterialPropertyBlock();
            ApplyProperties();
        }

        private void OnValidate()
        {
            if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
            ApplyProperties();
        }

        /// <summary>
        /// Updates the comic outline material properties via MaterialPropertyBlock.
        /// </summary>
        public void ApplyProperties()
        {
            if (_renderer == null) return;

            _renderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(OutlineColorProp, outlineColor);
            _propBlock.SetFloat(OutlineWidthProp, outlineWidth);
            _renderer.SetPropertyBlock(_propBlock);
        }

        public void SetOutlineWidth(float width)
        {
            outlineWidth = width;
            ApplyProperties();
        }

        public void SetOutlineColor(Color color)
        {
            outlineColor = color;
            ApplyProperties();
        }
    }
}
