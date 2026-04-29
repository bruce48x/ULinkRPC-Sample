#nullable enable

using System;
using System.Threading.Tasks;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        private async Task ConnectAsync(bool enterMultiplayerLobbyAfterLogin)
        {
            if (IsConnecting || IsConnected || _sessionMode == SessionMode.SinglePlayer)
            {
                return;
            }

            _flowState = FrontendFlowState.Matchmaking;
            _entryMenuState = enterMultiplayerLobbyAfterLogin ? EntryMenuState.MultiplayerAuth : EntryMenuState.Hidden;
            _status = $"正在连接 {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}";

            try
            {
                var reply = await NetworkSession.ConnectAndLoginAsync(_host, _port, _path, _account, _password, this, _cts.Token);

                if (reply.Code != 0)
                {
                    _status = $"Login failed, code={reply.Code}";
                    return;
                }

                _localPlayerId = string.IsNullOrWhiteSpace(reply.PlayerId) ? _account : reply.PlayerId;
                EnsureMetaState(_localPlayerId);
                _localWinCount = Math.Max(0, reply.WinCount);
                _hasAuthenticatedProfile = true;
                _authenticatedPlayerId = _localPlayerId;
                _sessionMode = SessionMode.Multiplayer;
                _flowState = enterMultiplayerLobbyAfterLogin ? FrontendFlowState.Entry : FrontendFlowState.Matchmaking;
                _entryMenuState = enterMultiplayerLobbyAfterLogin ? EntryMenuState.MultiplayerLobby : EntryMenuState.Hidden;
                _status = enterMultiplayerLobbyAfterLogin ? $"联机大厅: {_localPlayerId}" : $"Matchmaking: {_localPlayerId}";
                Debug.Log($"[DotArena] Connected as {_localPlayerId} -> {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}");
                PushEvent(enterMultiplayerLobbyAfterLogin ? "登录成功，可在联机大厅开始匹配" : "等待其他玩家加入");
            }
            catch (OperationCanceledException)
            {
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = enterMultiplayerLobbyAfterLogin ? EntryMenuState.MultiplayerAuth : EntryMenuState.MultiplayerLobby;
                _status = "Connection canceled";
            }
            catch (Exception ex)
            {
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = enterMultiplayerLobbyAfterLogin ? EntryMenuState.MultiplayerAuth : EntryMenuState.MultiplayerLobby;
                _status = $"Connect failed: {ex.Message}";
                Debug.LogError($"[DotArena] Connect failed: {ex}");
                await DisposeConnectionAsync();
                if (!enterMultiplayerLobbyAfterLogin && _hasAuthenticatedProfile && !string.IsNullOrWhiteSpace(_authenticatedPlayerId))
                {
                    _sessionMode = SessionMode.Multiplayer;
                    _localPlayerId = _authenticatedPlayerId;
                    _eventMessage = "联机匹配失败，已返回联机大厅";
                }
            }
        }

        private void OnDisconnected(Exception? ex)
        {
            if (_ignoreDisconnectCallback)
            {
                return;
            }

            if (_sessionMode == SessionMode.SinglePlayer)
            {
                Debug.LogWarning($"[DotArena] Ignored remote disconnect while running single-player: {ex?.Message ?? "Disconnected"}");
                return;
            }

            ResetSessionPresentation();
            _localPlayerId = string.Empty;
            _sessionMode = SessionMode.None;
            _flowState = FrontendFlowState.Entry;
            _localMatch = null;
            _entryMenuState = EntryMenuState.ModeSelect;
            _hasAuthenticatedProfile = false;
            _authenticatedPlayerId = string.Empty;
            _settlementSummary = null;
            _localWinCount = 0;
            _status = ex == null ? "Disconnected" : $"Disconnected: {ex.Message}";
            Debug.LogWarning($"[DotArena] {_status}");
        }

        private Task ReturnToMainMenuAfterMatchAsync(bool preserveLoginState)
        {
            return ReturnToMainMenuAfterMatchAsync(preserveLoginState, _localPlayerId, true);
        }

        private async Task ReturnToMainMenuAfterMatchAsync(bool preserveLoginState, string winnerPlayerId, bool localPlayerWon)
        {
            var sessionMode = _sessionMode;
            var localScore = GetLocalPlayerScoreValue();
            var authenticatedPlayerId = _authenticatedPlayerId;
            var localWinCount = _localWinCount;

            if (_sessionMode == SessionMode.Multiplayer)
            {
                await DisposeConnectionAsync().ConfigureAwait(false);
            }
            else
            {
                ResetSessionPresentation();
                _sessionMode = SessionMode.None;
                _localMatch = null;
                _localPlayerId = string.Empty;
            }

            _flowState = FrontendFlowState.Settlement;
            _entryMenuState = EntryMenuState.Hidden;
            _status = "Match finished";
            _eventMessage = "Review results, then rematch or return to the lobby.";

            if (preserveLoginState)
            {
                _hasAuthenticatedProfile = true;
                _authenticatedPlayerId = authenticatedPlayerId;
                _localWinCount = localWinCount;
                _sessionMode = SessionMode.Multiplayer;
                _localPlayerId = authenticatedPlayerId;
            }
            else
            {
                _hasAuthenticatedProfile = false;
                _authenticatedPlayerId = string.Empty;
                _localWinCount = 0;
                _sessionMode = SessionMode.None;
                _localPlayerId = string.Empty;
            }

            _settlementSummary = new MatchSettlementSummary
            {
                Title = preserveLoginState ? "Multiplayer Results" : "Single-player Results",
                Detail = DotArenaUiTextComposer.BuildSettlementDetail(sessionMode, localScore, _localWinCount, winnerPlayerId, localPlayerWon, _currentArenaMapVariant, _currentArenaRuleVariant),
                RewardSummary = DotArenaUiTextComposer.BuildSettlementRewardSummary(sessionMode, _lastRewardSummary),
                TaskSummary = DotArenaUiTextComposer.BuildSettlementTaskSummary(_metaState),
                NextStepSummary = DotArenaUiTextComposer.BuildSettlementNextStepSummary(sessionMode, _currentArenaMapVariant, _currentArenaRuleVariant),
                WinnerPlayerId = winnerPlayerId,
                LocalPlayerScore = localScore,
                LocalWinCount = _localWinCount,
                LocalPlayerWon = localPlayerWon,
                SessionMode = sessionMode
            };

            if (_metaState != null)
            {
                _lastRewardSummary = DotArenaMetaProgression.ApplyMatchResult(
                    _metaState,
                    sessionMode,
                    winnerPlayerId,
                    preserveLoginState ? authenticatedPlayerId : "Player",
                    localScore);
                _settlementSummary.RewardSummary = DotArenaUiTextComposer.BuildSettlementRewardSummary(sessionMode, _lastRewardSummary);
                _settlementSummary.TaskSummary = DotArenaUiTextComposer.BuildSettlementTaskSummary(_metaState);
            }
            else
            {
                _lastRewardSummary = null;
            }
        }

        private void BeginShutdown()
        {
            if (_shutdownStarted)
            {
                return;
            }

            _shutdownStarted = true;
            _cts.Cancel();
            _ = DisposeConnectionAsync();
        }

        private async Task DisposeConnectionAsync(bool clearSessionState = true)
        {
            _ignoreDisconnectCallback = true;
            try
            {
                await NetworkSession.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _ignoreDisconnectCallback = false;
            }

            if (clearSessionState)
            {
                _sessionMode = SessionMode.None;
                _localPlayerId = string.Empty;
                _localMatch = null;
            }
        }

        private async Task CancelMatchmakingAsync()
        {
            if (_flowState != FrontendFlowState.Matchmaking)
            {
                return;
            }

            var preserveLoginState = _sessionMode == SessionMode.Multiplayer && _hasAuthenticatedProfile;
            var authenticatedPlayerId = _authenticatedPlayerId;
            var localWinCount = _localWinCount;

            if (_sessionMode == SessionMode.Multiplayer)
            {
                await DisposeConnectionAsync().ConfigureAwait(false);
            }
            else
            {
                ResetSessionPresentation();
                _sessionMode = SessionMode.None;
                _localMatch = null;
                _localPlayerId = string.Empty;
            }

            _flowState = FrontendFlowState.Entry;
            if (preserveLoginState)
            {
                _hasAuthenticatedProfile = true;
                _authenticatedPlayerId = authenticatedPlayerId;
                _localWinCount = localWinCount;
                _sessionMode = SessionMode.Multiplayer;
                _localPlayerId = authenticatedPlayerId;
                _entryMenuState = EntryMenuState.MultiplayerLobby;
                _status = $"联机大厅: {authenticatedPlayerId}";
                _eventMessage = "已返回联机大厅";
            }
            else
            {
                _hasAuthenticatedProfile = false;
                _authenticatedPlayerId = string.Empty;
                _localWinCount = 0;
                _sessionMode = SessionMode.None;
                _localPlayerId = string.Empty;
                _entryMenuState = EntryMenuState.ModeSelect;
                _status = "Choose a mode";
                _eventMessage = "Select single-player or multiplayer.";
            }
        }

        private void ProcessMenuRequests()
        {
            if (IsConnecting)
            {
                return;
            }

            if (_flowState == FrontendFlowState.Settlement)
            {
                if (_returnToLobbyRequested)
                {
                    _returnToLobbyRequested = false;
                    _rematchRequested = false;
                    ReturnToEntryMenuFromSettlement();
                }

                if (_rematchRequested)
                {
                    _rematchRequested = false;
                    var sessionMode = _settlementSummary?.SessionMode ?? SessionMode.SinglePlayer;
                    _settlementSummary = null;
                    _flowState = FrontendFlowState.Entry;

                    if (sessionMode == SessionMode.SinglePlayer)
                    {
                        BeginSinglePlayerMatch();
                    }
                    else
                    {
                        BeginMultiplayerMatchmaking();
                    }
                }

                return;
            }

            if (HasActiveSession || !_singlePlayerStartRequested)
            {
                return;
            }

            _singlePlayerStartRequested = false;
            BeginSinglePlayerMatch();
        }

        private void BeginMultiplayerMatchmaking()
        {
            if (!_hasAuthenticatedProfile || string.IsNullOrWhiteSpace(_authenticatedPlayerId))
            {
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerAuth;
                _status = "请先登录联机账号";
                _eventMessage = "输入账号密码后进入联机大厅";
                return;
            }

            _sessionMode = SessionMode.Multiplayer;
            _localPlayerId = _authenticatedPlayerId;
            _flowState = FrontendFlowState.Matchmaking;
            _entryMenuState = EntryMenuState.Hidden;
            _status = $"Matchmaking: {_localPlayerId}";
            _eventMessage = "等待其他玩家加入";
            _settlementSummary = null;

            if (IsConnected)
            {
                return;
            }

            _ = ConnectAsync(enterMultiplayerLobbyAfterLogin: false);
        }

        private void ConfigureWindow()
        {
#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
            Screen.SetResolution(WindowWidth, WindowHeight, FullScreenMode.Windowed);
#endif
        }

        private void InitializeConnectionMode()
        {
            _flowState = FrontendFlowState.Entry;
            _entryMenuState = EntryMenuState.ModeSelect;
            _status = "选择模式";
            _eventMessage = "请选择单机或联机";
        }

        private string GetMenuLoginStatusText()
        {
            return DotArenaUiTextComposer.BuildMenuLoginStatusText(_hasAuthenticatedProfile, _authenticatedPlayerId, _localWinCount);
        }

        private void ApplyLaunchOverrides()
        {
            var launchArguments = Rpc.RpcLaunchArguments.ReadCurrentProcess();
            launchArguments.ApplyTo(ref _host, ref _port, ref _path);
            launchArguments.ApplyCredentials(ref _account, ref _password);

            if (launchArguments.HasOverrides)
            {
                Debug.Log($"[LaunchArgs] DotArenaGame host={_host}, port={_port}, path={_path}, account={_account}");
            }
        }

        private void ReturnToEntryMenuFromSettlement()
        {
            var preserveLoginState = _settlementSummary?.SessionMode == SessionMode.Multiplayer;
            var authenticatedPlayerId = _authenticatedPlayerId;
            var localWinCount = _localWinCount;

            _settlementSummary = null;
            _flowState = FrontendFlowState.Entry;

            if (preserveLoginState)
            {
                _hasAuthenticatedProfile = true;
                _authenticatedPlayerId = authenticatedPlayerId;
                _localWinCount = localWinCount;
                _sessionMode = SessionMode.Multiplayer;
                _localPlayerId = authenticatedPlayerId;
                _entryMenuState = EntryMenuState.MultiplayerLobby;
                _status = $"联机大厅: {authenticatedPlayerId}";
                _eventMessage = "已返回联机大厅";
                return;
            }

            _hasAuthenticatedProfile = false;
            _authenticatedPlayerId = string.Empty;
            _localWinCount = 0;
            _sessionMode = SessionMode.None;
            _localPlayerId = string.Empty;
            _entryMenuState = EntryMenuState.ModeSelect;
            _status = "Choose a mode";
            _eventMessage = "Select single-player or multiplayer.";
        }
    }
}
