#nullable enable

using Shared.Interfaces;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    internal sealed class DotView
    {
        private readonly SpriteRenderer _renderer;
        private readonly SpriteRenderer _outlineRenderer;
        private readonly TextMesh _nameText;
        private readonly TextMesh _scoreText;
        private float _impactUntil;

        public DotView(GameObject root, SpriteRenderer renderer, SpriteRenderer outlineRenderer, TextMesh nameText, TextMesh scoreText)
        {
            Root = root;
            _renderer = renderer;
            _outlineRenderer = outlineRenderer;
            _nameText = nameText;
            _scoreText = scoreText;
        }

        public GameObject Root { get; }

        public Vector2 GetPosition()
        {
            var position = Root.transform.position;
            return new Vector2(position.x, position.y);
        }

        public void SetPosition(Vector2 position)
        {
            Root.transform.position = new Vector3(position.x, position.y, 0f);
        }

        public void TriggerCollisionJelly()
        {
            _impactUntil = Time.time + 0.28f;
        }

        public void UpdateJelly(float time)
        {
            var remaining = Mathf.Clamp01((_impactUntil - time) / 0.28f);
            var pulse = remaining * remaining;
            UpdateMaterial(_renderer, time, pulse, 1f);
            UpdateMaterial(_outlineRenderer, time, pulse, 1.2f);
        }

        public void SetIdentity(string playerId, int score)
        {
            _nameText.text = playerId;
            _scoreText.text = DotArenaPresentation.FormatScore(score);
        }

        public void ApplyPresentation(Color baseColor, PlayerLifeState state, bool alive, bool hasSpeedBoost, bool hasKnockbackBoost)
        {
            var color = baseColor;
            if (!alive)
            {
                color = new Color(baseColor.r * 0.35f, baseColor.g * 0.35f, baseColor.b * 0.35f, 0.55f);
            }
            else if (state == PlayerLifeState.Dash)
            {
                color = Color.Lerp(baseColor, Color.white, 0.3f);
            }
            else if (state == PlayerLifeState.Stunned)
            {
                color = Color.Lerp(baseColor, new Color(1f, 0.9f, 0.45f, 1f), 0.45f);
            }

            if (hasSpeedBoost)
            {
                color = Color.Lerp(color, SpeedPickupColor, 0.28f);
            }

            if (hasKnockbackBoost)
            {
                color = Color.Lerp(color, KnockbackPickupColor, 0.33f);
            }

            _renderer.color = color;
            _outlineRenderer.color = alive
                ? PlayerOutlineColor
                : new Color(PlayerOutlineColor.r, PlayerOutlineColor.g, PlayerOutlineColor.b, 0.45f);
            var scaleBoost = hasSpeedBoost || hasKnockbackBoost ? 1.08f : 1f;
            Root.transform.localScale = new Vector3(PlayerVisualDiameter * scaleBoost, PlayerVisualDiameter * scaleBoost, 1f);
            _outlineRenderer.transform.localScale = new Vector3(1.14f, 1.14f, 1f);
        }

        private static void UpdateMaterial(SpriteRenderer renderer, float time, float impactPulse, float wobbleScale)
        {
            var material = renderer.material;
            if (material == null || !material.HasProperty("_WobbleAmount"))
            {
                return;
            }

            var wobble = (0.18f + (impactPulse * 0.62f)) * wobbleScale;
            var speed = 4.8f + (impactPulse * 9.5f);
            material.SetFloat("_WobbleAmount", wobble);
            material.SetFloat("_WobbleSpeed", speed + (Mathf.Sin(time * 1.3f) * 0.15f));
        }

        private static float PlayerVisualDiameter => GameplayConfig.PlayerVisualRadius * 2f;
    }
}
