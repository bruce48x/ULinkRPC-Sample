#nullable enable

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SampleClient.Gameplay
{
    internal sealed partial class DotArenaSceneUiPresenter
    {
        private void EnsureModeSelectPanelContents()
        {
            if (_modeSelectPanel == null)
            {
                return;
            }

            EnsureModeSelectButton("SinglePlayerButton", new Vector2(0f, -124f), "单机：普通模式");
            EnsureModeSelectButton("InvincibleSinglePlayerButton", new Vector2(0f, -172f), "单机：无敌模式");
            EnsureModeSelectButton("MultiplayerButton", new Vector2(0f, -220f), "联机");
        }

        private void EnsureModeSelectButton(string name, Vector2 anchoredPosition, string label)
        {
            var button = FindSceneUiButton($"SceneUI/EntryPanel/ModeSelectPanel/{name}");
            if (button == null)
            {
                CreateSettlementButton(_modeSelectPanel!.transform, name, anchoredPosition, new Vector2(260f, 38f), label);
                button = FindSceneUiButton($"SceneUI/EntryPanel/ModeSelectPanel/{name}");
            }

            if (button == null)
            {
                return;
            }

            var rect = button.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = anchoredPosition;
                rect.sizeDelta = new Vector2(260f, 38f);
            }

            var text = FindSceneUiText($"SceneUI/EntryPanel/ModeSelectPanel/{name}/Label");
            if (text != null)
            {
                text.text = label;
            }
        }

        private void EnsureMultiplayerAuthActionButtons()
        {
            if (_multiplayerPanel == null)
            {
                return;
            }

            EnsureMultiplayerAuthButton("MatchButton", new Vector2(-70f, -216f), new Vector2(120f, 30f), "登录");
            EnsureMultiplayerAuthButton("BackButton", new Vector2(70f, -216f), new Vector2(120f, 30f), "返回");
            EnsureMultiplayerAuthButton("GuestLoginButton", new Vector2(0f, -250f), new Vector2(260f, 30f), "游客登录");
        }

        private void EnsureMultiplayerAuthButton(string name, Vector2 anchoredPosition, Vector2 size, string label)
        {
            var button = FindSceneUiButton($"SceneUI/EntryPanel/MultiplayerPanel/{name}");
            if (button == null)
            {
                CreateSettlementButton(_multiplayerPanel!.transform, name, anchoredPosition, size, label);
                button = FindSceneUiButton($"SceneUI/EntryPanel/MultiplayerPanel/{name}");
            }

            if (button == null)
            {
                return;
            }

            var rect = button.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = anchoredPosition;
                rect.sizeDelta = size;
            }

            var text = FindSceneUiText($"SceneUI/EntryPanel/MultiplayerPanel/{name}/Label");
            if (text != null)
            {
                text.text = label;
            }
        }

        private void EnsureLobbyQuickActionsText()
        {
            if (_sceneUiRoot == null || _lobbyPanel == null || _lobbyQuickActionsText != null)
            {
                return;
            }

            _lobbyQuickActionsText = FindSceneUiText("SceneUI/LobbyPanel/QuickActionsText");
            if (_lobbyQuickActionsText != null)
            {
                return;
            }

            CreateLobbyText(_lobbyPanel.transform, "QuickActionsText", new Vector2(-18f, -150f), new Vector2(380f, 40f), 12f, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            _lobbyQuickActionsText = FindSceneUiText("SceneUI/LobbyPanel/QuickActionsText");
        }

        private void EnsureLobbyQuickActionButtons()
        {
            if (_lobbyPanel == null)
            {
                return;
            }

            EnsureLobbyQuickActionButton("QuickActionButton1", new Vector2(-100f, -194f));
            EnsureLobbyQuickActionButton("QuickActionButton2", new Vector2(100f, -194f));
            EnsureLobbyQuickActionButton("QuickActionButton3", new Vector2(-100f, -236f));
            EnsureLobbyQuickActionButton("QuickActionButton4", new Vector2(100f, -236f));
        }

        private void EnsureLobbyQuickActionButton(string name, Vector2 anchoredPosition)
        {
            if (FindSceneUiButton($"SceneUI/LobbyPanel/{name}") != null)
            {
                return;
            }

            CreateLobbyButton(_lobbyPanel!.transform, name, anchoredPosition, new Vector2(132f, 34f), "Action");
        }

        private void CreateLobbyText(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, float fontSize, FontStyles fontStyles, TextAlignmentOptions alignment)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            var rect = (RectTransform)textObject.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = _tmpFontAsset ??= LoadTmpFontAsset();
            text.fontSize = fontSize;
            text.fontStyle = fontStyles;
            text.alignment = alignment;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = false;
        }

        private void CreateLobbyButton(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, string label)
        {
            CreateSettlementButton(parent, name, anchoredPosition, size, label);
        }

        private void EnsureSettlementPanel()
        {
            if (_sceneUiRoot == null)
            {
                return;
            }

            _settlementPanel = FindSceneUiObject("SceneUI/SettlementPanel");
            if (_settlementPanel != null)
            {
                EnsureSettlementPanelContents();
                return;
            }

            _settlementPanel = new GameObject("SettlementPanel", typeof(RectTransform), typeof(Image));
            _settlementPanel.transform.SetParent(_sceneUiRoot.transform, false);
            var panelRect = (RectTransform)_settlementPanel.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(420f, 372f);
            EnsureSettlementPanelContents();
            _settlementPanel.SetActive(false);
        }

        private void EnsureSettlementPanelContents()
        {
            if (_settlementPanel == null)
            {
                return;
            }

            var panelRect = (RectTransform)_settlementPanel.transform;
            panelRect.sizeDelta = new Vector2(420f, 372f);

            if (FindSceneUiText("SceneUI/SettlementPanel/TitleText") == null)
            {
                CreateSettlementText(_settlementPanel.transform, "TitleText", new Vector2(0f, -18f), new Vector2(340f, 32f), 22f, FontStyles.Bold);
            }

            if (FindSceneUiText("SceneUI/SettlementPanel/DetailText") == null)
            {
                CreateSettlementText(_settlementPanel.transform, "DetailText", new Vector2(0f, -58f), new Vector2(340f, 72f), 12f, FontStyles.Normal);
            }

            if (FindSceneUiText("SceneUI/SettlementPanel/RewardText") == null)
            {
                CreateSettlementText(_settlementPanel.transform, "RewardText", new Vector2(0f, -138f), new Vector2(340f, 42f), 13f, FontStyles.Normal);
            }

            if (FindSceneUiText("SceneUI/SettlementPanel/TaskText") == null)
            {
                CreateSettlementText(_settlementPanel.transform, "TaskText", new Vector2(0f, -182f), new Vector2(340f, 42f), 13f, FontStyles.Normal);
            }

            if (FindSceneUiText("SceneUI/SettlementPanel/NextStepText") == null)
            {
                CreateSettlementText(_settlementPanel.transform, "NextStepText", new Vector2(0f, -226f), new Vector2(340f, 42f), 13f, FontStyles.Normal);
            }

            if (FindSceneUiButton("SceneUI/SettlementPanel/PrimaryButton") == null)
            {
                CreateSettlementButton(_settlementPanel.transform, "PrimaryButton", new Vector2(0f, -286f), new Vector2(260f, 32f), "Play Again");
            }

            if (FindSceneUiButton("SceneUI/SettlementPanel/SecondaryButton") == null)
            {
                CreateSettlementButton(_settlementPanel.transform, "SecondaryButton", new Vector2(0f, -328f), new Vector2(260f, 32f), "Return to Lobby");
            }
        }

        private void EnsureMatchmakingPanel()
        {
            if (_sceneUiRoot == null)
            {
                return;
            }

            _matchmakingPanel = FindSceneUiObject("SceneUI/MatchmakingPanel");
            if (_matchmakingPanel != null)
            {
                return;
            }

            _matchmakingPanel = new GameObject("MatchmakingPanel", typeof(RectTransform), typeof(Image));
            _matchmakingPanel.transform.SetParent(_sceneUiRoot.transform, false);
            var panelRect = (RectTransform)_matchmakingPanel.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(420f, 240f);

            CreateSettlementText(_matchmakingPanel.transform, "TitleText", new Vector2(0f, -18f), new Vector2(340f, 32f), 22f, FontStyles.Bold);
            CreateSettlementText(_matchmakingPanel.transform, "DetailText", new Vector2(0f, -62f), new Vector2(340f, 100f), 13f, FontStyles.Normal);
            CreateSettlementButton(_matchmakingPanel.transform, "CancelButton", new Vector2(0f, -188f), new Vector2(260f, 32f), "Cancel");
            _matchmakingPanel.SetActive(false);
        }

        private void CreateSettlementText(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, float fontSize, FontStyles fontStyles)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            var rect = (RectTransform)textObject.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = _tmpFontAsset ??= LoadTmpFontAsset();
            text.fontSize = fontSize;
            text.fontStyle = fontStyles;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = false;
        }

        private void CreateSettlementButton(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, string label)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var text = labelObject.GetComponent<TextMeshProUGUI>();
            text.font = _tmpFontAsset ??= LoadTmpFontAsset();
            text.fontSize = 13f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = false;
            text.text = label;
        }
    }
}
