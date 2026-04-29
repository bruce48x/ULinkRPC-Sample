#nullable enable

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        private void BindSceneUi()
        {
            _sceneUiPresenter.Bind(
                transform,
                OnUiSinglePlayerSelected,
                OnUiMultiplayerSelected,
                OnUiConnectRequested,
                OnUiBackToModeSelect,
                OnUiCancelMatchmakingRequested,
                OnUiAccountChanged,
                OnUiPasswordChanged,
                OnUiLobbyActionRequested,
                OnUiRematchRequested,
                OnUiReturnToLobbyRequested);
        }

        private void RefreshSceneUi()
        {
            _sceneUiPresenter.Refresh(BuildSceneUiSnapshot());
        }

        private DotArenaSceneUiSnapshot BuildSceneUiSnapshot()
        {
            var settlementSummary = _settlementSummary;
            var previewPreset = DotArenaSinglePlayerCatalog.PeekPreset(_singlePlayerPlaylistIndex);
            var endpoint = Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path);
            return new DotArenaSceneUiSnapshot
            {
                HasSession = HasActiveSession,
                FlowState = _flowState,
                EntryMenuState = _entryMenuState,
                SessionMode = _sessionMode,
                Status = _status,
                LocalPlayerId = _localPlayerId,
                Account = _account,
                Password = _password,
                LocalPlayerScoreText = GetLocalPlayerScoreText(),
                LocalWinCount = _localWinCount,
                LastWorldTick = _lastWorldTick,
                ViewCount = _views.Count,
                LocalPlayerBuffText = GetLocalPlayerBuffText(),
                DebugPanelVisible = _showDebugPanel,
                DebugPanelDetail = DotArenaUiTextComposer.BuildDebugPanelDetail(
                    _status,
                    _flowState,
                    _entryMenuState,
                    _sessionMode,
                    _localPlayerId,
                    _lastWorldTick,
                    _views.Count,
                    GetLocalPlayerBuffText(),
                    GetCurrentEventMessage(),
                    endpoint,
                    IsConnected,
                    IsConnecting),
                Host = _host,
                Port = _port,
                Path = _path,
                CurrentEventMessage = GetCurrentEventMessage(),
                LastRoundRemainingSeconds = _lastRoundRemainingSeconds,
                MenuLoginStatusText = GetMenuLoginStatusText(),
                IsConnecting = IsConnecting,
                SettlementTitle = settlementSummary?.Title ?? string.Empty,
                SettlementDetail = settlementSummary?.Detail ?? string.Empty,
                SettlementRewardSummary = settlementSummary?.RewardSummary ?? string.Empty,
                SettlementTaskSummary = settlementSummary?.TaskSummary ?? string.Empty,
                SettlementNextStepSummary = settlementSummary?.NextStepSummary ?? string.Empty,
                SettlementPrimaryActionText = settlementSummary == null
                    ? string.Empty
                    : DotArenaUiTextComposer.GetRematchButtonLabel(settlementSummary.SessionMode),
                MatchmakingTitle = _sessionMode == SessionMode.SinglePlayer ? "Preparing Local Match" : "Matchmaking",
                MatchmakingDetail = DotArenaUiTextComposer.BuildMatchmakingDetail(_sessionMode, _currentArenaMapVariant, _currentArenaRuleVariant, _status, GetCurrentEventMessage()),
                MetaPlayerSummary = DotArenaUiTextComposer.BuildMetaPlayerSummary(_metaState, IsInMultiplayerLobby()),
                MetaLobbyHighlights = DotArenaUiTextComposer.BuildMetaLobbyHighlights(_metaState, IsInMultiplayerLobby(), previewPreset),
                MetaProfileDetail = DotArenaUiTextComposer.BuildMetaProfileDetail(_metaState, IsInMultiplayerLobby(), previewPreset, _lastRewardSummary, endpoint),
                MetaTasksDetail = DotArenaUiTextComposer.BuildMetaTasksDetail(_metaState),
                MetaShopDetail = DotArenaUiTextComposer.BuildMetaShopDetail(_metaState),
                MetaRecordsDetail = DotArenaUiTextComposer.BuildMetaRecordsDetail(_metaState),
                MetaLeaderboardDetail = DotArenaUiTextComposer.BuildMetaLeaderboardDetail(_metaState),
                MetaSettingsDetail = DotArenaUiTextComposer.BuildMetaSettingsDetail(_metaState),
                MetaFooterHint = DotArenaUiTextComposer.BuildMetaFooterHint(IsInMultiplayerLobby())
            };
        }
    }
}
