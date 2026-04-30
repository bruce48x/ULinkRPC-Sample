#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        private DotArenaGameUiSurface? _uiSurface;

        private DotArenaGameUiSurface UiSurface => _uiSurface ??= new DotArenaGameUiSurface(this);

        private sealed class DotArenaGameUiSurface
        {
            private readonly DotArenaGame _owner;

            public DotArenaGameUiSurface(DotArenaGame owner)
            {
                _owner = owner;
            }

            public void BindSceneUi()
            {
                _owner._sceneUiPresenter.Bind(
                    _owner.transform,
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

            public void RefreshSceneUi()
            {
                _owner._sceneUiPresenter.Refresh(BuildSceneUiSnapshot());
            }

            public void OnUiSinglePlayerSelected()
            {
                if (_owner.IsUiBusy)
                {
                    return;
                }

                _owner._singlePlayerStartRequested = true;
            }

            public void OnUiMultiplayerSelected()
            {
                if (_owner.IsUiBusy)
                {
                    return;
                }

                _owner._entryMenuState = EntryMenuState.MultiplayerAuth;
                _owner._status = "Enter account credentials";
                _owner._eventMessage = "点击匹配开始联机";
                RefreshSceneUi();
            }

            public void OnUiBackToModeSelect()
            {
                if (_owner.IsUiBusy)
                {
                    return;
                }

                _owner._entryMenuState = EntryMenuState.ModeSelect;
                _owner._status = "请选择模式";
                _owner._eventMessage = "请选择单机或联机";
                RefreshSceneUi();
            }

            public void OnUiCancelMatchmakingRequested()
            {
                if (_owner._flowState != FrontendFlowState.Matchmaking || _owner.HasPendingUiRequest)
                {
                    return;
                }

                _owner._pendingUiRequest = PendingUiRequest.CancelMatchmaking;
                _owner._status = "正在取消匹配";
                _owner._eventMessage = "正在返回联机大厅";
                RefreshSceneUi();
                _ = _owner.CancelMatchmakingAsync();
            }

            public void OnUiConnectRequested()
            {
                if (_owner.IsUiBusy)
                {
                    return;
                }

                _owner._pendingUiRequest = PendingUiRequest.Login;
                _owner._flowState = FrontendFlowState.Entry;
                _owner._entryMenuState = EntryMenuState.MultiplayerAuth;
                _owner._status = $"正在连接 {Rpc.WebSocketRpcClientFactory.BuildUrl(_owner._host, _owner._port, _owner._path)}";
                _owner._eventMessage = "正在登录联机账号";
                RefreshSceneUi();
                _ = _owner.ConnectAsync();
            }

            public void OnUiRematchRequested()
            {
                if (_owner._flowState != FrontendFlowState.Settlement || _owner.IsUiBusy)
                {
                    return;
                }

                _owner._rematchRequested = true;
            }

            public void OnUiReturnToLobbyRequested()
            {
                if (_owner._flowState != FrontendFlowState.Settlement || _owner.IsUiBusy)
                {
                    return;
                }

                _owner._returnToLobbyRequested = true;
            }

            public void OnUiAccountChanged(string value)
            {
                _owner._account = value;
            }

            public void OnUiPasswordChanged(string value)
            {
                _owner._password = value;
            }

            public void OnUiLobbyActionRequested(MetaTab tab, bool isPrimaryAction)
            {
                if (_owner._metaState == null || _owner._flowState == FrontendFlowState.Matchmaking || _owner.HasPendingUiRequest)
                {
                    return;
                }

                switch (tab)
                {
                    case MetaTab.Lobby:
                        HandleLobbyPresetAction(isPrimaryAction);
                        break;
                    case MetaTab.Tasks:
                        HandleTaskLobbyAction(isPrimaryAction);
                        break;
                    case MetaTab.Shop:
                        HandleShopLobbyAction(isPrimaryAction);
                        break;
                    case MetaTab.Settings:
                        HandleSettingsLobbyAction(isPrimaryAction);
                        break;
                }
            }

            public void HandleLobbyPresetAction(bool isPrimaryAction)
            {
                if (IsInMultiplayerLobby())
                {
                    if (isPrimaryAction)
                    {
                        _owner.BeginMultiplayerMatchmaking();
                    }
                    else
                    {
                        LogOutToModeSelect();
                    }

                    return;
                }

                if (!isPrimaryAction)
                {
                    var previewPreset = DotArenaSinglePlayerCatalog.PeekPreset(_owner._singlePlayerPlaylistIndex);
                    _owner.PushEvent($"Next local preset: {DotArenaSinglePlayerCatalog.GetPresetLabel(previewPreset.MapVariant, previewPreset.RuleVariant)}", 4f);
                    return;
                }

                var selectedPreset = DotArenaSinglePlayerCatalog.AdvancePresetSelection(ref _owner._singlePlayerPlaylistIndex);
                _owner.PushEvent($"Preset switched to {DotArenaSinglePlayerCatalog.GetPresetLabel(selectedPreset.MapVariant, selectedPreset.RuleVariant)}", 4f);
            }

            public bool IsInMultiplayerLobby()
            {
                return _owner._flowState == FrontendFlowState.Entry &&
                       _owner._entryMenuState == EntryMenuState.MultiplayerLobby &&
                       _owner._sessionMode == SessionMode.Multiplayer &&
                       _owner._hasAuthenticatedProfile &&
                       !string.IsNullOrWhiteSpace(_owner._authenticatedPlayerId);
            }

            public void LogOutToModeSelect()
            {
                if (_owner.HasPendingUiRequest)
                {
                    return;
                }

                _owner._pendingUiRequest = PendingUiRequest.ExitLobby;
                _owner._status = "正在退出联机大厅";
                _owner._eventMessage = "正在断开连接并注销会话";
                RefreshSceneUi();
                _ = ExitMultiplayerLobbyAsync();
            }

            public async Task ExitMultiplayerLobbyAsync()
            {
                try
                {
                    await _owner.DisposeConnectionAsync(clearSessionState: false, logout: true);
                    _owner.ResetToModeSelect(
                        status: "选择模式",
                        eventMessage: "已退出联机大厅",
                        toastMessage: "已断开连接并退出联机大厅");
                }
                finally
                {
                    _owner._pendingUiRequest = PendingUiRequest.None;
                }
            }

            public void HandleTaskLobbyAction(bool isPrimaryAction)
            {
                if (_owner._metaState == null)
                {
                    return;
                }

                var task = FindReadyTask(isPrimaryAction ? _owner._metaState.DailyTasks : _owner._metaState.NewPlayerTasks)
                    ?? FindReadyTask(isPrimaryAction ? _owner._metaState.NewPlayerTasks : _owner._metaState.DailyTasks);
                if (task == null)
                {
                    _owner.PushEvent("No claimable task right now.");
                    return;
                }

                if (DotArenaMetaProgression.TryClaimTaskById(_owner._metaState, task.TaskId))
                {
                    _owner.PushEvent($"Claimed task: {task.Title}");
                }
            }

            public void HandleShopLobbyAction(bool isPrimaryAction)
            {
                if (_owner._metaState == null)
                {
                    return;
                }

                if (isPrimaryAction)
                {
                    foreach (var item in DotArenaMetaProgression.GetShopCatalog())
                    {
                        if (_owner._metaState.OwnedCosmeticIds.Contains(item.Id))
                        {
                            continue;
                        }

                        if (DotArenaMetaProgression.TryPurchaseAndOptionallyEquip(_owner._metaState, item.Id, false))
                        {
                            _owner.PushEvent($"Purchased {item.Name}");
                            return;
                        }
                    }

                    _owner.PushEvent("No affordable cosmetic available.");
                    return;
                }

                var nextOwnedCosmeticId = GetNextOwnedCosmeticId();
                if (nextOwnedCosmeticId.Length == 0)
                {
                    _owner.PushEvent("No owned cosmetic to equip.");
                    return;
                }

                DotArenaMetaProgression.Equip(_owner._metaState, nextOwnedCosmeticId);
                _owner.PushEvent($"Equipped {nextOwnedCosmeticId}");
            }

            public void HandleSettingsLobbyAction(bool isPrimaryAction)
            {
                if (_owner._metaState == null)
                {
                    return;
                }

                if (isPrimaryAction)
                {
                    var nextLanguage = string.Equals(_owner._metaState.Settings.Language, "zh-CN", StringComparison.Ordinal)
                        ? "en-US"
                        : "zh-CN";
                    if (DotArenaMetaProgression.SetLanguage(_owner._metaState, nextLanguage))
                    {
                        _owner.PushEvent($"Language set to {nextLanguage}");
                    }

                    return;
                }

                var fullscreen = DotArenaMetaProgression.ToggleFullscreen(_owner._metaState);
                _owner.PushEvent(fullscreen ? "Fullscreen enabled" : "Fullscreen disabled");
            }

            public string GetNextOwnedCosmeticId()
            {
                if (_owner._metaState == null || _owner._metaState.OwnedCosmeticIds.Count == 0)
                {
                    return string.Empty;
                }

                var ownedCosmetics = _owner._metaState.OwnedCosmeticIds;
                var currentIndex = ownedCosmetics.IndexOf(_owner._metaState.EquippedCosmeticId);
                if (currentIndex < 0)
                {
                    return ownedCosmetics[0];
                }

                return ownedCosmetics[(currentIndex + 1) % ownedCosmetics.Count];
            }

            public DotArenaSceneUiSnapshot BuildSceneUiSnapshot()
            {
                var settlementSummary = _owner._settlementSummary;
                var previewPreset = DotArenaSinglePlayerCatalog.PeekPreset(_owner._singlePlayerPlaylistIndex);
                var endpoint = Rpc.WebSocketRpcClientFactory.BuildUrl(_owner._host, _owner._port, _owner._path);
                var currentEventMessage = _owner.GetCurrentEventMessage();
                var localPlayerBuffText = _owner.GetLocalPlayerBuffText();
                var inMultiplayerLobby = IsInMultiplayerLobby();

                return new DotArenaSceneUiSnapshot
                {
                    HasSession = _owner.HasActiveSession,
                    FlowState = _owner._flowState,
                    EntryMenuState = _owner._entryMenuState,
                    SessionMode = _owner._sessionMode,
                    Status = _owner._status,
                    LocalPlayerId = _owner._localPlayerId,
                    Account = _owner._account,
                    Password = _owner._password,
                    LocalPlayerScoreText = _owner.GetLocalPlayerScoreText(),
                    LocalWinCount = _owner._localWinCount,
                    LastWorldTick = _owner._lastWorldTick,
                    ViewCount = _owner._views.Count,
                    LocalPlayerBuffText = localPlayerBuffText,
                    DebugPanelVisible = _owner._showDebugPanel,
                    DebugPanelDetail = DotArenaUiTextComposer.BuildDebugPanelDetail(
                        _owner._status,
                        _owner._flowState,
                        _owner._entryMenuState,
                        _owner._sessionMode,
                        _owner._localPlayerId,
                        _owner._lastWorldTick,
                        _owner._views.Count,
                        localPlayerBuffText,
                        currentEventMessage,
                        endpoint,
                        _owner.IsConnected,
                        _owner.IsConnecting),
                    Host = _owner._host,
                    Port = _owner._port,
                    Path = _owner._path,
                    CurrentEventMessage = currentEventMessage,
                    LastRoundRemainingSeconds = _owner._lastRoundRemainingSeconds,
                    MenuLoginStatusText = _owner.GetMenuLoginStatusText(),
                    IsConnecting = _owner.IsConnecting,
                    IsBusy = _owner.IsUiBusy,
                    SettlementTitle = settlementSummary?.Title ?? string.Empty,
                    SettlementDetail = settlementSummary?.Detail ?? string.Empty,
                    SettlementRewardSummary = settlementSummary?.RewardSummary ?? string.Empty,
                    SettlementTaskSummary = settlementSummary?.TaskSummary ?? string.Empty,
                    SettlementNextStepSummary = settlementSummary?.NextStepSummary ?? string.Empty,
                    SettlementPrimaryActionText = settlementSummary == null
                        ? string.Empty
                        : DotArenaUiTextComposer.GetRematchButtonLabel(settlementSummary.SessionMode),
                    MatchmakingTitle = _owner._sessionMode == SessionMode.SinglePlayer
                        ? "Preparing Local Match"
                        : _owner._flowState == FrontendFlowState.Matchmaking
                            ? "正在排队"
                            : "联机大厅",
                    MatchmakingDetail = DotArenaUiTextComposer.BuildMatchmakingDetail(_owner._sessionMode, _owner._currentArenaMapVariant, _owner._currentArenaRuleVariant, _owner._status, currentEventMessage),
                    MetaPlayerSummary = DotArenaUiTextComposer.BuildMetaPlayerSummary(_owner._metaState, inMultiplayerLobby),
                    MetaLobbyHighlights = DotArenaUiTextComposer.BuildMetaLobbyHighlights(_owner._metaState, inMultiplayerLobby, previewPreset),
                    MetaProfileDetail = DotArenaUiTextComposer.BuildMetaProfileDetail(_owner._metaState, inMultiplayerLobby, previewPreset, _owner._lastRewardSummary, endpoint),
                    MetaTasksDetail = DotArenaUiTextComposer.BuildMetaTasksDetail(_owner._metaState),
                    MetaShopDetail = DotArenaUiTextComposer.BuildMetaShopDetail(_owner._metaState),
                    MetaRecordsDetail = DotArenaUiTextComposer.BuildMetaRecordsDetail(_owner._metaState),
                    MetaLeaderboardDetail = DotArenaUiTextComposer.BuildMetaLeaderboardDetail(_owner._metaState),
                    MetaSettingsDetail = DotArenaUiTextComposer.BuildMetaSettingsDetail(_owner._metaState),
                    MetaFooterHint = DotArenaUiTextComposer.BuildMetaFooterHint(inMultiplayerLobby)
                };
            }

            private static DotArenaTaskProgress? FindReadyTask(List<DotArenaTaskProgress> tasks)
            {
                foreach (var task in tasks)
                {
                    if (!task.Claimed && task.Progress >= task.Target)
                    {
                        return task;
                    }
                }

                return null;
            }
        }
    }
}
