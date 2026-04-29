#nullable enable

using System;
using System.Collections.Generic;
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

            var statusText = BuildMatchmakingStatusText(matchmakingStatus);
            var detailText = BuildMatchmakingStatusDetail(matchmakingStatus);

            switch (matchmakingStatus.State)
            {
                case MatchmakingState.Canceled:
                    if (_pendingUiRequest == PendingUiRequest.CancelMatchmaking)
                    {
                        _pendingUiRequest = PendingUiRequest.None;
                    }
                    _flowState = FrontendFlowState.Entry;
                    _entryMenuState = EntryMenuState.MultiplayerLobby;
                    _status = statusText;
                    _eventMessage = string.IsNullOrWhiteSpace(detailText)
                        ? "已返回联机大厅"
                        : detailText;
                    return;
                case MatchmakingState.Failed:
                    if (_pendingUiRequest == PendingUiRequest.CancelMatchmaking)
                    {
                        _pendingUiRequest = PendingUiRequest.None;
                    }
                    _flowState = FrontendFlowState.Entry;
                    _entryMenuState = EntryMenuState.MultiplayerLobby;
                    _status = statusText;
                    _eventMessage = string.IsNullOrWhiteSpace(detailText)
                        ? "请重新开始匹配"
                        : detailText;
                    return;
                case MatchmakingState.Matched:
                    if (_pendingUiRequest == PendingUiRequest.CancelMatchmaking)
                    {
                        _pendingUiRequest = PendingUiRequest.None;
                    }
                    _flowState = FrontendFlowState.Matchmaking;
                    _entryMenuState = EntryMenuState.Hidden;
                    _status = statusText;
                    _eventMessage = string.IsNullOrWhiteSpace(detailText)
                        ? "房间已就绪，等待世界状态"
                        : detailText;
                    return;
                default:
                    if (_pendingUiRequest == PendingUiRequest.CancelMatchmaking &&
                        matchmakingStatus.State is MatchmakingState.Queued or MatchmakingState.Searching)
                    {
                        _status = "正在取消匹配";
                        _eventMessage = "等待服务器确认取消";
                        return;
                    }

                    _flowState = FrontendFlowState.Matchmaking;
                    _entryMenuState = EntryMenuState.Hidden;
                    _status = statusText;
                    _eventMessage = string.IsNullOrWhiteSpace(detailText)
                        ? statusText
                        : detailText;
                    return;
            }
        }

        private static string BuildMatchmakingStatusText(MatchmakingStatusUpdate matchmakingStatus)
        {
            if (!string.IsNullOrWhiteSpace(matchmakingStatus.Message))
            {
                return matchmakingStatus.Message;
            }

            return matchmakingStatus.State switch
            {
                MatchmakingState.Queued when matchmakingStatus.QueueSize > 0 => $"排队中 {matchmakingStatus.QueuePosition}/{matchmakingStatus.QueueSize}",
                MatchmakingState.Queued => "排队中",
                MatchmakingState.Searching when matchmakingStatus.MatchedPlayerCount > 0 && matchmakingStatus.RoomCapacity > 0 => $"匹配中 {matchmakingStatus.MatchedPlayerCount}/{matchmakingStatus.RoomCapacity}",
                MatchmakingState.Searching => "匹配中",
                MatchmakingState.Matched when !string.IsNullOrWhiteSpace(matchmakingStatus.RoomId) => $"已进入房间 {matchmakingStatus.RoomId}",
                MatchmakingState.Matched => "已进入房间",
                MatchmakingState.Canceled => "已取消匹配",
                MatchmakingState.Failed => "匹配失败",
                _ => "等待匹配"
            };
        }

        private static string BuildMatchmakingStatusDetail(MatchmakingStatusUpdate matchmakingStatus)
        {
            var details = new List<string>();

            if (matchmakingStatus.QueuePosition > 0 && matchmakingStatus.QueueSize > 0)
            {
                details.Add($"Queue {matchmakingStatus.QueuePosition}/{matchmakingStatus.QueueSize}");
            }

            if (matchmakingStatus.MatchedPlayerCount > 0 && matchmakingStatus.RoomCapacity > 0)
            {
                details.Add($"Room {matchmakingStatus.MatchedPlayerCount}/{matchmakingStatus.RoomCapacity}");
            }

            if (!string.IsNullOrWhiteSpace(matchmakingStatus.RoomId))
            {
                details.Add($"Room {matchmakingStatus.RoomId}");
            }

            if (!string.IsNullOrWhiteSpace(matchmakingStatus.Message))
            {
                details.Add(matchmakingStatus.Message);
            }

            return details.Count == 0 ? string.Empty : string.Join("  |  ", details);
        }
    }
}
