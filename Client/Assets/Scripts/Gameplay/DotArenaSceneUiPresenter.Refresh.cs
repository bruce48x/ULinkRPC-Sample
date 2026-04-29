#nullable enable

using UnityEngine;

namespace SampleClient.Gameplay
{
    internal sealed partial class DotArenaSceneUiPresenter
    {
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
            SetText(_matchmakingCancelButtonText, "取消匹配");
            SetText(_lobbyTitleText, GetLobbyTabTitle(snapshot, _selectedLobbyTab));
            SetText(_lobbySummaryText, snapshot.MetaPlayerSummary);
            SetText(_lobbyHighlightsText, _selectedLobbyTab == MetaTab.Lobby ? snapshot.MetaLobbyHighlights : string.Empty);
            SetText(_lobbyQuickActionsText, GetLobbyQuickActionsText(snapshot, _selectedLobbyTab));
            RefreshLobbyQuickActionButtons(snapshot, _selectedLobbyTab);
            SetText(_lobbyDetailText, GetLobbyTabDetail(snapshot, _selectedLobbyTab));
            SetText(_lobbyFooterText, snapshot.MetaFooterHint);
            SetText(_lobbyPrimaryActionButtonText, GetLobbyPrimaryActionLabel(snapshot, _selectedLobbyTab));
            SetText(_lobbySecondaryActionButtonText, GetLobbySecondaryActionLabel(snapshot, _selectedLobbyTab));
            ApplyLobbyActionLayout(snapshot);
            SetText(_multiplayerSubtitleText, "联机登录");
            SetText(_accountLabelText, "账号");
            SetText(_passwordLabelText, "密码");
            SetText(_accountPlaceholderText, "请输入账号");
            SetText(_passwordPlaceholderText, "请输入密码");
            SetText(_singlePlayerButtonText, "单机");
            SetText(_multiplayerButtonText, "联机");
            SetText(_matchButtonText, snapshot.IsConnecting ? "登录中..." : "登录");
            SetText(_backButtonText, "返回");

            if (_singlePlayerButton != null) _singlePlayerButton.interactable = !snapshot.IsBusy;
            if (_multiplayerButton != null) _multiplayerButton.interactable = !snapshot.IsBusy;
            if (_matchButton != null) _matchButton.interactable = !snapshot.IsBusy;
            if (_backButton != null) _backButton.interactable = !snapshot.IsBusy;
            if (_matchmakingCancelButton != null) _matchmakingCancelButton.interactable = !snapshot.IsBusy;
            if (_lobbyProfileButton != null) _lobbyProfileButton.interactable = !snapshot.IsBusy && _selectedLobbyTab != MetaTab.Lobby;
            if (_lobbyTasksButton != null) _lobbyTasksButton.interactable = !snapshot.IsBusy && _selectedLobbyTab != MetaTab.Tasks;
            if (_lobbyShopButton != null) _lobbyShopButton.interactable = !snapshot.IsBusy && _selectedLobbyTab != MetaTab.Shop;
            if (_lobbyRecordsButton != null) _lobbyRecordsButton.interactable = !snapshot.IsBusy && _selectedLobbyTab != MetaTab.Records;
            if (_lobbyLeaderboardButton != null) _lobbyLeaderboardButton.interactable = !snapshot.IsBusy && _selectedLobbyTab != MetaTab.Leaderboard;
            if (_lobbySettingsButton != null) _lobbySettingsButton.interactable = !snapshot.IsBusy && _selectedLobbyTab != MetaTab.Settings;
            if (_lobbyPrimaryActionButton != null) _lobbyPrimaryActionButton.gameObject.SetActive(HasLobbyPrimaryAction(snapshot, _selectedLobbyTab));
            if (_lobbySecondaryActionButton != null) _lobbySecondaryActionButton.gameObject.SetActive(HasLobbySecondaryAction(snapshot, _selectedLobbyTab));
            if (_lobbyPrimaryActionButton != null) _lobbyPrimaryActionButton.interactable = !snapshot.IsBusy;
            if (_lobbySecondaryActionButton != null) _lobbySecondaryActionButton.interactable = !snapshot.IsBusy;
            if (_lobbyQuickActionsText != null) _lobbyQuickActionsText.gameObject.SetActive(!string.IsNullOrWhiteSpace(_lobbyQuickActionsText.text));
            if (_lobbyQuickActionButton1 != null) _lobbyQuickActionButton1.interactable = !snapshot.IsBusy;
            if (_lobbyQuickActionButton2 != null) _lobbyQuickActionButton2.interactable = !snapshot.IsBusy;
            if (_lobbyQuickActionButton3 != null) _lobbyQuickActionButton3.interactable = !snapshot.IsBusy;
            if (_lobbyQuickActionButton4 != null) _lobbyQuickActionButton4.interactable = !snapshot.IsBusy;
            if (_accountInputField != null) _accountInputField.interactable = !snapshot.IsBusy;
            if (_passwordInputField != null) _passwordInputField.interactable = !snapshot.IsBusy;

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

