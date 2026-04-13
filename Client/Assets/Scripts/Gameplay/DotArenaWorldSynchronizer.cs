#nullable enable

using System;
using System.Collections.Generic;
using Shared.Gameplay;
using Shared.Interfaces;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    internal sealed class DotArenaWorldSynchronizer
    {
        private readonly Dictionary<string, DotView> _views;
        private readonly Dictionary<string, PlayerRenderState> _renderStates;
        private readonly Dictionary<PickupType, PickupView> _pickupViews;
        private readonly Dictionary<string, PlayerOverlayView> _playerOverlayViews;
        private readonly Func<string, DotView> _createView;
        private readonly Action<string> _ensurePlayerOverlay;
        private readonly Func<PickupType, PickupView> _createPickupView;
        private readonly Action<UnityEngine.Object> _destroyObject;
        private readonly Action _updateArenaVisuals;
        private readonly Action<string> _pushEvent;
        private readonly Action<string, float> _pushEventWithDuration;
        private readonly Action<string> _setEventMessage;

        public DotArenaWorldSynchronizer(
            Dictionary<string, DotView> views,
            Dictionary<string, PlayerRenderState> renderStates,
            Dictionary<PickupType, PickupView> pickupViews,
            Dictionary<string, PlayerOverlayView> playerOverlayViews,
            Func<string, DotView> createView,
            Action<string> ensurePlayerOverlay,
            Func<PickupType, PickupView> createPickupView,
            Action<UnityEngine.Object> destroyObject,
            Action updateArenaVisuals,
            Action<string> pushEvent,
            Action<string, float> pushEventWithDuration,
            Action<string> setEventMessage)
        {
            _views = views;
            _renderStates = renderStates;
            _pickupViews = pickupViews;
            _playerOverlayViews = playerOverlayViews;
            _createView = createView;
            _ensurePlayerOverlay = ensurePlayerOverlay;
            _createPickupView = createPickupView;
            _destroyObject = destroyObject;
            _updateArenaVisuals = updateArenaVisuals;
            _pushEvent = pushEvent;
            _pushEventWithDuration = pushEventWithDuration;
            _setEventMessage = setEventMessage;
        }

        public void ApplyWorldState(
            WorldState worldState,
            string localPlayerId,
            ref int lastWorldTick,
            ref int lastRoundRemainingSeconds,
            ref int lastLoggedPlayerCount,
            ref Vector2 currentArenaHalfExtents)
        {
            if (worldState.Tick < lastWorldTick)
            {
                return;
            }

            var previousArenaHalfExtents = currentArenaHalfExtents;
            lastWorldTick = worldState.Tick;
            lastRoundRemainingSeconds = worldState.RoundRemainingSeconds;
            currentArenaHalfExtents = new Vector2(worldState.ArenaHalfExtentX, worldState.ArenaHalfExtentY);
            _updateArenaVisuals();
            if (worldState.Players.Count != lastLoggedPlayerCount)
            {
                lastLoggedPlayerCount = worldState.Players.Count;
                Debug.Log($"[DotArena] WorldState tick={worldState.Tick}, players={worldState.Players.Count}, local={localPlayerId}");
                Debug.Log($"[DotArena] Players => {string.Join(", ", worldState.Players.ConvertAll(static p => $"{p.PlayerId}@({p.X:0.00},{p.Y:0.00}) alive={p.Alive}"))}");
            }

            var activeIds = new HashSet<string>(StringComparer.Ordinal);
            var collectedPickups = new Dictionary<PickupType, string>();
            foreach (var player in worldState.Players)
            {
                activeIds.Add(player.PlayerId);
                var targetPosition = new Vector2(player.X, player.Y);
                var isNewView = false;
                var isNewRenderState = false;

                if (!_views.TryGetValue(player.PlayerId, out var view))
                {
                    view = _createView(player.PlayerId);
                    _views.Add(player.PlayerId, view);
                    _ensurePlayerOverlay(player.PlayerId);
                    view.SetPosition(targetPosition);
                    isNewView = true;
                    Debug.Log($"[DotArena] Created view for {player.PlayerId}, totalViews={_views.Count}");
                }

                if (!_renderStates.TryGetValue(player.PlayerId, out var renderState))
                {
                    renderState = new PlayerRenderState();
                    _renderStates.Add(player.PlayerId, renderState);
                    isNewRenderState = true;
                }

                var previousState = renderState.State;
                var previousScore = renderState.Score;

                var currentPosition = view.GetPosition();
                if (isNewView || isNewRenderState)
                {
                    currentPosition = targetPosition;
                }

                renderState.PreviousPosition = currentPosition;
                renderState.TargetPosition = targetPosition;
                renderState.ReceivedAt = Time.time;
                renderState.Alive = player.Alive;
                renderState.State = player.State;
                renderState.Score = player.Score;
                var previousSpeedBuff = renderState.SpeedBoostRemainingSeconds;
                var previousKnockbackBuff = renderState.KnockbackBoostRemainingSeconds;
                renderState.SpeedBoostRemainingSeconds = player.SpeedBoostRemainingSeconds;
                renderState.KnockbackBoostRemainingSeconds = player.KnockbackBoostRemainingSeconds;

                view.SetIdentity(player.PlayerId, player.Score);
                if (_playerOverlayViews.TryGetValue(player.PlayerId, out var overlay))
                {
                    overlay.NameText.text = player.PlayerId;
                    overlay.ScoreText.text = $"score: {DotArenaPresentation.FormatScore(player.Score)}";
                }

                if (previousState != PlayerLifeState.Stunned && player.State == PlayerLifeState.Stunned)
                {
                    view.TriggerCollisionJelly();
                }

                view.ApplyPresentation(
                    DotArenaPresentation.ResolvePlayerColor(player.PlayerId),
                    player.State,
                    player.Alive,
                    player.SpeedBoostRemainingSeconds > 0,
                    player.KnockbackBoostRemainingSeconds > 0);

                if (player.PlayerId == localPlayerId)
                {
                    if (player.Score > previousScore &&
                        !(previousSpeedBuff <= 0 && player.SpeedBoostRemainingSeconds > 0) &&
                        !(previousKnockbackBuff <= 0 && player.KnockbackBoostRemainingSeconds > 0))
                    {
                        _pushEvent($"积分 +{player.Score - previousScore}");
                    }

                    if (previousSpeedBuff <= 0 && player.SpeedBoostRemainingSeconds > 0)
                    {
                        _pushEvent($"拾取{DotArenaPresentation.GetPickupDisplayName(PickupType.SpeedBoost)}: 移速提升 100%，持续 10 秒");
                    }

                    if (previousKnockbackBuff <= 0 && player.KnockbackBoostRemainingSeconds > 0)
                    {
                        _pushEvent($"拾取{DotArenaPresentation.GetPickupDisplayName(PickupType.KnockbackBoost)}: 撞飞增强 50%，持续 5 秒");
                    }
                }

                if (_views.Count >= 2 && worldState.Players.Exists(static p => p.Alive))
                {
                    _setEventMessage("对局进行中");
                }
            }

            var removedIds = new List<string>();
            foreach (var playerId in _views.Keys)
            {
                if (!activeIds.Contains(playerId))
                {
                    removedIds.Add(playerId);
                }
            }

            foreach (var removedId in removedIds)
            {
                _destroyObject(_views[removedId].Root);
                _views.Remove(removedId);
                _renderStates.Remove(removedId);
                if (_playerOverlayViews.TryGetValue(removedId, out var overlay))
                {
                    _destroyObject(overlay.Root);
                    _playerOverlayViews.Remove(removedId);
                }
            }

            if (previousArenaHalfExtents.x >= GameplayConfig.ArenaHalfExtents.x - 0.05f &&
                currentArenaHalfExtents.x < previousArenaHalfExtents.x - 0.05f)
            {
                _pushEventWithDuration("开始缩圈，注意边界", 2.5f);
            }

            ApplyPickupState(worldState, collectedPickups);
            Debug.Log($"[DotArena] ApplyWorldState complete tick={worldState.Tick}, views={_views.Count}, renders={_renderStates.Count}");
        }

        private void ApplyPickupState(WorldState worldState, Dictionary<PickupType, string> collectedPickups)
        {
            var pickupScale = GameplayConfig.PickupCollisionRadius * 2f;
            var activeTypes = new HashSet<PickupType>();
            foreach (var pickup in worldState.Pickups)
            {
                activeTypes.Add(pickup.Type);
                if (!_pickupViews.TryGetValue(pickup.Type, out var view))
                {
                    view = _createPickupView(pickup.Type);
                    _pickupViews.Add(pickup.Type, view);
                }

                view.ShowAt(new Vector3(pickup.X, pickup.Y, 0f), pickupScale);
            }

            foreach (var entry in _pickupViews)
            {
                if (activeTypes.Contains(entry.Key))
                {
                    continue;
                }

                if (entry.Value.IsAbsorbing)
                {
                    continue;
                }

                if (TryGetPickupAbsorbTarget(entry.Key, collectedPickups, entry.Value.Root.transform.position, out var absorbTarget))
                {
                    entry.Value.StartAbsorb(absorbTarget, Time.time, pickupScale);
                    continue;
                }

                entry.Value.Root.SetActive(false);
            }
        }

        private bool TryGetPickupAbsorbTarget(PickupType pickupType, Dictionary<PickupType, string> collectedPickups, Vector3 pickupPosition, out Vector3 targetPosition)
        {
            if (collectedPickups.TryGetValue(pickupType, out var collectorId) &&
                _views.TryGetValue(collectorId, out var collectorView))
            {
                var collectorPosition = collectorView.GetPosition();
                targetPosition = new Vector3(collectorPosition.x, collectorPosition.y, 0f);
                return true;
            }

            var bestDistance = float.MaxValue;
            targetPosition = default;
            foreach (var entry in _views)
            {
                if (!_renderStates.TryGetValue(entry.Key, out var renderState) || !renderState.Alive)
                {
                    continue;
                }

                var candidate = entry.Value.GetPosition();
                var distance = (candidate - new Vector2(pickupPosition.x, pickupPosition.y)).sqrMagnitude;
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                targetPosition = new Vector3(candidate.x, candidate.y, 0f);
            }

            return bestDistance < float.MaxValue;
        }
    }
}
