#nullable enable

using System;
using System.Threading.Tasks;
using Shared.Interfaces;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        private void HandleInput()
        {
            if (!HasActiveSession || Time.time < _nextInputAt)
            {
                return;
            }

            _nextInputAt = Time.time + InputSendIntervalSeconds;

            var move = ReadMoveVector();
            var dash = _dashQueued;
            _dashQueued = false;
            var inputSummary = $"{move.x:0.00},{move.y:0.00}|dash={dash}";
            if (!string.Equals(_lastLoggedInputVector, inputSummary, StringComparison.Ordinal))
            {
                _lastLoggedInputVector = inputSummary;
                Debug.Log($"[DotArena] HandleInput mode={_sessionMode} move={inputSummary} localMatch={_localMatch != null}");
            }

            if (_sessionMode == SessionMode.SinglePlayer && _localMatch != null)
            {
                _localMatch.SubmitInput(new InputMessage
                {
                    PlayerId = _localPlayerId,
                    MoveX = move.x,
                    MoveY = move.y,
                    Dash = dash,
                    Tick = ++_inputTick
                });
                return;
            }

            if (!IsConnected)
            {
                return;
            }

            _ = SendInputAsync(move, dash);
        }

        private async Task SendInputAsync(Vector2 move, bool dash)
        {
            try
            {
                await NetworkSession.SubmitInputAsync(new InputMessage
                {
                    PlayerId = _localPlayerId,
                    MoveX = move.x,
                    MoveY = move.y,
                    Dash = dash,
                    Tick = ++_inputTick
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _status = $"Input failed: {ex.Message}";
            }
        }

    }
}