        private void ApplyLobbyActionLayout(in DotArenaSceneUiSnapshot snapshot)
        {
            var isMultiplayerLobby = snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby &&
                                     snapshot.SessionMode == SessionMode.Multiplayer;

            if (isMultiplayerLobby)
            {
                ApplyMultiplayerLobbyActionLayout();
                return;
            }

            SetAnchoredPosition(_lobbyDetailText?.rectTransform, new Vector2(0f, -326f));
            SetSizeDelta(_lobbyDetailText?.rectTransform, new Vector2(980f, 290f));
            SetAnchoredPosition(_lobbyPrimaryActionButton?.GetComponent<RectTransform>(), new Vector2(-120f, -650f));
            SetAnchoredPosition(_lobbySecondaryActionButton?.GetComponent<RectTransform>(), new Vector2(120f, -650f));
            SetAnchoredPosition(_lobbyFooterText?.rectTransform, new Vector2(0f, -708f));
            SetSizeDelta(_lobbyFooterText?.rectTransform, new Vector2(980f, 24f));
        }

        private void ApplyMultiplayerLobbyActionLayout()
        {
            var panelRect = _lobbyPanel?.GetComponent<RectTransform>();
            var primaryRect = _lobbyPrimaryActionButton?.GetComponent<RectTransform>();
            var secondaryRect = _lobbySecondaryActionButton?.GetComponent<RectTransform>();
            var detailRect = _lobbyDetailText?.rectTransform;
            var footerRect = _lobbyFooterText?.rectTransform;
            if (panelRect == null || primaryRect == null || secondaryRect == null || detailRect == null || footerRect == null)
            {
                return;
            }

            var panelHeight = panelRect.rect.height;
            var footerHeight = footerRect.sizeDelta.y > 0f ? footerRect.sizeDelta.y : 24f;
            var buttonHeight = Mathf.Max(primaryRect.sizeDelta.y, secondaryRect.sizeDelta.y, 42f);
            const float detailTop = 238f;
            const float detailGap = 18f;
            const float buttonGap = 24f;
            const float bottomPadding = 30f;
            const float buttonXOffset = 120f;

            var footerTop = panelHeight - footerHeight - bottomPadding;
            var buttonTop = footerTop - buttonGap - buttonHeight;
            var detailHeight = Mathf.Max(84f, buttonTop - detailTop - detailGap);

            SetAnchoredPosition(detailRect, new Vector2(0f, -detailTop));
            SetSizeDelta(detailRect, new Vector2(980f, detailHeight));
            SetAnchoredPosition(primaryRect, new Vector2(-buttonXOffset, -buttonTop));
            SetAnchoredPosition(secondaryRect, new Vector2(buttonXOffset, -buttonTop));
            SetAnchoredPosition(footerRect, new Vector2(0f, -footerTop));
            SetSizeDelta(footerRect, new Vector2(980f, footerHeight));
        }

        private static void SetAnchoredPosition(RectTransform? rectTransform, Vector2 anchoredPosition)
        {
            if (rectTransform != null)
            {
                rectTransform.anchoredPosition = anchoredPosition;
            }
        }

        private static void SetSizeDelta(RectTransform? rectTransform, Vector2 sizeDelta)
        {
            if (rectTransform != null)
            {
                rectTransform.sizeDelta = sizeDelta;
            }
        }
    }
}
