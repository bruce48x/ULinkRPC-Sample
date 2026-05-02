#nullable enable

using System;
using System.Threading.Tasks;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        private async Task ConnectAsync()
        {
            if (IsConnecting || IsConnected || _sessionMode == SessionMode.SinglePlayer)
            {
                _pendingUiRequest = PendingUiRequest.None;
                return;
            }

            _flowState = FrontendFlowState.Entry;
            _entryMenuState = EntryMenuState.MultiplayerAuth;
            _status = $"正在连接 {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}";
            _eventMessage = "正在登录联机账号";

            try
            {
                var reply = await NetworkSession.ConnectAndLoginAsync(_host, _port, _path, _account, _password, guestLogin: false, this, _cts.Token);

                if (reply.Code != 0)
                {
                    _hasAuthenticatedProfile = false;
                    _authenticatedPlayerId = string.Empty;
                    _localWinCount = 0;
                    _localPlayerId = string.Empty;
                    _localMatch = null;
                    _sessionMode = SessionMode.None;
                    _status = $"登录失败, code={reply.Code}";
                    _eventMessage = "登录失败，请检查账号或密码";
                    return;
                }

                _localPlayerId = string.IsNullOrWhiteSpace(reply.PlayerId) ? _account : reply.PlayerId;
                _localMatch = null;
                EnsureMetaState(_localPlayerId);
                _localWinCount = Math.Max(0, reply.WinCount);
                _hasAuthenticatedProfile = true;
                _authenticatedPlayerId = _localPlayerId;
                _sessionMode = SessionMode.Multiplayer;
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerLobby;
                _status = $"联机大厅: {_localPlayerId}";
                _eventMessage = "登录成功，可点击 Start Match 进入排队";
                Debug.Log($"[DotArena] Connected as {_localPlayerId} -> {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}");
                PushEvent("登录成功，可在联机大厅开始匹配");
            }
            catch (OperationCanceledException)
            {
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerAuth;
                _status = "Connection canceled";
                _eventMessage = "登录已取消";
            }
            catch (Exception ex)
            {
                var feedback = DotArenaMultiplayerFlow.BuildConnectionFailure(ex);
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerAuth;
                _status = feedback.Status;
                _eventMessage = feedback.Message;
                Debug.LogError($"[DotArena] Connect failed: {ex}");
                await DisposeConnectionAsync();
                _localMatch = null;
            }
            finally
            {
                if (_pendingUiRequest == PendingUiRequest.Login)
                {
                    _pendingUiRequest = PendingUiRequest.None;
                }
            }
        }

        private async Task ConnectAsGuestAsync()
        {
            if (IsConnecting || IsConnected || _sessionMode == SessionMode.SinglePlayer)
            {
                _pendingUiRequest = PendingUiRequest.None;
                return;
            }

            _flowState = FrontendFlowState.Entry;
            _entryMenuState = EntryMenuState.MultiplayerAuth;
            _status = $"正在连接 {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}";
            _eventMessage = "正在申请游客账号";

            try
            {
                var reply = await NetworkSession.ConnectAndLoginAsync(_host, _port, _path, string.Empty, string.Empty, guestLogin: true, this, _cts.Token);

                if (reply.Code != 0)
                {
                    _hasAuthenticatedProfile = false;
                    _authenticatedPlayerId = string.Empty;
                    _localWinCount = 0;
                    _localPlayerId = string.Empty;
                    _localMatch = null;
                    _sessionMode = SessionMode.None;
                    _status = $"游客登录失败, code={reply.Code}";
                    _eventMessage = "无法申请游客账号，请稍后重试";
                    return;
                }

                _account = string.IsNullOrWhiteSpace(reply.Account) ? reply.PlayerId : reply.Account;
                _password = reply.Password;
                _localPlayerId = string.IsNullOrWhiteSpace(reply.PlayerId) ? _account : reply.PlayerId;
                _localMatch = null;
                EnsureMetaState(_localPlayerId);
                _localWinCount = Math.Max(0, reply.WinCount);
                _hasAuthenticatedProfile = true;
                _authenticatedPlayerId = _localPlayerId;
                _sessionMode = SessionMode.Multiplayer;
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerLobby;
                _status = $"联机大厅: {_localPlayerId}";
                _eventMessage = "游客登录成功，可点击 Start Match 进入排队";
                Debug.Log($"[DotArena] Connected as guest {_localPlayerId} -> {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}");
                PushEvent("游客登录成功，可在联机大厅开始匹配");
            }
            catch (OperationCanceledException)
            {
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerAuth;
                _status = "Connection canceled";
                _eventMessage = "游客登录已取消";
            }
            catch (Exception ex)
            {
                var feedback = DotArenaMultiplayerFlow.BuildConnectionFailure(ex);
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerAuth;
                _status = feedback.Status;
                _eventMessage = feedback.Message;
                Debug.LogError($"[DotArena] Guest connect failed: {ex}");
                await DisposeConnectionAsync();
                _localMatch = null;
            }
            finally
            {
                if (_pendingUiRequest == PendingUiRequest.Login)
                {
                    _pendingUiRequest = PendingUiRequest.None;
                }
            }
        }

        private void OnDisconnected(Exception? ex)
        {
            if (_ignoreDisconnectCallback)
            {
                return;
            }

            _callbackInbox.EnqueueDisconnected(ex?.Message);
        }

        private Task ReturnToMainMenuAfterMatchAsync(bool preserveLoginState)
        {
            return ReturnToMainMenuAfterMatchAsync(preserveLoginState, _localPlayerId, true);
        }

        private Task ReturnToMainMenuAfterMatchAsync(bool preserveLoginState, string winnerPlayerId, bool localPlayerWon)
        {
            var sessionMode = _sessionMode;
            var localScore = GetLocalPlayerScoreValue();
            var authenticatedPlayerId = _authenticatedPlayerId;
            var localWinCount = _localWinCount;

            if (_sessionMode == SessionMode.Multiplayer)
            {
                _ = NetworkSession.DisposeRealtimeAsync();
            }

            if (_sessionMode != SessionMode.Multiplayer)
            {
                ResetSessionPresentation();
                _sessionMode = SessionMode.None;
                _localMatch = null;
                _localPlayerId = string.Empty;
            }
            else
            {
                ResetSessionPresentation();
            }

            _localMatch = null;
            _flowState = FrontendFlowState.Settlement;
            _entryMenuState = EntryMenuState.Hidden;
            _status = "对局结束";
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

            return Task.CompletedTask;
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

        private async Task DisposeConnectionAsync(bool clearSessionState = true, bool logout = true)
        {
            _ignoreDisconnectCallback = true;
            try
            {
                await NetworkSession.DisposeAsync(logout).ConfigureAwait(false);
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
                if (_pendingUiRequest == PendingUiRequest.CancelMatchmaking)
                {
                    _pendingUiRequest = PendingUiRequest.None;
                }
                return;
            }

            var preserveLoginState = _sessionMode == SessionMode.Multiplayer && _hasAuthenticatedProfile;
            var authenticatedPlayerId = _authenticatedPlayerId;
            var localWinCount = _localWinCount;

            if (_sessionMode == SessionMode.Multiplayer)
            {
                try
                {
                    await NetworkSession.DisposeRealtimeAsync().ConfigureAwait(false);
                    await NetworkSession.CancelMatchmakingAsync(_cts.Token).ConfigureAwait(false);
                    _status = "正在取消匹配";
                    _eventMessage = "等待服务器确认取消";
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DotArena] Cancel matchmaking failed: {ex.Message}");
                    _pendingUiRequest = PendingUiRequest.None;
                    _flowState = FrontendFlowState.Matchmaking;
                    _entryMenuState = EntryMenuState.Hidden;
                    _status = $"Cancel matchmaking failed: {ex.Message}";
                    _eventMessage = "取消匹配失败，请重试";
                }

                return;
            }
            else
            {
                ResetSessionPresentation();
                _sessionMode = SessionMode.None;
                _localMatch = null;
                _localPlayerId = string.Empty;
            }

            _localMatch = null;
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
                        _requestedSinglePlayerMode = _currentSinglePlayerMode;
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

            _ = BeginMultiplayerMatchmakingAsync();
        }

        private async Task BeginMultiplayerMatchmakingAsync()
        {
            if (!IsConnected)
            {
                await ConnectAsync().ConfigureAwait(false);
            }

            if (!IsConnected)
            {
                return;
            }

            _sessionMode = SessionMode.Multiplayer;
            _localPlayerId = _authenticatedPlayerId;
            _localMatch = null;
            _flowState = FrontendFlowState.Matchmaking;
            _entryMenuState = EntryMenuState.Hidden;
            _status = $"排队中: {_localPlayerId}";
            _eventMessage = "正在请求服务器分房";
            _settlementSummary = null;

            try
            {
                await NetworkSession.DisposeRealtimeAsync().ConfigureAwait(false);
                await NetworkSession.StartMatchmakingAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DotArena] Start matchmaking failed: {ex.Message}");
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerLobby;
                _status = $"Start Match failed: {ex.Message}";
                _eventMessage = "无法开始匹配";
            }
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
                _localMatch = null;
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
            _localMatch = null;
            _requestedSinglePlayerMode = SinglePlayerMode.Normal;
            _currentSinglePlayerMode = SinglePlayerMode.Normal;
            _entryMenuState = EntryMenuState.ModeSelect;
            _status = "Choose a mode";
            _eventMessage = "Select single-player or multiplayer.";
        }

        private void ResetToModeSelect(string status, string eventMessage, string? toastMessage)
        {
            _ = NetworkSession.DisposeRealtimeAsync();
            ResetSessionPresentation();
            _callbackInbox.Clear();
            _settlementSummary = null;
            _lastRewardSummary = null;
            _pendingUiRequest = PendingUiRequest.None;
            _flowState = FrontendFlowState.Entry;
            _entryMenuState = EntryMenuState.ModeSelect;
            _sessionMode = SessionMode.None;
            _hasAuthenticatedProfile = false;
            _authenticatedPlayerId = string.Empty;
            _localPlayerId = string.Empty;
            _localWinCount = 0;
            _localMatch = null;
            _requestedSinglePlayerMode = SinglePlayerMode.Normal;
            _currentSinglePlayerMode = SinglePlayerMode.Normal;
            _metaState = null;
            _status = status;
            _eventMessage = eventMessage;
            if (!string.IsNullOrWhiteSpace(toastMessage))
            {
                PushEvent(toastMessage!);
            }
        }
    }
}
