#nullable enable

using UnityEngine;

namespace SampleClient.Gameplay
{
    internal sealed class PickupView
    {
        private readonly Color _baseGlowColor;
        private readonly Color _baseLabelColor;
        private Vector3 _absorbStartPosition;
        private Vector3 _absorbTargetPosition;
        private float _absorbStartedAt;
        private bool _isAbsorbing;

        public PickupView(GameObject root, SpriteRenderer renderer, SpriteRenderer glowRenderer, TextMesh labelText)
        {
            Root = root;
            Renderer = renderer;
            GlowRenderer = glowRenderer;
            LabelText = labelText;
            _baseGlowColor = glowRenderer.color;
            _baseLabelColor = labelText.color;
        }

        public GameObject Root { get; }
        public SpriteRenderer Renderer { get; }
        public SpriteRenderer GlowRenderer { get; }
        public TextMesh LabelText { get; }
        public bool IsAbsorbing => _isAbsorbing;

        public void ShowAt(Vector3 position, float scale)
        {
            _isAbsorbing = false;
            Root.SetActive(true);
            Root.transform.position = position;
            Root.transform.localScale = new Vector3(scale, scale, 1f);
            GlowRenderer.transform.localScale = Vector3.one * 1.24f;

            var glowColor = _baseGlowColor;
            glowColor.a = _baseGlowColor.a;
            GlowRenderer.color = glowColor;

            var labelColor = _baseLabelColor;
            labelColor.a = _baseLabelColor.a;
            LabelText.color = labelColor;

            var material = Renderer.material;
            if (material != null && material.HasProperty("_Dissolve"))
            {
                material.SetFloat("_Dissolve", 0f);
            }
        }

        public void StartAbsorb(Vector3 targetPosition, float time, float scale)
        {
            if (!Root.activeSelf)
            {
                return;
            }

            _isAbsorbing = true;
            _absorbStartedAt = time;
            _absorbStartPosition = Root.transform.position;
            _absorbTargetPosition = targetPosition;
            Root.transform.localScale = new Vector3(scale, scale, 1f);

            var material = Renderer.material;
            if (material != null && material.HasProperty("_Dissolve"))
            {
                material.SetFloat("_Dissolve", 0f);
            }
        }

        public void UpdateVisual(float time, float pulseScale, float absorbDurationSeconds)
        {
            if (!_isAbsorbing)
            {
                if (Root.activeSelf)
                {
                    Root.transform.localScale = new Vector3(pulseScale, pulseScale, 1f);
                }

                return;
            }

            var progress = Mathf.Clamp01((time - _absorbStartedAt) / absorbDurationSeconds);
            var eased = 1f - Mathf.Pow(1f - progress, 3f);
            Root.transform.position = Vector3.Lerp(_absorbStartPosition, _absorbTargetPosition, eased);
            var scale = Mathf.Lerp(pulseScale, pulseScale * 0.24f, eased);
            Root.transform.localScale = new Vector3(scale, scale, 1f);
            GlowRenderer.transform.localScale = Vector3.one * Mathf.Lerp(1.24f, 0.42f, eased);

            var material = Renderer.material;
            if (material != null && material.HasProperty("_Dissolve"))
            {
                material.SetFloat("_Dissolve", Mathf.SmoothStep(0f, 1f, progress));
            }

            var glowColor = _baseGlowColor;
            glowColor.a = Mathf.Lerp(_baseGlowColor.a, 0f, eased);
            GlowRenderer.color = glowColor;

            var labelColor = _baseLabelColor;
            labelColor.a = Mathf.Lerp(_baseLabelColor.a, 0f, Mathf.Clamp01(progress * 1.25f));
            LabelText.color = labelColor;

            if (progress >= 1f)
            {
                _isAbsorbing = false;
                Root.SetActive(false);
            }
        }
    }
}
