#nullable enable

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    internal sealed partial class DotArenaSceneUiPresenter
    {
        private void EnsureHudCountdownText()
        {
            var parent = OverlayLayer != null ? OverlayLayer.transform : _sceneUiRoot?.transform;
            if (parent == null)
            {
                return;
            }

            if (_hudCountdownText != null)
            {
                _hudCountdownText.transform.SetParent(parent, false);
                var existingRect = _hudCountdownText.rectTransform;
                existingRect.anchorMin = new Vector2(0.5f, 1f);
                existingRect.anchorMax = new Vector2(0.5f, 1f);
                existingRect.pivot = new Vector2(0.5f, 1f);
                existingRect.anchoredPosition = new Vector2(0f, -10f);
                existingRect.sizeDelta = new Vector2(220f, 28f);
                _hudCountdownText.alignment = TextAlignmentOptions.Center;
                _hudCountdownText.fontSize = 18f;
                _hudCountdownText.fontStyle = FontStyles.Bold;
                _hudCountdownText.color = UiAccentTextColor;
                _hudCountdownText.overflowMode = TextOverflowModes.Ellipsis;
                _hudCountdownText.enableWordWrapping = false;
                _hudCountdownText.richText = false;
                return;
            }

            var countdownObject = new GameObject("CountdownText", typeof(RectTransform), typeof(TextMeshProUGUI));
            countdownObject.transform.SetParent(parent, false);
            var rect = (RectTransform)countdownObject.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -10f);
            rect.sizeDelta = new Vector2(220f, 28f);

            var text = countdownObject.GetComponent<TextMeshProUGUI>();
            text.font = _tmpFontAsset ??= LoadTmpFontAsset();
            text.fontSize = 14f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.color = UiAccentTextColor;
            text.richText = false;
            _hudCountdownText = text;
        }

        private void EnsureDebugPanel()
        {
            if (_sceneUiRoot == null)
            {
                return;
            }

            _debugPanel = FindSceneUiObject("SceneUI/DebugPanel");
            if (_debugPanel != null)
            {
                EnsureDebugPanelContents();
                return;
            }

            _debugPanel = new GameObject("DebugPanel", typeof(RectTransform), typeof(Image));
            _debugPanel.transform.SetParent(_sceneUiRoot.transform, false);
            var panelRect = (RectTransform)_debugPanel.transform;
            panelRect.anchorMin = new Vector2(1f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = new Vector2(-16f, -68f);
            panelRect.sizeDelta = new Vector2(300f, 170f);
            EnsureDebugPanelContents();
            _debugPanel.SetActive(false);
        }

        private void EnsureDebugPanelContents()
        {
            if (_debugPanel == null)
            {
                return;
            }

            var panelRect = (RectTransform)_debugPanel.transform;
            panelRect.sizeDelta = new Vector2(300f, 170f);

            if (FindSceneUiText("SceneUI/DebugPanel/TitleText") == null)
            {
                CreateSettlementText(_debugPanel.transform, "TitleText", new Vector2(-110f, -14f), new Vector2(220f, 26f), 16f, FontStyles.Bold);
            }

            if (FindSceneUiText("SceneUI/DebugPanel/DetailText") == null)
            {
                CreateSettlementText(_debugPanel.transform, "DetailText", new Vector2(0f, -34f), new Vector2(260f, 120f), 12f, FontStyles.Normal);
            }
        }

        private void EnsureMultiplayerLabelLayout()
        {
            FixMultiplayerLabelRect(_accountLabelText, -132f);
            FixMultiplayerLabelRect(_passwordLabelText, -168f);
        }

        private static void FixMultiplayerLabelRect(TMP_Text? label, float y)
        {
            if (label == null)
            {
                return;
            }

            var rect = label.rectTransform;
            var misplaced = rect.anchorMin == new Vector2(0f, 1f) && rect.anchorMax == new Vector2(0f, 1f) && rect.anchoredPosition.x < -100f;
            if (!misplaced)
            {
                return;
            }

            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(-136f, y);
        }

        private void EnsureLobbyPanel()
        {
            if (_sceneUiRoot == null)
            {
                return;
            }

            _lobbyPanel = FindSceneUiObject("SceneUI/LobbyPanel");
            if (_lobbyPanel != null)
            {
                EnsureLobbyPanelContents();
                return;
            }

            _lobbyPanel = new GameObject("LobbyPanel", typeof(RectTransform), typeof(Image));
            _lobbyPanel.transform.SetParent(_sceneUiRoot.transform, false);
            EnsureLobbyPanelContents();
            _lobbyPanel.SetActive(false);
        }

        private void EnsureLobbyPanelContents()
        {
            if (_lobbyPanel == null)
            {
                return;
            }

            var panelRect = (RectTransform)_lobbyPanel.transform;
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.offsetMin = new Vector2(36f, 36f);
            panelRect.offsetMax = new Vector2(-36f, -36f);

            EnsureLobbyTextElement("TitleText", new Vector2(0f, -22f), new Vector2(720f, 38f), 24f, FontStyles.Bold, TextAlignmentOptions.Center);
            EnsureLobbyTextElement("SummaryText", new Vector2(0f, -72f), new Vector2(980f, 30f), 14f, FontStyles.Normal, TextAlignmentOptions.Center);
            EnsureLobbyButtonElement("ProfileButton", new Vector2(-300f, -128f), new Vector2(120f, 34f), "Profile");
            EnsureLobbyButtonElement("TasksButton", new Vector2(-180f, -128f), new Vector2(110f, 34f), "Tasks");
            EnsureLobbyButtonElement("ShopButton", new Vector2(-60f, -128f), new Vector2(110f, 34f), "Shop");
            EnsureLobbyButtonElement("RecordsButton", new Vector2(60f, -128f), new Vector2(110f, 34f), "Records");
            EnsureLobbyButtonElement("LeaderboardButton", new Vector2(190f, -128f), new Vector2(130f, 34f), "Board");
            EnsureLobbyButtonElement("SettingsButton", new Vector2(330f, -128f), new Vector2(130f, 34f), "Settings");
            EnsureLobbyTextElement("HighlightsText", new Vector2(0f, -184f), new Vector2(980f, 56f), 14f, FontStyles.Bold, TextAlignmentOptions.Center);
            EnsureLobbyTextElement("QuickActionsText", new Vector2(-410f, -250f), new Vector2(220f, 28f), 13f, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            EnsureLobbyButtonElement("QuickActionButton1", new Vector2(-220f, -246f), new Vector2(180f, 40f), "Action");
            EnsureLobbyButtonElement("QuickActionButton2", new Vector2(-20f, -246f), new Vector2(180f, 40f), "Action");
            EnsureLobbyButtonElement("QuickActionButton3", new Vector2(180f, -246f), new Vector2(180f, 40f), "Action");
            EnsureLobbyButtonElement("QuickActionButton4", new Vector2(380f, -246f), new Vector2(180f, 40f), "Action");
            EnsureLobbyTextElement("DetailText", new Vector2(0f, -326f), new Vector2(980f, 290f), 14f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            EnsureLobbyButtonElement("PrimaryActionButton", new Vector2(-120f, -650f), new Vector2(220f, 42f), "Action");
            EnsureLobbyButtonElement("SecondaryActionButton", new Vector2(120f, -650f), new Vector2(220f, 42f), "Action");
            EnsureLobbyTextElement("FooterText", new Vector2(0f, -708f), new Vector2(980f, 24f), 12f, FontStyles.Normal, TextAlignmentOptions.Center);
        }

        private void EnsureLobbyTextElement(string name, Vector2 anchoredPosition, Vector2 size, float fontSize, FontStyles fontStyles, TextAlignmentOptions alignment)
        {
            if (_lobbyPanel == null)
            {
                return;
            }

            var text = FindSceneUiText($"SceneUI/LobbyPanel/{name}");
            if (text == null)
            {
                CreateLobbyText(_lobbyPanel.transform, name, anchoredPosition, size, fontSize, fontStyles, alignment);
                text = FindSceneUiText($"SceneUI/LobbyPanel/{name}");
            }

            if (text == null)
            {
                return;
            }

            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            text.fontSize = fontSize;
            text.fontStyle = fontStyles;
            text.alignment = alignment;
        }

        private void EnsureLobbyButtonElement(string name, Vector2 anchoredPosition, Vector2 size, string label)
        {
            if (_lobbyPanel == null)
            {
                return;
            }

            var button = FindSceneUiButton($"SceneUI/LobbyPanel/{name}");
            if (button == null)
            {
                CreateLobbyButton(_lobbyPanel.transform, name, anchoredPosition, size, label);
                button = FindSceneUiButton($"SceneUI/LobbyPanel/{name}");
            }

            if (button == null)
            {
                return;
            }

            var rect = button.GetComponent<RectTransform>();
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }
    }
}
