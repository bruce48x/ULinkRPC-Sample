#nullable enable

using System;
using TMPro;
using ULinkRPC.Client;
using UnityEngine;
using UnityEngine.UI;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    internal struct DotArenaSceneUiSnapshot
    {
        public bool HasSession { get; set; }
        public EntryMenuState EntryMenuState { get; set; }
        public SessionMode SessionMode { get; set; }
        public string Status { get; set; }
        public string LocalPlayerId { get; set; }
        public string Account { get; set; }
        public string Password { get; set; }
        public string LocalPlayerScoreText { get; set; }
        public int LocalWinCount { get; set; }
        public int LastWorldTick { get; set; }
        public int ViewCount { get; set; }
        public string LocalPlayerBuffText { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string Path { get; set; }
        public string CurrentEventMessage { get; set; }
        public int LastRoundRemainingSeconds { get; set; }
        public string MenuLoginStatusText { get; set; }
        public bool IsConnecting { get; set; }
    }

    internal sealed class DotArenaSceneUiPresenter
    {
        private Transform? _owner;
        private GameObject? _sceneUiRoot;
        private GameObject? _hudPanel;
        private GameObject? _entryPanel;
        private GameObject? _modeSelectPanel;
        private GameObject? _multiplayerPanel;
        private TMP_Text? _hudTitleText;
        private TMP_Text? _hudStatusText;
        private TMP_Text? _hudPlayerText;
        private TMP_Text? _hudTickText;
        private TMP_Text? _hudModeText;
        private TMP_Text? _hudHintText;
        private TMP_Text? _hudEventText;
        private TMP_Text? _hudCountdownText;
        private TMP_Text? _entryTitleText;
        private TMP_Text? _entryStatusText;
        private TMP_Text? _modeSelectDescriptionText;
        private TMP_Text? _multiplayerSubtitleText;
        private TMP_Text? _accountLabelText;
        private TMP_Text? _passwordLabelText;
        private TMP_Text? _accountPlaceholderText;
        private TMP_Text? _passwordPlaceholderText;
        private Button? _singlePlayerButton;
        private Button? _multiplayerButton;
        private Button? _matchButton;
        private Button? _backButton;
        private TMP_Text? _singlePlayerButtonText;
        private TMP_Text? _multiplayerButtonText;
        private TMP_Text? _matchButtonText;
        private TMP_Text? _backButtonText;
        private TMP_InputField? _accountInputField;
        private TMP_InputField? _passwordInputField;
        private TMP_FontAsset? _tmpFontAsset;

        public bool HasSceneUi => _sceneUiRoot != null;

        public RectTransform? OverlayLayer { get; private set; }

        public void Bind(
            Transform owner,
            Action onSinglePlayerSelected,
            Action onMultiplayerSelected,
            Action onConnectRequested,
            Action onBackToModeSelect,
            Action<string> onAccountChanged,
            Action<string> onPasswordChanged)
        {
            _owner = owner;
            _sceneUiRoot = FindSceneUiObject("SceneUI");
            if (_sceneUiRoot == null)
            {
                return;
            }

            _tmpFontAsset ??= LoadTmpFontAsset();
            ApplySceneUiFonts();

            OverlayLayer = FindSceneUiRect("SceneUI/OverlayLayer");
            _hudPanel = FindSceneUiObject("SceneUI/HUDPanel");
            _entryPanel = FindSceneUiObject("SceneUI/EntryPanel");
            _modeSelectPanel = FindSceneUiObject("SceneUI/EntryPanel/ModeSelectPanel");
            _multiplayerPanel = FindSceneUiObject("SceneUI/EntryPanel/MultiplayerPanel");

            _hudTitleText = FindSceneUiText("SceneUI/HUDPanel/TitleText");
            _hudStatusText = FindSceneUiText("SceneUI/HUDPanel/StatusText");
            _hudPlayerText = FindSceneUiText("SceneUI/HUDPanel/PlayerText");
            _hudTickText = FindSceneUiText("SceneUI/HUDPanel/TickText");
            _hudModeText = FindSceneUiText("SceneUI/HUDPanel/ModeText");
            _hudHintText = FindSceneUiText("SceneUI/HUDPanel/HintText");
            _hudEventText = FindSceneUiText("SceneUI/HUDPanel/EventText");
            _hudCountdownText = FindSceneUiText("SceneUI/HUDPanel/CountdownText");
            EnsureHudCountdownText();

            _entryTitleText = FindSceneUiText("SceneUI/EntryPanel/TitleText");
            _entryStatusText = FindSceneUiText("SceneUI/EntryPanel/StatusText");
            _modeSelectDescriptionText = FindSceneUiText("SceneUI/EntryPanel/ModeSelectPanel/DescriptionText");

            _multiplayerSubtitleText = FindSceneUiText("SceneUI/EntryPanel/MultiplayerPanel/SubtitleText");
            _accountLabelText = FindSceneUiText("SceneUI/EntryPanel/MultiplayerPanel/AccountLabel");
            _passwordLabelText = FindSceneUiText("SceneUI/EntryPanel/MultiplayerPanel/PasswordLabel");
            EnsureMultiplayerLabelLayout();
            _accountPlaceholderText = FindSceneUiText("SceneUI/EntryPanel/MultiplayerPanel/AccountInput/Text Area/Placeholder");
            _passwordPlaceholderText = FindSceneUiText("SceneUI/EntryPanel/MultiplayerPanel/PasswordInput/Text Area/Placeholder");

            _singlePlayerButton = FindSceneUiButton("SceneUI/EntryPanel/ModeSelectPanel/SinglePlayerButton");
            _multiplayerButton = FindSceneUiButton("SceneUI/EntryPanel/ModeSelectPanel/MultiplayerButton");
            _matchButton = FindSceneUiButton("SceneUI/EntryPanel/MultiplayerPanel/MatchButton");
            _backButton = FindSceneUiButton("SceneUI/EntryPanel/MultiplayerPanel/BackButton");

            _singlePlayerButtonText = FindSceneUiText("SceneUI/EntryPanel/ModeSelectPanel/SinglePlayerButton/Label");
            _multiplayerButtonText = FindSceneUiText("SceneUI/EntryPanel/ModeSelectPanel/MultiplayerButton/Label");
            _matchButtonText = FindSceneUiText("SceneUI/EntryPanel/MultiplayerPanel/MatchButton/Label");
            _backButtonText = FindSceneUiText("SceneUI/EntryPanel/MultiplayerPanel/BackButton/Label");

            _accountInputField = FindSceneUiInputField("SceneUI/EntryPanel/MultiplayerPanel/AccountInput");
            _passwordInputField = FindSceneUiInputField("SceneUI/EntryPanel/MultiplayerPanel/PasswordInput");
            EnsureInputFieldViewport(_accountInputField);
            EnsureInputFieldViewport(_passwordInputField);

            ApplySceneUiTheme();

            if (_singlePlayerButton != null)
            {
                _singlePlayerButton.onClick.RemoveAllListeners();
                _singlePlayerButton.onClick.AddListener(() => onSinglePlayerSelected());
            }

            if (_multiplayerButton != null)
            {
                _multiplayerButton.onClick.RemoveAllListeners();
                _multiplayerButton.onClick.AddListener(() => onMultiplayerSelected());
            }

            if (_matchButton != null)
            {
                _matchButton.onClick.RemoveAllListeners();
                _matchButton.onClick.AddListener(() => onConnectRequested());
            }

            if (_backButton != null)
            {
                _backButton.onClick.RemoveAllListeners();
                _backButton.onClick.AddListener(() => onBackToModeSelect());
            }

            if (_accountInputField != null)
            {
                _accountInputField.onValueChanged.RemoveAllListeners();
                _accountInputField.onValueChanged.AddListener(value => onAccountChanged(value));
            }

            if (_passwordInputField != null)
            {
                _passwordInputField.onValueChanged.RemoveAllListeners();
                _passwordInputField.onValueChanged.AddListener(value => onPasswordChanged(value));
            }
        }

        public void Refresh(in DotArenaSceneUiSnapshot snapshot)
        {
            if (_sceneUiRoot == null)
            {
                return;
            }

            if (_hudPanel != null) _hudPanel.SetActive(snapshot.HasSession);
            if (_entryPanel != null) _entryPanel.SetActive(!snapshot.HasSession);
            if (_modeSelectPanel != null) _modeSelectPanel.SetActive(snapshot.EntryMenuState == EntryMenuState.ModeSelect);
            if (_multiplayerPanel != null) _multiplayerPanel.SetActive(snapshot.EntryMenuState == EntryMenuState.MultiplayerAuth);

            SetText(_hudTitleText, "ULinkRPC 点阵竞技场");
            SetText(_hudStatusText, $"状态: {snapshot.Status}");
            SetText(_hudPlayerText, $"玩家: {(snapshot.LocalPlayerId.Length > 0 ? snapshot.LocalPlayerId : snapshot.Account)}   积分: {snapshot.LocalPlayerScoreText}   胜场: {snapshot.LocalWinCount}");
            SetText(_hudTickText, $"Tick: {snapshot.LastWorldTick}   同步人数: {snapshot.ViewCount}   Buff: {snapshot.LocalPlayerBuffText}");
            SetText(_hudModeText, snapshot.SessionMode == SessionMode.SinglePlayer
                ? "模式: 本地单机"
                : $"地址: {Rpc.WebSocketRpcClientFactory.BuildUrl(snapshot.Host, snapshot.Port, snapshot.Path)}");
            SetText(_hudHintText, "W/A/S/D 移动，Space 冲刺。位置以权威状态为准。");
            SetText(_hudEventText, $"事件: {snapshot.CurrentEventMessage}");
            if (snapshot.HasSession)
            {
                if (snapshot.LastRoundRemainingSeconds > 0)
                {
                    var minutes = snapshot.LastRoundRemainingSeconds / 60;
                    var seconds = snapshot.LastRoundRemainingSeconds % 60;
                    SetText(_hudCountdownText, $"Time: {minutes:D2}:{seconds:D2}");
                }
                else
                {
                    SetText(_hudCountdownText, "Time: --:--");
                }
            }
            else
            {
                SetText(_hudCountdownText, string.Empty);
            }

            SetText(_entryTitleText, "点阵竞技场");
            SetText(_entryStatusText, snapshot.Status);
            SetText(_modeSelectDescriptionText, $"选择模式。单机将立即开始，并补足 4 名 AI。\n{snapshot.MenuLoginStatusText}");
            SetText(_multiplayerSubtitleText, "联机匹配");
            SetText(_accountLabelText, "账号");
            SetText(_passwordLabelText, "密码");
            SetText(_accountPlaceholderText, "请输入账号");
            SetText(_passwordPlaceholderText, "请输入密码");
            SetText(_singlePlayerButtonText, "单机");
            SetText(_multiplayerButtonText, "联机");
            SetText(_matchButtonText, snapshot.IsConnecting ? "匹配中..." : "匹配");
            SetText(_backButtonText, "返回");

            if (_singlePlayerButton != null) _singlePlayerButton.interactable = !snapshot.IsConnecting;
            if (_multiplayerButton != null) _multiplayerButton.interactable = !snapshot.IsConnecting;
            if (_matchButton != null) _matchButton.interactable = !snapshot.IsConnecting;
            if (_backButton != null) _backButton.interactable = !snapshot.IsConnecting;
            if (_accountInputField != null) _accountInputField.interactable = !snapshot.IsConnecting;
            if (_passwordInputField != null) _passwordInputField.interactable = !snapshot.IsConnecting;

            SyncSceneUiInputs(snapshot.Account, snapshot.Password);
        }

        private void SyncSceneUiInputs(string account, string password)
        {
            if (_accountInputField != null && !_accountInputField.isFocused && _accountInputField.text != account)
            {
                _accountInputField.SetTextWithoutNotify(account);
            }

            if (_passwordInputField != null && !_passwordInputField.isFocused && _passwordInputField.text != password)
            {
                _passwordInputField.SetTextWithoutNotify(password);
            }
        }

        private GameObject? FindSceneUiObject(string path)
        {
            if (_owner == null)
            {
                return null;
            }

            var target = _owner.Find(path);
            return target != null ? target.gameObject : null;
        }

        private void ApplySceneUiFonts()
        {
            if (_sceneUiRoot == null)
            {
                return;
            }

            _tmpFontAsset ??= LoadTmpFontAsset();
            if (_tmpFontAsset == null)
            {
                return;
            }

            foreach (var text in _sceneUiRoot.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.font == null)
                {
                    text.font = _tmpFontAsset;
                }
            }
        }

        private void ApplySceneUiTheme()
        {
            StylePanelImage(_hudPanel, UiPanelBackgroundColor);
            StylePanelImage(_entryPanel, UiPanelBackgroundColor);

            StyleText(_hudTitleText, UiAccentTextColor, 16f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_entryTitleText, UiAccentTextColor, 22f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);

            StyleText(_hudStatusText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudPlayerText, UiSecondaryTextColor, 13f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudTickText, UiSecondaryTextColor, 13f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudModeText, UiSecondaryTextColor, 13f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudHintText, UiMutedTextColor, 12f, true, TextAlignmentOptions.TopLeft, TextOverflowModes.Truncate);
            StyleText(_hudEventText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudCountdownText, UiAccentTextColor, 14f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);

            StyleText(_entryStatusText, UiPrimaryTextColor, 14f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_modeSelectDescriptionText, UiSecondaryTextColor, 13f, true, TextAlignmentOptions.Top, TextOverflowModes.Truncate);
            StyleText(_multiplayerSubtitleText, UiPrimaryTextColor, 15f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_accountLabelText, UiSecondaryTextColor, 13f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);
            StyleText(_passwordLabelText, UiSecondaryTextColor, 13f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);
            StyleText(_accountPlaceholderText, UiMutedTextColor, 13f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);
            StyleText(_passwordPlaceholderText, UiMutedTextColor, 13f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);

            StyleButton(_singlePlayerButton);
            StyleButton(_multiplayerButton);
            StyleButton(_matchButton);
            StyleButton(_backButton);
            StyleText(_singlePlayerButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_multiplayerButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_matchButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_backButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);

            StyleInputField(_accountInputField);
            StyleInputField(_passwordInputField);
        }

        private static void StylePanelImage(GameObject? panel, Color color)
        {
            if (panel == null)
            {
                return;
            }

            if (panel.TryGetComponent<Image>(out var image))
            {
                image.color = color;
            }
        }

        private static void StyleText(
            TMP_Text? text,
            Color color,
            float fontSize,
            bool wrap,
            TextAlignmentOptions alignment,
            TextOverflowModes overflowMode)
        {
            if (text == null)
            {
                return;
            }

            text.color = color;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.enableWordWrapping = wrap;
            text.overflowMode = overflowMode;
            text.richText = false;
        }

        private static void StyleButton(Button? button)
        {
            if (button == null)
            {
                return;
            }

            var colors = button.colors;
            colors.normalColor = new Color(0.2f, 0.29f, 0.38f, 1f);
            colors.highlightedColor = new Color(0.27f, 0.39f, 0.5f, 1f);
            colors.pressedColor = new Color(0.14f, 0.22f, 0.3f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.2f, 0.2f, 0.22f, 0.7f);
            colors.colorMultiplier = 1f;
            button.colors = colors;
        }

        private static void StyleInputField(TMP_InputField? inputField)
        {
            if (inputField == null)
            {
                return;
            }

            if (inputField.targetGraphic is Image inputImage)
            {
                inputImage.color = UiInputBackgroundColor;
            }

            if (inputField.textComponent != null)
            {
                StyleText(inputField.textComponent, UiPrimaryTextColor, 14f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);
            }

            if (inputField.placeholder is TMP_Text placeholderText)
            {
                StyleText(placeholderText, UiMutedTextColor, 13f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);
            }
        }

        private static void EnsureInputFieldViewport(TMP_InputField? inputField)
        {
            if (inputField?.textViewport == null)
            {
                return;
            }

            var rect = inputField.textViewport;
            var currentHeight = rect.rect.height;
            if (currentHeight >= 18f)
            {
                return;
            }

            rect.offsetMin = new Vector2(10f, 4f);
            rect.offsetMax = new Vector2(-10f, -4f);
        }

        private static TMP_FontAsset? LoadTmpFontAsset()
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

            var fallback = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            return fallback ?? TMP_Settings.defaultFontAsset;
        }

        private TMP_Text? FindSceneUiText(string path)
        {
            if (_owner == null)
            {
                return null;
            }

            var target = _owner.Find(path);
            return target != null ? target.GetComponent<TMP_Text>() : null;
        }

        private void EnsureHudCountdownText()
        {
            if (_hudCountdownText != null || _hudPanel == null)
            {
                return;
            }

            var countdownObject = new GameObject("CountdownText", typeof(RectTransform), typeof(TextMeshProUGUI));
            countdownObject.transform.SetParent(_hudPanel.transform, false);

            var rect = (RectTransform)countdownObject.transform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-12f, -12f);
            rect.sizeDelta = new Vector2(160f, 24f);

            var text = countdownObject.GetComponent<TextMeshProUGUI>();
            text.font = _tmpFontAsset ??= LoadTmpFontAsset();
            text.fontSize = 14f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.TopRight;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.color = UiAccentTextColor;
            text.richText = false;
            _hudCountdownText = text;
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

        private Button? FindSceneUiButton(string path)
        {
            if (_owner == null)
            {
                return null;
            }

            var target = _owner.Find(path);
            return target != null ? target.GetComponent<Button>() : null;
        }

        private TMP_InputField? FindSceneUiInputField(string path)
        {
            if (_owner == null)
            {
                return null;
            }

            var target = _owner.Find(path);
            return target != null ? target.GetComponent<TMP_InputField>() : null;
        }

        private RectTransform? FindSceneUiRect(string path)
        {
            if (_owner == null)
            {
                return null;
            }

            var target = _owner.Find(path);
            return target != null ? target.GetComponent<RectTransform>() : null;
        }

        private static void SetText(TMP_Text? label, string value)
        {
            if (label == null || label.text == value)
            {
                return;
            }

            label.text = value;
        }
    }
}
