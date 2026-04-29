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
    }
}
