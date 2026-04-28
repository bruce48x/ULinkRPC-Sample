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
        public FrontendFlowState FlowState { get; set; }
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
        public bool DebugPanelVisible { get; set; }
        public string DebugPanelDetail { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string Path { get; set; }
        public string CurrentEventMessage { get; set; }
        public int LastRoundRemainingSeconds { get; set; }
        public string MenuLoginStatusText { get; set; }
        public bool IsConnecting { get; set; }
        public string SettlementTitle { get; set; }
        public string SettlementDetail { get; set; }
        public string SettlementRewardSummary { get; set; }
        public string SettlementTaskSummary { get; set; }
        public string SettlementNextStepSummary { get; set; }
        public string SettlementPrimaryActionText { get; set; }
        public string MatchmakingTitle { get; set; }
        public string MatchmakingDetail { get; set; }
        public string MetaPlayerSummary { get; set; }
        public string MetaLobbyHighlights { get; set; }
        public string MetaProfileDetail { get; set; }
        public string MetaTasksDetail { get; set; }
        public string MetaShopDetail { get; set; }
        public string MetaRecordsDetail { get; set; }
        public string MetaLeaderboardDetail { get; set; }
        public string MetaSettingsDetail { get; set; }
        public string MetaFooterHint { get; set; }
    }

    internal sealed class DotArenaSceneUiPresenter
    {
        private Transform? _owner;
        private GameObject? _sceneUiRoot;
        private GameObject? _hudPanel;
        private GameObject? _debugPanel;
        private GameObject? _entryPanel;
        private GameObject? _matchmakingPanel;
        private GameObject? _lobbyPanel;
        private GameObject? _modeSelectPanel;
        private GameObject? _multiplayerPanel;
        private TMP_Text? _matchmakingTitleText;
        private TMP_Text? _matchmakingDetailText;
        private Button? _matchmakingCancelButton;
        private TMP_Text? _matchmakingCancelButtonText;
        private TMP_Text? _lobbyTitleText;
        private TMP_Text? _lobbySummaryText;
        private TMP_Text? _lobbyHighlightsText;
        private TMP_Text? _lobbyQuickActionsText;
        private Button? _lobbyQuickActionButton1;
        private Button? _lobbyQuickActionButton2;
        private Button? _lobbyQuickActionButton3;
        private Button? _lobbyQuickActionButton4;
        private TMP_Text? _lobbyQuickActionButton1Text;
        private TMP_Text? _lobbyQuickActionButton2Text;
        private TMP_Text? _lobbyQuickActionButton3Text;
        private TMP_Text? _lobbyQuickActionButton4Text;
        private TMP_Text? _lobbyDetailText;
        private TMP_Text? _lobbyFooterText;
        private Button? _lobbyPrimaryActionButton;
        private Button? _lobbySecondaryActionButton;
        private TMP_Text? _lobbyPrimaryActionButtonText;
        private TMP_Text? _lobbySecondaryActionButtonText;
        private Button? _lobbyProfileButton;
        private Button? _lobbyTasksButton;
        private Button? _lobbyShopButton;
        private Button? _lobbyRecordsButton;
        private Button? _lobbyLeaderboardButton;
        private Button? _lobbySettingsButton;
        private TMP_Text? _hudTitleText;
        private TMP_Text? _hudStatusText;
        private TMP_Text? _hudPlayerText;
        private TMP_Text? _hudTickText;
        private TMP_Text? _hudModeText;
        private TMP_Text? _hudHintText;
        private TMP_Text? _hudEventText;
        private TMP_Text? _hudCountdownText;
        private TMP_Text? _debugTitleText;
        private TMP_Text? _debugDetailText;
        private TMP_Text? _entryTitleText;
        private TMP_Text? _entryStatusText;
        private TMP_Text? _multiplayerSubtitleText;
        private TMP_Text? _accountLabelText;
        private TMP_Text? _passwordLabelText;
        private TMP_Text? _accountPlaceholderText;
        private TMP_Text? _passwordPlaceholderText;
        private Button? _singlePlayerButton;
        private Button? _multiplayerButton;
        private Button? _matchButton;
        private Button? _backButton;
        private GameObject? _settlementPanel;
        private TMP_Text? _settlementTitleText;
        private TMP_Text? _settlementDetailText;
        private TMP_Text? _settlementRewardText;
        private TMP_Text? _settlementTaskText;
        private TMP_Text? _settlementNextStepText;
        private Button? _settlementPrimaryButton;
        private Button? _settlementSecondaryButton;
        private TMP_Text? _settlementPrimaryButtonText;
        private TMP_Text? _settlementSecondaryButtonText;
        private TMP_Text? _singlePlayerButtonText;
        private TMP_Text? _multiplayerButtonText;
        private TMP_Text? _matchButtonText;
        private TMP_Text? _backButtonText;
        private TMP_InputField? _accountInputField;
        private TMP_InputField? _passwordInputField;
        private TMP_FontAsset? _tmpFontAsset;
        private MetaTab _selectedLobbyTab = MetaTab.Lobby;

        public bool HasSceneUi => _sceneUiRoot != null;

        public RectTransform? OverlayLayer { get; private set; }

        public void Bind(
            Transform owner,
            Action onSinglePlayerSelected,
            Action onMultiplayerSelected,
            Action onConnectRequested,
            Action onBackToModeSelect,
            Action onCancelMatchmakingRequested,
            Action<string> onAccountChanged,
            Action<string> onPasswordChanged,
            Action<MetaTab, bool> onLobbyActionRequested,
            Action onRematchRequested,
            Action onReturnToLobbyRequested)
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
            EnsureDebugPanel();
            _debugPanel = FindSceneUiObject("SceneUI/DebugPanel");
            _entryPanel = FindSceneUiObject("SceneUI/EntryPanel");
            EnsureMatchmakingPanel();
            _matchmakingPanel = FindSceneUiObject("SceneUI/MatchmakingPanel");
            EnsureLobbyPanel();
            _lobbyPanel = FindSceneUiObject("SceneUI/LobbyPanel");
            EnsureLobbyQuickActionsText();
            EnsureLobbyQuickActionButtons();
            _modeSelectPanel = FindSceneUiObject("SceneUI/EntryPanel/ModeSelectPanel");
            _multiplayerPanel = FindSceneUiObject("SceneUI/EntryPanel/MultiplayerPanel");
            EnsureSettlementPanel();

            _hudTitleText = FindSceneUiText("SceneUI/HUDPanel/TitleText");
            _hudStatusText = FindSceneUiText("SceneUI/HUDPanel/StatusText");
            _hudPlayerText = FindSceneUiText("SceneUI/HUDPanel/PlayerText");
            _hudTickText = FindSceneUiText("SceneUI/HUDPanel/TickText");
            _hudModeText = FindSceneUiText("SceneUI/HUDPanel/ModeText");
            _hudHintText = FindSceneUiText("SceneUI/HUDPanel/HintText");
            _hudEventText = FindSceneUiText("SceneUI/HUDPanel/EventText");
            _hudCountdownText = FindSceneUiText("SceneUI/OverlayLayer/CountdownText")
                ?? FindSceneUiText("SceneUI/CountdownText")
                ?? FindSceneUiText("SceneUI/HUDPanel/CountdownText");
            _debugTitleText = FindSceneUiText("SceneUI/DebugPanel/TitleText");
            _debugDetailText = FindSceneUiText("SceneUI/DebugPanel/DetailText");
            EnsureHudCountdownText();

            _entryTitleText = FindSceneUiText("SceneUI/EntryPanel/TitleText");
            _entryStatusText = FindSceneUiText("SceneUI/EntryPanel/StatusText");
            _matchmakingTitleText = FindSceneUiText("SceneUI/MatchmakingPanel/TitleText");
            _matchmakingDetailText = FindSceneUiText("SceneUI/MatchmakingPanel/DetailText");
            _matchmakingCancelButton = FindSceneUiButton("SceneUI/MatchmakingPanel/CancelButton");
            _matchmakingCancelButtonText = FindSceneUiText("SceneUI/MatchmakingPanel/CancelButton/Label");
            _lobbyTitleText = FindSceneUiText("SceneUI/LobbyPanel/TitleText");
            _lobbySummaryText = FindSceneUiText("SceneUI/LobbyPanel/SummaryText");
            _lobbyHighlightsText = FindSceneUiText("SceneUI/LobbyPanel/HighlightsText");
            _lobbyQuickActionsText = FindSceneUiText("SceneUI/LobbyPanel/QuickActionsText");
            _lobbyQuickActionButton1 = FindSceneUiButton("SceneUI/LobbyPanel/QuickActionButton1");
            _lobbyQuickActionButton2 = FindSceneUiButton("SceneUI/LobbyPanel/QuickActionButton2");
            _lobbyQuickActionButton3 = FindSceneUiButton("SceneUI/LobbyPanel/QuickActionButton3");
            _lobbyQuickActionButton4 = FindSceneUiButton("SceneUI/LobbyPanel/QuickActionButton4");
            _lobbyQuickActionButton1Text = FindSceneUiText("SceneUI/LobbyPanel/QuickActionButton1/Label");
            _lobbyQuickActionButton2Text = FindSceneUiText("SceneUI/LobbyPanel/QuickActionButton2/Label");
            _lobbyQuickActionButton3Text = FindSceneUiText("SceneUI/LobbyPanel/QuickActionButton3/Label");
            _lobbyQuickActionButton4Text = FindSceneUiText("SceneUI/LobbyPanel/QuickActionButton4/Label");
            _lobbyDetailText = FindSceneUiText("SceneUI/LobbyPanel/DetailText");
            _lobbyFooterText = FindSceneUiText("SceneUI/LobbyPanel/FooterText");
            _lobbyPrimaryActionButton = FindSceneUiButton("SceneUI/LobbyPanel/PrimaryActionButton");
            _lobbySecondaryActionButton = FindSceneUiButton("SceneUI/LobbyPanel/SecondaryActionButton");
            _lobbyPrimaryActionButtonText = FindSceneUiText("SceneUI/LobbyPanel/PrimaryActionButton/Label");
            _lobbySecondaryActionButtonText = FindSceneUiText("SceneUI/LobbyPanel/SecondaryActionButton/Label");
            _lobbyProfileButton = FindSceneUiButton("SceneUI/LobbyPanel/ProfileButton");
            _lobbyTasksButton = FindSceneUiButton("SceneUI/LobbyPanel/TasksButton");
            _lobbyShopButton = FindSceneUiButton("SceneUI/LobbyPanel/ShopButton");
            _lobbyRecordsButton = FindSceneUiButton("SceneUI/LobbyPanel/RecordsButton");
            _lobbyLeaderboardButton = FindSceneUiButton("SceneUI/LobbyPanel/LeaderboardButton");
            _lobbySettingsButton = FindSceneUiButton("SceneUI/LobbyPanel/SettingsButton");

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
            _settlementTitleText = FindSceneUiText("SceneUI/SettlementPanel/TitleText");
            _settlementDetailText = FindSceneUiText("SceneUI/SettlementPanel/DetailText");
            _settlementRewardText = FindSceneUiText("SceneUI/SettlementPanel/RewardText");
            _settlementTaskText = FindSceneUiText("SceneUI/SettlementPanel/TaskText");
            _settlementNextStepText = FindSceneUiText("SceneUI/SettlementPanel/NextStepText");
            _settlementPrimaryButton = FindSceneUiButton("SceneUI/SettlementPanel/PrimaryButton");
            _settlementSecondaryButton = FindSceneUiButton("SceneUI/SettlementPanel/SecondaryButton");
            _settlementPrimaryButtonText = FindSceneUiText("SceneUI/SettlementPanel/PrimaryButton/Label");
            _settlementSecondaryButtonText = FindSceneUiText("SceneUI/SettlementPanel/SecondaryButton/Label");

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

            if (_matchmakingCancelButton != null)
            {
                _matchmakingCancelButton.onClick.RemoveAllListeners();
                _matchmakingCancelButton.onClick.AddListener(() => onCancelMatchmakingRequested());
            }

            if (_lobbyProfileButton != null)
            {
                _lobbyProfileButton.onClick.RemoveAllListeners();
                _lobbyProfileButton.onClick.AddListener(() => _selectedLobbyTab = MetaTab.Lobby);
            }

            if (_lobbyTasksButton != null)
            {
                _lobbyTasksButton.onClick.RemoveAllListeners();
                _lobbyTasksButton.onClick.AddListener(() => _selectedLobbyTab = MetaTab.Tasks);
            }

            if (_lobbyShopButton != null)
            {
                _lobbyShopButton.onClick.RemoveAllListeners();
                _lobbyShopButton.onClick.AddListener(() => _selectedLobbyTab = MetaTab.Shop);
            }

            if (_lobbyRecordsButton != null)
            {
                _lobbyRecordsButton.onClick.RemoveAllListeners();
                _lobbyRecordsButton.onClick.AddListener(() => _selectedLobbyTab = MetaTab.Records);
            }

            if (_lobbyLeaderboardButton != null)
            {
                _lobbyLeaderboardButton.onClick.RemoveAllListeners();
                _lobbyLeaderboardButton.onClick.AddListener(() => _selectedLobbyTab = MetaTab.Leaderboard);
            }

            if (_lobbySettingsButton != null)
            {
                _lobbySettingsButton.onClick.RemoveAllListeners();
                _lobbySettingsButton.onClick.AddListener(() => _selectedLobbyTab = MetaTab.Settings);
            }

            BindQuickActionButton(_lobbyQuickActionButton1, 0);
            BindQuickActionButton(_lobbyQuickActionButton2, 1);
            BindQuickActionButton(_lobbyQuickActionButton3, 2);
            BindQuickActionButton(_lobbyQuickActionButton4, 3);

            if (_lobbyPrimaryActionButton != null)
            {
                _lobbyPrimaryActionButton.onClick.RemoveAllListeners();
                _lobbyPrimaryActionButton.onClick.AddListener(() => onLobbyActionRequested(_selectedLobbyTab, true));
            }

            if (_lobbySecondaryActionButton != null)
            {
                _lobbySecondaryActionButton.onClick.RemoveAllListeners();
                _lobbySecondaryActionButton.onClick.AddListener(() => onLobbyActionRequested(_selectedLobbyTab, false));
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

            if (_settlementPrimaryButton != null)
            {
                _settlementPrimaryButton.onClick.RemoveAllListeners();
                _settlementPrimaryButton.onClick.AddListener(() => onRematchRequested());
            }

            if (_settlementSecondaryButton != null)
            {
                _settlementSecondaryButton.onClick.RemoveAllListeners();
                _settlementSecondaryButton.onClick.AddListener(() => onReturnToLobbyRequested());
            }
        }

        public void Refresh(in DotArenaSceneUiSnapshot snapshot)
        {
            if (_sceneUiRoot == null)
            {
                return;
            }

            var showSettlement = snapshot.FlowState == FrontendFlowState.Settlement;
            var showMatchmaking = snapshot.FlowState == FrontendFlowState.Matchmaking;
            var showHud = snapshot.HasSession && snapshot.FlowState == FrontendFlowState.InMatch;
            var showDebug = showHud && snapshot.DebugPanelVisible;
            var showLobby = !showSettlement &&
                            !showMatchmaking &&
                            !snapshot.HasSession &&
                            snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby;
            var showEntry = !showSettlement && !showMatchmaking && !showHud && !showLobby;

            if (_hudPanel != null) _hudPanel.SetActive(showHud);
            if (_debugPanel != null) _debugPanel.SetActive(showDebug);
            if (_entryPanel != null) _entryPanel.SetActive(showEntry);
            if (_matchmakingPanel != null) _matchmakingPanel.SetActive(showMatchmaking);
            if (_settlementPanel != null) _settlementPanel.SetActive(showSettlement);
            if (_lobbyPanel != null) _lobbyPanel.SetActive(showLobby);
            if (_modeSelectPanel != null) _modeSelectPanel.SetActive(snapshot.EntryMenuState == EntryMenuState.ModeSelect);
            if (_multiplayerPanel != null) _multiplayerPanel.SetActive(snapshot.EntryMenuState == EntryMenuState.MultiplayerAuth);

            SetText(_hudStatusText, $"State: {snapshot.LocalPlayerBuffText}");
            SetText(_hudPlayerText, $"玩家: {(snapshot.LocalPlayerId.Length > 0 ? snapshot.LocalPlayerId : snapshot.Account)}   分数/质量: {snapshot.LocalPlayerScoreText}   胜场: {snapshot.LocalWinCount}");
            SetText(_hudTickText, string.Empty);
            SetText(_hudTitleText, string.Empty);
            SetText(_hudModeText, string.Empty);
            SetText(_hudHintText, string.Empty);
            SetText(_hudEventText, string.Empty);
            SetText(_debugTitleText, "Debug");
            SetText(_debugDetailText, snapshot.DebugPanelDetail);
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
            SetText(_matchmakingTitleText, snapshot.MatchmakingTitle);
            SetText(_matchmakingDetailText, snapshot.MatchmakingDetail);
            SetText(_matchmakingCancelButtonText, "Cancel");
            SetText(_lobbyTitleText, GetLobbyTabTitle(snapshot, _selectedLobbyTab));
            SetText(_lobbySummaryText, snapshot.MetaPlayerSummary);
            SetText(_lobbyHighlightsText, _selectedLobbyTab == MetaTab.Lobby ? snapshot.MetaLobbyHighlights : string.Empty);
            SetText(_lobbyQuickActionsText, GetLobbyQuickActionsText(snapshot, _selectedLobbyTab));
            RefreshLobbyQuickActionButtons(snapshot, _selectedLobbyTab);
            SetText(_lobbyDetailText, GetLobbyTabDetail(snapshot, _selectedLobbyTab));
            SetText(_lobbyFooterText, snapshot.MetaFooterHint);
            SetText(_lobbyPrimaryActionButtonText, GetLobbyPrimaryActionLabel(snapshot, _selectedLobbyTab));
            SetText(_lobbySecondaryActionButtonText, GetLobbySecondaryActionLabel(snapshot, _selectedLobbyTab));
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
            if (_lobbyProfileButton != null) _lobbyProfileButton.interactable = _selectedLobbyTab != MetaTab.Lobby;
            if (_lobbyTasksButton != null) _lobbyTasksButton.interactable = _selectedLobbyTab != MetaTab.Tasks;
            if (_lobbyShopButton != null) _lobbyShopButton.interactable = _selectedLobbyTab != MetaTab.Shop;
            if (_lobbyRecordsButton != null) _lobbyRecordsButton.interactable = _selectedLobbyTab != MetaTab.Records;
            if (_lobbyLeaderboardButton != null) _lobbyLeaderboardButton.interactable = _selectedLobbyTab != MetaTab.Leaderboard;
            if (_lobbySettingsButton != null) _lobbySettingsButton.interactable = _selectedLobbyTab != MetaTab.Settings;
            if (_lobbyPrimaryActionButton != null) _lobbyPrimaryActionButton.gameObject.SetActive(HasLobbyPrimaryAction(snapshot, _selectedLobbyTab));
            if (_lobbySecondaryActionButton != null) _lobbySecondaryActionButton.gameObject.SetActive(HasLobbySecondaryAction(snapshot, _selectedLobbyTab));
            if (_accountInputField != null) _accountInputField.interactable = !snapshot.IsConnecting;
            if (_passwordInputField != null) _passwordInputField.interactable = !snapshot.IsConnecting;

            SyncSceneUiInputs(snapshot.Account, snapshot.Password);
            SetText(_settlementTitleText, snapshot.SettlementTitle);
            SetText(_settlementDetailText, snapshot.SettlementDetail);
            SetText(_settlementRewardText, snapshot.SettlementRewardSummary);
            SetText(_settlementTaskText, snapshot.SettlementTaskSummary);
            SetText(_settlementNextStepText, snapshot.SettlementNextStepSummary);
            SetText(_settlementPrimaryButtonText, snapshot.SettlementPrimaryActionText);
            SetText(_settlementSecondaryButtonText, "Return to Lobby");
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
            StylePanelImage(_hudPanel, Color.clear);
            StylePanelImage(_debugPanel, UiPanelBackgroundColor);
            StylePanelImage(_entryPanel, UiPanelBackgroundColor);
            StylePanelImage(_matchmakingPanel, UiPanelBackgroundColor);
            StylePanelImage(_lobbyPanel, UiPanelBackgroundColor);
            StylePanelImage(_settlementPanel, UiPanelBackgroundColor);

            StyleText(_hudTitleText, UiMutedTextColor, 1f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_entryTitleText, UiAccentTextColor, 22f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);

            StyleText(_hudStatusText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudPlayerText, UiSecondaryTextColor, 13f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudTickText, UiMutedTextColor, 1f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudModeText, UiMutedTextColor, 1f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudHintText, UiMutedTextColor, 1f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudEventText, UiMutedTextColor, 1f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudCountdownText, UiAccentTextColor, 18f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_debugTitleText, UiAccentTextColor, 16f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_debugDetailText, UiSecondaryTextColor, 12f, true, TextAlignmentOptions.TopLeft, TextOverflowModes.Overflow);

            StyleText(_entryStatusText, UiPrimaryTextColor, 14f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_matchmakingTitleText, UiAccentTextColor, 22f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_matchmakingDetailText, UiSecondaryTextColor, 13f, true, TextAlignmentOptions.Top, TextOverflowModes.Overflow);
            StyleText(_lobbyTitleText, UiAccentTextColor, 22f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_lobbySummaryText, UiSecondaryTextColor, 14f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_lobbyHighlightsText, UiAccentTextColor, 14f, true, TextAlignmentOptions.Center, TextOverflowModes.Overflow);
            StyleText(_lobbyQuickActionsText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_lobbyQuickActionButton1Text, UiPrimaryTextColor, 12f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_lobbyQuickActionButton2Text, UiPrimaryTextColor, 12f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_lobbyQuickActionButton3Text, UiPrimaryTextColor, 12f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_lobbyQuickActionButton4Text, UiPrimaryTextColor, 12f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_lobbyDetailText, UiSecondaryTextColor, 14f, true, TextAlignmentOptions.TopLeft, TextOverflowModes.Overflow);
            StyleText(_lobbyFooterText, UiMutedTextColor, 12f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_multiplayerSubtitleText, UiPrimaryTextColor, 15f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_accountLabelText, UiSecondaryTextColor, 13f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);
            StyleText(_passwordLabelText, UiSecondaryTextColor, 13f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);
            StyleText(_accountPlaceholderText, UiMutedTextColor, 13f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);
            StyleText(_passwordPlaceholderText, UiMutedTextColor, 13f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);

            StyleButton(_singlePlayerButton);
            StyleButton(_multiplayerButton);
            StyleButton(_matchButton);
            StyleButton(_backButton);
            StyleButton(_matchmakingCancelButton);
            StyleButton(_lobbyPrimaryActionButton);
            StyleButton(_lobbySecondaryActionButton);
            StyleButton(_lobbyProfileButton);
            StyleButton(_lobbyTasksButton);
            StyleButton(_lobbyShopButton);
            StyleButton(_lobbyRecordsButton);
            StyleButton(_lobbyLeaderboardButton);
            StyleButton(_lobbySettingsButton);
            StyleButton(_lobbyQuickActionButton1);
            StyleButton(_lobbyQuickActionButton2);
            StyleButton(_lobbyQuickActionButton3);
            StyleButton(_lobbyQuickActionButton4);
            StyleButton(_settlementPrimaryButton);
            StyleButton(_settlementSecondaryButton);
            StyleText(_singlePlayerButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_multiplayerButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_matchButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_backButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_matchmakingCancelButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_lobbyPrimaryActionButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_lobbySecondaryActionButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_settlementTitleText, UiAccentTextColor, 22f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_settlementDetailText, UiSecondaryTextColor, 13f, true, TextAlignmentOptions.Top, TextOverflowModes.Overflow);
            StyleText(_settlementPrimaryButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_settlementSecondaryButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);

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
                image.raycastTarget = color.a > 0f;
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

        private static string GetLobbyTabTitle(in DotArenaSceneUiSnapshot snapshot, MetaTab tab)
        {
            return tab switch
            {
                MetaTab.Lobby when snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby && snapshot.SessionMode == SessionMode.Multiplayer => "Multiplayer Lobby",
                MetaTab.Lobby => "Profile",
                MetaTab.Tasks => "Tasks",
                MetaTab.Shop => "Shop",
                MetaTab.Records => snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby ? "Match History" : "Records",
                MetaTab.Leaderboard => snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby ? "Lobby Board" : "Leaderboard",
                MetaTab.Settings => "Settings",
                _ => "Lobby"
            };
        }

        private static string GetLobbyTabDetail(in DotArenaSceneUiSnapshot snapshot, MetaTab tab)
        {
            return tab switch
            {
                MetaTab.Lobby => snapshot.MetaProfileDetail,
                MetaTab.Tasks => snapshot.MetaTasksDetail,
                MetaTab.Shop => snapshot.MetaShopDetail,
                MetaTab.Records => snapshot.MetaRecordsDetail,
                MetaTab.Leaderboard => snapshot.MetaLeaderboardDetail,
                MetaTab.Settings => snapshot.MetaSettingsDetail,
                _ => snapshot.MetaProfileDetail
            };
        }

        private static string GetLobbyQuickActionsText(in DotArenaSceneUiSnapshot snapshot, MetaTab tab)
        {
            return tab == MetaTab.Lobby
                ? "Quick Access"
                : "Sections";
        }

        private void BindQuickActionButton(Button? button, int index)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                var targetTab = GetLobbyQuickActionTarget(_selectedLobbyTab, index);
                if (targetTab.HasValue)
                {
                    _selectedLobbyTab = targetTab.Value;
                }
            });
        }

        private void RefreshLobbyQuickActionButtons(in DotArenaSceneUiSnapshot snapshot, MetaTab tab)
        {
            RefreshLobbyQuickActionButton(_lobbyQuickActionButton1, _lobbyQuickActionButton1Text, snapshot, tab, 0);
            RefreshLobbyQuickActionButton(_lobbyQuickActionButton2, _lobbyQuickActionButton2Text, snapshot, tab, 1);
            RefreshLobbyQuickActionButton(_lobbyQuickActionButton3, _lobbyQuickActionButton3Text, snapshot, tab, 2);
            RefreshLobbyQuickActionButton(_lobbyQuickActionButton4, _lobbyQuickActionButton4Text, snapshot, tab, 3);
        }

        private void RefreshLobbyQuickActionButton(Button? button, TMP_Text? label, in DotArenaSceneUiSnapshot snapshot, MetaTab tab, int index)
        {
            if (button == null)
            {
                return;
            }

            var text = GetLobbyQuickActionHint(snapshot, tab, index);
            var hasAction = !string.IsNullOrWhiteSpace(text);
            button.gameObject.SetActive(hasAction);
            if (hasAction)
            {
                SetText(label, text);
            }
        }

        private static bool HasLobbyPrimaryAction(in DotArenaSceneUiSnapshot snapshot, MetaTab tab)
        {
            return tab is MetaTab.Lobby or MetaTab.Tasks or MetaTab.Shop or MetaTab.Settings;
        }

        private static bool HasLobbySecondaryAction(in DotArenaSceneUiSnapshot snapshot, MetaTab tab)
        {
            return tab is MetaTab.Lobby or MetaTab.Tasks or MetaTab.Shop or MetaTab.Settings;
        }

        private static string GetLobbyPrimaryActionLabel(in DotArenaSceneUiSnapshot snapshot, MetaTab tab)
        {
            return tab switch
            {
                MetaTab.Lobby when snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby && snapshot.SessionMode == SessionMode.Multiplayer => "Start Match",
                MetaTab.Lobby => "Cycle Preset",
                MetaTab.Tasks => "Claim Ready",
                MetaTab.Shop => "Buy Cheapest",
                MetaTab.Settings => "Toggle Lang",
                _ => string.Empty
            };
        }

        private static string GetLobbySecondaryActionLabel(in DotArenaSceneUiSnapshot snapshot, MetaTab tab)
        {
            return tab switch
            {
                MetaTab.Lobby when snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby && snapshot.SessionMode == SessionMode.Multiplayer => "Log Out",
                MetaTab.Lobby => "Preview",
                MetaTab.Tasks => "Claim Next",
                MetaTab.Shop => "Equip Next",
                MetaTab.Settings => "Fullscreen",
                _ => string.Empty
            };
        }

        private static string GetLobbyQuickActionHint(in DotArenaSceneUiSnapshot snapshot, MetaTab tab, int index)
        {
            return (snapshot.EntryMenuState, snapshot.SessionMode, tab, index) switch
            {
                (EntryMenuState.MultiplayerLobby, SessionMode.Multiplayer, MetaTab.Lobby, 0) => "Match History",
                (EntryMenuState.MultiplayerLobby, SessionMode.Multiplayer, MetaTab.Lobby, 1) => "Lobby Board",
                (EntryMenuState.MultiplayerLobby, SessionMode.Multiplayer, MetaTab.Lobby, 2) => "Tasks",
                (EntryMenuState.MultiplayerLobby, SessionMode.Multiplayer, MetaTab.Lobby, 3) => "Shop",
                (EntryMenuState.MultiplayerLobby, SessionMode.Multiplayer, _, 0) => "Profile",
                (EntryMenuState.MultiplayerLobby, SessionMode.Multiplayer, _, 1) => "Shop",
                (EntryMenuState.MultiplayerLobby, SessionMode.Multiplayer, _, 2) => "Records",
                (EntryMenuState.MultiplayerLobby, SessionMode.Multiplayer, _, 3) => "Board",
                (_, SessionMode.SinglePlayer, MetaTab.Lobby, 0) => "Tasks",
                (_, SessionMode.SinglePlayer, MetaTab.Lobby, 1) => "Shop",
                (_, SessionMode.SinglePlayer, MetaTab.Lobby, 2) => "Board",
                (_, SessionMode.SinglePlayer, MetaTab.Lobby, 3) => "Settings",
                (_, SessionMode.SinglePlayer, MetaTab.Tasks, 0) => "Shop",
                (_, SessionMode.SinglePlayer, MetaTab.Tasks, 1) => "Settings",
                (_, SessionMode.SinglePlayer, MetaTab.Shop, 0) => "Tasks",
                (_, SessionMode.SinglePlayer, MetaTab.Shop, 1) => "Board",
                (_, SessionMode.SinglePlayer, MetaTab.Settings, 0) => "Tasks",
                (_, SessionMode.SinglePlayer, MetaTab.Settings, 1) => "Shop",
                (_, SessionMode.SinglePlayer, _, 0) => "Tasks",
                (_, SessionMode.SinglePlayer, _, 1) => "Shop",
                (_, _, MetaTab.Tasks, 0) => "Shop",
                (_, _, MetaTab.Tasks, 1) => "Profile",
                (_, _, MetaTab.Shop, 0) => "Tasks",
                (_, _, MetaTab.Shop, 1) => "Settings",
                (_, _, MetaTab.Records, 0) => "Profile",
                (_, _, MetaTab.Records, 1) => "Shop",
                (_, _, MetaTab.Leaderboard, 0) => "Profile",
                (_, _, MetaTab.Leaderboard, 1) => "Records",
                (_, _, MetaTab.Settings, 0) => "Tasks",
                (_, _, MetaTab.Settings, 1) => "Shop",
                (_, _, _, 0) => "Tasks",
                (_, _, _, 1) => "Shop",
                _ => string.Empty
            };
        }

        private MetaTab? GetLobbyQuickActionTarget(MetaTab currentTab, int index)
        {
            return (currentTab, index) switch
            {
                (MetaTab.Lobby, 0) => MetaTab.Records,
                (MetaTab.Lobby, 1) => MetaTab.Leaderboard,
                (MetaTab.Lobby, 2) => MetaTab.Tasks,
                (MetaTab.Lobby, 3) => MetaTab.Shop,
                (MetaTab.Tasks, 0) => MetaTab.Shop,
                (MetaTab.Tasks, 1) => MetaTab.Settings,
                (MetaTab.Tasks, 2) => MetaTab.Records,
                (MetaTab.Tasks, 3) => MetaTab.Leaderboard,
                (MetaTab.Shop, 0) => MetaTab.Tasks,
                (MetaTab.Shop, 1) => MetaTab.Leaderboard,
                (MetaTab.Shop, 2) => MetaTab.Records,
                (MetaTab.Shop, 3) => MetaTab.Settings,
                (MetaTab.Records, 0) => MetaTab.Lobby,
                (MetaTab.Records, 1) => MetaTab.Shop,
                (MetaTab.Records, 2) => MetaTab.Tasks,
                (MetaTab.Records, 3) => MetaTab.Settings,
                (MetaTab.Leaderboard, 0) => MetaTab.Lobby,
                (MetaTab.Leaderboard, 1) => MetaTab.Records,
                (MetaTab.Leaderboard, 2) => MetaTab.Tasks,
                (MetaTab.Leaderboard, 3) => MetaTab.Shop,
                (MetaTab.Settings, 0) => MetaTab.Tasks,
                (MetaTab.Settings, 1) => MetaTab.Shop,
                (MetaTab.Settings, 2) => MetaTab.Records,
                (MetaTab.Settings, 3) => MetaTab.Leaderboard,
                _ => null
            };
        }

        private static void AppendLobbyQuickAction(ref string actionLine, string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return;
            }

            if (actionLine.Length > 0)
            {
                actionLine += "  |  ";
            }

            actionLine += label;
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

        private void EnsureLobbyQuickActionsText()
        {
            if (_sceneUiRoot == null || _lobbyPanel == null)
            {
                return;
            }

            if (_lobbyQuickActionsText != null)
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
