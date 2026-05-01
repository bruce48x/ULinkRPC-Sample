#nullable enable

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    internal sealed class DotArenaPlayerOverlayPresenter
    {
        private readonly Dictionary<string, PlayerOverlayView> _views = new(StringComparer.Ordinal);

        public Dictionary<string, PlayerOverlayView> Views => _views;

        public void EnsureOverlay(DotArenaSceneUiPresenter sceneUiPresenter, string playerId)
        {
            var overlayLayer = sceneUiPresenter.OverlayLayer;
            if (overlayLayer == null || _views.ContainsKey(playerId))
            {
                return;
            }

            var root = new GameObject($"{playerId}Overlay", typeof(RectTransform));
            root.transform.SetParent(overlayLayer, false);

            var rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = new Vector2(140f, 40f);

            var nameObject = new GameObject("NameText", typeof(RectTransform), typeof(TextMeshProUGUI));
            nameObject.transform.SetParent(root.transform, false);
            var nameRect = (RectTransform)nameObject.transform;
            nameRect.anchorMin = new Vector2(0.5f, 0.5f);
            nameRect.anchorMax = new Vector2(0.5f, 0.5f);
            nameRect.pivot = new Vector2(0.5f, 0.5f);
            nameRect.anchoredPosition = new Vector2(0f, -10f);
            nameRect.sizeDelta = new Vector2(140f, 20f);

            var nameText = nameObject.GetComponent<TextMeshProUGUI>();
            nameText.font = ResolveOverlayFontAsset();
            nameText.fontSize = 16;
            nameText.fontStyle = FontStyles.Bold;
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.enableWordWrapping = false;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            nameText.color = UiPrimaryTextColor;

            var scoreObject = new GameObject("ScoreText", typeof(RectTransform), typeof(TextMeshProUGUI));
            scoreObject.transform.SetParent(root.transform, false);
            var scoreRect = (RectTransform)scoreObject.transform;
            scoreRect.anchorMin = new Vector2(0.5f, 0.5f);
            scoreRect.anchorMax = new Vector2(0.5f, 0.5f);
            scoreRect.pivot = new Vector2(0.5f, 0.5f);
            scoreRect.anchoredPosition = new Vector2(0f, 8f);
            scoreRect.sizeDelta = new Vector2(140f, 18f);

            var scoreText = scoreObject.GetComponent<TextMeshProUGUI>();
            scoreText.font = ResolveOverlayFontAsset();
            scoreText.fontSize = 14;
            scoreText.fontStyle = FontStyles.Bold;
            scoreText.alignment = TextAlignmentOptions.Center;
            scoreText.enableWordWrapping = false;
            scoreText.overflowMode = TextOverflowModes.Ellipsis;
            scoreText.color = UiAccentTextColor;

            _views.Add(playerId, new PlayerOverlayView(root, rootRect, nameText, scoreText));
        }

        public void UpdateOverlayViews(
            DotArenaSceneUiPresenter sceneUiPresenter,
            IReadOnlyDictionary<string, DotView> worldViews,
            IReadOnlyDictionary<string, PlayerRenderState> renderStates)
        {
            if (sceneUiPresenter.OverlayLayer == null)
            {
                return;
            }

            var camera = Camera.main;
            if (camera == null)
            {
                foreach (var overlay in _views.Values)
                {
                    overlay.Root.SetActive(false);
                }

                return;
            }

            var pixelsPerWorldUnit = Screen.height / (camera.orthographicSize * 2f);

            foreach (var entry in _views)
            {
                if (!worldViews.TryGetValue(entry.Key, out var view) ||
                    !renderStates.TryGetValue(entry.Key, out var renderState))
                {
                    entry.Value.Root.SetActive(false);
                    continue;
                }

                var screenPosition = camera.WorldToScreenPoint(view.Root.transform.position);
                if (screenPosition.z <= 0f)
                {
                    entry.Value.Root.SetActive(false);
                    continue;
                }

                entry.Value.Root.SetActive(true);
                var serverRadius = !float.IsNaN(renderState.Radius) && !float.IsInfinity(renderState.Radius) && renderState.Radius > 0f
                    ? renderState.Radius
                    : GameplayConfig.PlayerVisualRadius;
                var diameterPixels = serverRadius * 2f * pixelsPerWorldUnit;
                var labelWidth = Mathf.Max(96f, diameterPixels * 2f);
                var nameHeight = Mathf.Max(18f, diameterPixels * 0.36f);
                var scoreHeight = Mathf.Max(16f, diameterPixels * 0.3f);

                entry.Value.RootRect.anchoredPosition = new Vector2(screenPosition.x, screenPosition.y);
                entry.Value.RootRect.sizeDelta = new Vector2(labelWidth, nameHeight + scoreHeight + 4f);

                var nameRect = entry.Value.NameText.rectTransform;
                nameRect.sizeDelta = new Vector2(labelWidth, nameHeight);
                nameRect.anchoredPosition = new Vector2(0f, nameHeight * 0.55f);
                entry.Value.NameText.fontSize = Mathf.RoundToInt(Mathf.Clamp(diameterPixels * 0.24f, 14f, 22f));

                var scoreRect = entry.Value.ScoreText.rectTransform;
                scoreRect.sizeDelta = new Vector2(labelWidth, scoreHeight);
                scoreRect.anchoredPosition = new Vector2(0f, -(scoreHeight * 0.55f));
                entry.Value.ScoreText.fontSize = Mathf.RoundToInt(Mathf.Clamp(diameterPixels * 0.22f, 13f, 20f));
            }
        }

        public void Clear(Action<UnityEngine.Object> destroyObject)
        {
            foreach (var overlay in _views.Values)
            {
                destroyObject(overlay.Root);
            }

            _views.Clear();
        }

        private static TMP_FontAsset? ResolveOverlayFontAsset()
        {
            var projectFont = Resources.Load<TMP_FontAsset>(TmpFallbackFontAssetResourcePath);
            if (projectFont != null)
            {
                return projectFont;
            }

            if (TMP_Settings.defaultFontAsset != null)
            {
                return TMP_Settings.defaultFontAsset;
            }

            return Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        }
    }
}
