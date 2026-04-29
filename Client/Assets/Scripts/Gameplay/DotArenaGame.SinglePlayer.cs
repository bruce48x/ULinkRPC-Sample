#nullable enable

using Shared.Gameplay;
using Shared.Interfaces;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        private void BeginSinglePlayerMatch()
        {
            var preset = DotArenaSinglePlayerCatalog.GetNextPreset(ref _singlePlayerPlaylistIndex);
            _settlementSummary = null;
            ResetSessionPresentation();
            _ = DisposeConnectionAsync(clearSessionState: false);
            _sessionMode = SessionMode.SinglePlayer;
            _flowState = FrontendFlowState.Matchmaking;
            _localPlayerId = "Player";
            EnsureMetaState(_localPlayerId);
            _currentArenaMapVariant = preset.MapVariant;
            _currentArenaRuleVariant = preset.RuleVariant;
            _localMatch = new ArenaSimulation(DotArenaSinglePlayerCatalog.CreateOptions(preset));
            _localMatch.UpsertPlayer(new ArenaPlayerRegistration
            {
                PlayerId = _localPlayerId,
                Score = 1
            });
            _localWinCount = 0;
            _entryMenuState = EntryMenuState.Hidden;
            _status = $"Single-player | {DotArenaSinglePlayerCatalog.GetRuleVariantName(_currentArenaRuleVariant)}";
            _eventMessage = $"Loading {DotArenaSinglePlayerCatalog.GetMapVariantName(_currentArenaMapVariant)}";
            _lastWorldTick = -1;
            _inputTick = 0;
            _singlePlayerTickAccumulator = 0f;
            Debug.Log("[DotArena] BeginSinglePlayerMatch");
            ApplyWorldState(_localMatch.CreateWorldState());
            PushEvent($"Preset: {DotArenaSinglePlayerCatalog.GetPresetLabel(_currentArenaMapVariant, _currentArenaRuleVariant)}", 4f);
            _status = $"Single-player: {_localPlayerId}";
        }

        private void TickLocalMatch()
        {
            if (_sessionMode != SessionMode.SinglePlayer || _localMatch == null)
            {
                return;
            }

            _singlePlayerTickAccumulator += Mathf.Min(Time.deltaTime, SinglePlayerTickSeconds * MaxSinglePlayerCatchUpTicks);

            var catchUpTicks = 0;
            while (_singlePlayerTickAccumulator >= SinglePlayerTickSeconds && catchUpTicks < MaxSinglePlayerCatchUpTicks)
            {
                _singlePlayerTickAccumulator -= SinglePlayerTickSeconds;
                catchUpTicks++;

                var step = _localMatch.Tick(SinglePlayerTickSeconds);
                ApplyWorldState(step.WorldState);

                foreach (var deadEvent in step.Deaths)
                {
                    HandleDeadEvent(deadEvent);
                }

                if (step.MatchEnd != null)
                {
                    HandleMatchEnd(step.MatchEnd);
                    _singlePlayerTickAccumulator = 0f;
                    break;
                }
            }

            if (catchUpTicks == MaxSinglePlayerCatchUpTicks && _singlePlayerTickAccumulator > SinglePlayerTickSeconds)
            {
                _singlePlayerTickAccumulator = 0f;
            }
        }

        private Vector2 ReadMoveVector()
        {
#if UNITY_EDITOR
            if (_hasEditorInputOverride)
            {
                return _editorMoveOverride.sqrMagnitude > 1f ? _editorMoveOverride.normalized : _editorMoveOverride;
            }
#endif

            var x = 0f;
            var y = 0f;

            if (DotArenaInputUtility.IsKeyPressed(KeyCode.A)) x -= 1f;
            if (DotArenaInputUtility.IsKeyPressed(KeyCode.D)) x += 1f;
            if (DotArenaInputUtility.IsKeyPressed(KeyCode.S)) y -= 1f;
            if (DotArenaInputUtility.IsKeyPressed(KeyCode.W)) y += 1f;

            var move = new Vector2(x, y);
            return move.sqrMagnitude > 1f ? move.normalized : move;
        }

#if UNITY_EDITOR
        private bool ConsumeEditorDashOverride()
        {
            if (!_editorDashOverride)
            {
                return false;
            }

            _editorDashOverride = false;
            return true;
        }
#endif
    }
}
