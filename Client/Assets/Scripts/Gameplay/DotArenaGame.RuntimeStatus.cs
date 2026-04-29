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
        private string GetLocalPlayerScoreText()
        {
            if (_localPlayerId.Length == 0)
            {
                return "0";
            }

            return _renderStates.TryGetValue(_localPlayerId, out var renderState)
                ? $"{DotArenaPresentation.FormatScore(renderState.Score)} / {DotArenaPresentation.FormatMass(renderState.Mass)}"
                : "0";
        }

        private int GetLocalPlayerScoreValue()
        {
            if (_localPlayerId.Length == 0)
            {
                return 0;
            }

            return _renderStates.TryGetValue(_localPlayerId, out var renderState) ? renderState.Score : 0;
        }

        private string GetLocalPlayerBuffText()
        {
            if (_localPlayerId.Length == 0 || !_renderStates.TryGetValue(_localPlayerId, out var renderState))
            {
                return "mass 0";
            }

            return $"mass {DotArenaPresentation.FormatMass(renderState.Mass)} / speed {renderState.MoveSpeed:0.0}";
        }

        private string GetCurrentEventMessage()
        {
            if (_eventMessageUntil > 0f && Time.time > _eventMessageUntil)
            {
                _eventMessageUntil = 0f;
                _eventMessage = _views.Count < 2 ? "等待玩家加入" : "对局进行中";
            }

            return _eventMessage;
        }

        private void PushEvent(string message, float durationSeconds = 3f)
        {
            _eventMessage = message;
            _eventMessageUntil = Time.time + durationSeconds;
        }
    }
}
