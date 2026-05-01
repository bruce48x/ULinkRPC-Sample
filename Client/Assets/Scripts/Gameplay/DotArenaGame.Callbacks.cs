#nullable enable

using System;
using Shared.Gameplay;
using Shared.Interfaces;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        public void OnWorldState(WorldState worldState)
        {
            _callbackInbox.EnqueueWorldState(worldState);
        }

        public void OnPlayerDead(PlayerDead deadEvent)
        {
            _callbackInbox.EnqueuePlayerDead(deadEvent);
        }

        public void OnMatchEnd(MatchEnd matchEnd)
        {
            _callbackInbox.EnqueueMatchEnd(matchEnd);
        }

        public void OnMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus)
        {
            _callbackInbox.EnqueueMatchmakingStatus(matchmakingStatus);
        }

        private void CaptureInputIntent()
        {
            if (DotArenaInputUtility.IsKeyDown(KeyCode.P))
            {
                _showDebugPanel = !_showDebugPanel;
            }
        }

        private void ApplyPendingCallbacks()
        {
            var pending = _callbackInbox.Drain();

            if (pending.DisconnectedMessage != null)
            {
                HandleDisconnectedOnMainThread(pending.DisconnectedMessage);
                return;
            }

            if (pending.WorldState != null)
            {
                ApplyWorldState(pending.WorldState);
            }

            foreach (var deadEvent in pending.Deaths)
            {
                HandleDeadEvent(deadEvent);
            }

            if (pending.MatchEnd != null)
            {
                HandleMatchEnd(pending.MatchEnd);
            }

            if (pending.MatchmakingStatus != null)
            {
                HandleMatchmakingStatus(pending.MatchmakingStatus);
            }
        }

        private void HandleDisconnectedOnMainThread(string? disconnectMessage)
        {
            if (_sessionMode == SessionMode.SinglePlayer)
            {
                Debug.LogWarning($"[DotArena] Ignored remote disconnect while running single-player: {disconnectMessage ?? "Disconnected"}");
                return;
            }

            ResetToModeSelect(
                status: string.IsNullOrWhiteSpace(disconnectMessage) ? "Disconnected" : $"Disconnected: {disconnectMessage}",
                eventMessage: "联机连接已断开",
                toastMessage: null);
            Debug.LogWarning($"[DotArena] {_status}");
        }

        private void ApplyWorldState(WorldState worldState)
        {
            WorldSynchronizer.ApplyWorldState(
                worldState,
                _localPlayerId,
                ref _lastWorldTick,
                ref _lastRoundRemainingSeconds,
                ref _lastLoggedPlayerCount,
                ref _currentArenaHalfExtents);

            if (_sessionMode != SessionMode.None &&
                _flowState != FrontendFlowState.Settlement &&
                worldState.Players.Count > 0)
            {
                _flowState = FrontendFlowState.InMatch;
                _entryMenuState = EntryMenuState.Hidden;
                _status = _sessionMode == SessionMode.SinglePlayer
                    ? $"Single-player Match: {_localPlayerId}"
                    : $"In Match: {_localPlayerId}";
            }
        }

        private void HandleDeadEvent(PlayerDead deadEvent)
        {
            if (_renderStates.TryGetValue(deadEvent.PlayerId, out var renderState))
            {
                renderState.Alive = false;
                renderState.State = PlayerLifeState.Dead;
            }

            if (_views.TryGetValue(deadEvent.PlayerId, out var view))
            {
                var radius = renderState?.Radius ?? GameplayConfig.PlayerVisualRadius;
                view.ApplyPresentation(DotArenaPresentation.ResolvePlayerColor(deadEvent.PlayerId), PlayerLifeState.Dead, false, radius);
            }

            PushEvent(deadEvent.PlayerId == _localPlayerId
                ? "你被吞噬了"
                : $"{deadEvent.PlayerId} 被吞噬");
        }

        private void HandleMatchEnd(MatchEnd matchEnd)
        {
            if (_sessionMode == SessionMode.Multiplayer &&
                string.Equals(matchEnd.WinnerPlayerId, _localPlayerId, StringComparison.Ordinal))
            {
                _localWinCount += 1;
            }

            PushEvent(matchEnd.WinnerPlayerId == _localPlayerId
                ? "本局胜利"
                : $"胜者: {matchEnd.WinnerPlayerId}");

            _ = ReturnToMainMenuAfterMatchAsync(
                _sessionMode == SessionMode.Multiplayer,
                matchEnd.WinnerPlayerId,
                string.Equals(matchEnd.WinnerPlayerId, _localPlayerId, StringComparison.Ordinal));
        }

        private void HandleMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus)
        {
            _sessionMode = SessionMode.Multiplayer;
            _localPlayerId = string.IsNullOrWhiteSpace(_authenticatedPlayerId) ? _localPlayerId : _authenticatedPlayerId;

            if (matchmakingStatus.State == MatchmakingState.Matched &&
                matchmakingStatus.RealtimeConnection is { Transport: RealtimeTransportKind.Kcp } realtimeConnection)
            {
                _status = "房间已就绪，正在连接 KCP";
                _eventMessage = $"正在建立实时连接 {realtimeConnection.Host}:{realtimeConnection.Port}";
                _ = EnsureRealtimeSessionAsync(realtimeConnection);
            }

            var viewState = DotArenaMultiplayerFlow.BuildMatchmakingViewState(
                matchmakingStatus,
                _pendingUiRequest == PendingUiRequest.CancelMatchmaking);

            if (viewState.ClearPendingCancelRequest)
            {
                _pendingUiRequest = PendingUiRequest.None;
            }

            _flowState = viewState.FlowState;
            _entryMenuState = viewState.EntryMenuState;
            _status = viewState.Status;
            _eventMessage = viewState.EventMessage;
        }

        private async System.Threading.Tasks.Task EnsureRealtimeSessionAsync(RealtimeConnectionInfo realtimeConnection)
        {
            try
            {
                var connected = await NetworkSession
                    .EnsureRealtimeConnectedAsync(realtimeConnection, this, _cts.Token)
                    .ConfigureAwait(false);

                if (!connected)
                {
                    _callbackInbox.EnqueueDisconnected("KCP realtime attach failed");
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DotArena] Realtime connect failed: {ex}");
                _callbackInbox.EnqueueDisconnected(ex.Message);
            }
        }
    }
}
