#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Shared.Interfaces;
using UnityEngine;

namespace Shared.Gameplay
{
    public sealed class ArenaSimulationOptions
    {
        public ArenaConfig Arena { get; set; } = ArenaConfig.CreateDefault();
        public float RespawnDelaySeconds { get; set; } = 5f;
        public int MinPlayersToStart { get; set; } = 2;
        public int TargetParticipantCount { get; set; } = 4;
        public int RestartDelayTicks { get; set; } = 60;
        public float MaxRoundSeconds { get; set; } = 120f;
        public int EliminationCreditWindowTicks { get; set; } = 20;
        public float BaseSpeed { get; set; } = 6f;
        public float SpeedBoostMultiplier { get; set; } = 2f;
        public float SpeedBoostDurationSeconds { get; set; } = 10f;
        public float DashSpeed { get; set; } = 12f;
        public float DashTimeSeconds { get; set; } = 0.3f;
        public float PushForce { get; set; } = 10f;
        public float KnockbackBoostMultiplier { get; set; } = 3f;
        public float KnockbackBoostDurationSeconds { get; set; } = 5f;
        public float ShieldDurationSeconds { get; set; } = 8f;
        public int BonusScoreAmount { get; set; } = 3;
        public float StunTimeSeconds { get; set; } = 0.2f;
        public float PickupRespawnMinSeconds { get; set; } = 2f;
        public float PickupRespawnMaxSeconds { get; set; } = 5f;
        public float ShrinkStartDelaySeconds { get; set; } = 12f;
        public float ShrinkDurationSeconds { get; set; } = 40f;
        public Vector2 FinalArenaHalfExtents { get; set; } = new(14f, 14f);
        public string BotPrefix { get; set; } = "AI";
        public float BotEdgeAvoidDistance { get; set; } = 2.25f;
        public float BotEmergencyEdgeDistance { get; set; } = 1f;
        public PickupType[] EnabledPickupTypes { get; set; } =
        {
            PickupType.SpeedBoost,
            PickupType.KnockbackBoost,
            PickupType.ScorePoint,
            PickupType.Shield,
            PickupType.BonusScore
        };
    }

    public sealed class ArenaPlayerRegistration
    {
        public string PlayerId { get; set; } = string.Empty;
        public int Score { get; set; }
        public int PreferredSpawnIndex { get; set; } = -1;
        public bool IsBot { get; set; }
        public int BotNumber { get; set; }
    }

    public sealed class ArenaPlayerSnapshot
    {
        public string PlayerId { get; set; } = string.Empty;
        public int Score { get; set; }
        public int SpawnIndex { get; set; }
        public bool IsBot { get; set; }
        public int BotNumber { get; set; }
    }

    public sealed class ArenaScoreUpdate
    {
        public string PlayerId { get; set; } = string.Empty;
        public int Score { get; set; }
        public bool IsBot { get; set; }
    }

    public sealed class ArenaStepResult
    {
        public ArenaStepResult(WorldState worldState, PlayerDead[] deaths, MatchEnd? matchEnd, ArenaScoreUpdate[] scoreUpdates)
        {
            WorldState = worldState;
            Deaths = deaths;
            MatchEnd = matchEnd;
            ScoreUpdates = scoreUpdates;
        }

        public WorldState WorldState { get; }
        public PlayerDead[] Deaths { get; }
        public MatchEnd? MatchEnd { get; }
        public ArenaScoreUpdate[] ScoreUpdates { get; }
    }

    public sealed class ArenaSimulation
    {
        private const int SpawnScore = 1;
        private static readonly Vector2 Zero = new(0f, 0f);

        private readonly List<PlayerDead> _pendingDeaths = new();
        private readonly List<ArenaScoreUpdate> _pendingScoreUpdates = new();
        private readonly Dictionary<string, ArenaPlayer> _players = new(StringComparer.Ordinal);
        private readonly Dictionary<PickupType, ArenaPickup> _pickups = new();
        private readonly ArenaSimulationOptions _options;
        private readonly System.Random _random = new();
        private readonly int _respawnDelaySecondsCeiling;
        private MatchEnd? _pendingMatchEnd;
        private int? _restartAtTick;
        private int _tick;
        private float _roundElapsedSeconds;
        private Vector2 _currentArenaHalfExtents;
        private string? _winnerPlayerId;
        private int _nextBotNumber = 1;

        public ArenaSimulation(ArenaSimulationOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _respawnDelaySecondsCeiling = (int)MathF.Ceiling(Math.Max(1f, _options.RespawnDelaySeconds));
            _currentArenaHalfExtents = options.Arena.ArenaHalfExtents;
        }

        public int TickCount => _tick;

        public void UpsertPlayer(ArenaPlayerRegistration registration)
        {
            if (string.IsNullOrWhiteSpace(registration.PlayerId))
            {
                throw new ArgumentException("Player id is required.", nameof(registration));
            }

            if (_players.TryGetValue(registration.PlayerId, out var existing))
            {
                existing.Score = NormalizeScore(existing.Score);
                return;
            }

            var spawnIndex = ClaimSpawnIndex(registration.PreferredSpawnIndex);
            var player = new ArenaPlayer(
                registration.PlayerId,
                spawnIndex,
                NormalizeScore(registration.Score),
                registration.IsBot,
                registration.BotNumber)
            {
                Position = GetSpawnPosition(spawnIndex)
            };
            _players.Add(registration.PlayerId, player);

            if (!registration.IsBot)
            {
                RebalanceBots();
                ResetMatchIfNeeded();
            }
        }

        public bool RemovePlayer(string playerId, out ArenaPlayerSnapshot snapshot)
        {
            snapshot = new ArenaPlayerSnapshot();
            if (!_players.Remove(playerId, out var player))
            {
                return false;
            }

            snapshot = CreateSnapshot(player);
            _pendingDeaths.RemoveAll(deadEvent => string.Equals(deadEvent.PlayerId, playerId, StringComparison.Ordinal));

            if (player.IsBot)
            {
                ReleaseBot(player);
            }
            else
            {
                RebalanceBots();
            }

            if (HumanPlayerCount() == 0)
            {
                RemoveAllBots();
                ClearMatchState();
                _pickups.Clear();
                _tick = 0;
                return true;
            }

            if (_players.Count < _options.MinPlayersToStart ||
                string.Equals(_winnerPlayerId, playerId, StringComparison.Ordinal))
            {
                ClearMatchState();
            }

            return true;
        }

        public bool TryGetPlayerSnapshot(string playerId, out ArenaPlayerSnapshot snapshot)
        {
            if (_players.TryGetValue(playerId, out var player))
            {
                snapshot = CreateSnapshot(player);
                return true;
            }

            snapshot = new ArenaPlayerSnapshot();
            return false;
        }

        public void SubmitInput(InputMessage input)
        {
            if (!_players.TryGetValue(input.PlayerId, out var player) || !player.Alive)
            {
                return;
            }

            player.LastInputTick = input.Tick;
            player.Input = new Vector2(
                Math.Clamp(input.MoveX, -1f, 1f),
                Math.Clamp(input.MoveY, -1f, 1f));

            if (input.Dash)
            {
                player.PendingDash = true;
            }
        }

        public ArenaStepResult Tick(float deltaTime)
        {
            _tick++;
            _pendingDeaths.Clear();

            if (_players.Count > 0)
            {
                UpdateArenaBounds(deltaTime);
                UpdateBotInputs();
                SimulatePlayers(deltaTime);
                UpdatePickups(deltaTime);
                ResolvePushes();
                ResolvePickupCollections();
                ResolveEliminations();
                ResolveMatchLifecycle();
            }

            var result = new ArenaStepResult(
                CreateWorldState(),
                _pendingDeaths.ToArray(),
                _pendingMatchEnd,
                _pendingScoreUpdates.ToArray());
            _pendingMatchEnd = null;
            _pendingScoreUpdates.Clear();
            return result;
        }

        public WorldState CreateWorldState()
        {
            var state = new WorldState
            {
                Tick = _tick,
                RespawnDelaySeconds = _respawnDelaySecondsCeiling,
                ArenaHalfExtentX = _currentArenaHalfExtents.x,
                ArenaHalfExtentY = _currentArenaHalfExtents.y,
                RoundRemainingSeconds = (_winnerPlayerId is null && _players.Count >= _options.MinPlayersToStart)
                    ? (int)MathF.Ceiling(MathF.Max(0f, _options.MaxRoundSeconds - _roundElapsedSeconds))
                    : 0
            };

            foreach (var player in _players.Values.OrderBy(static p => p.PlayerId, StringComparer.Ordinal))
            {
                state.Players.Add(new PlayerState
                {
                    PlayerId = player.PlayerId,
                    X = player.Position.x,
                    Y = player.Position.y,
                    Vx = player.Velocity.x,
                    Vy = player.Velocity.y,
                    State = GetLifeState(player),
                    Alive = player.Alive,
                    RespawnRemainingSeconds = player.Alive ? 0 : (int)MathF.Ceiling(player.RespawnRemaining),
                    Score = player.Score,
                    SpeedBoostRemainingSeconds = (int)MathF.Ceiling(player.SpeedBoostRemaining),
                    KnockbackBoostRemainingSeconds = (int)MathF.Ceiling(player.KnockbackBoostRemaining),
                    ShieldRemainingSeconds = (int)MathF.Ceiling(player.ShieldRemaining)
                });
            }

            foreach (var pickup in _pickups.Values.Where(static p => p.Active).OrderBy(static p => p.Type))
            {
                state.Pickups.Add(new PickupState
                {
                    Type = pickup.Type,
                    X = pickup.Position.x,
                    Y = pickup.Position.y
                });
            }

            return state;
        }

        public void Clear()
        {
            _players.Clear();
            _pickups.Clear();
            _pendingDeaths.Clear();
            _pendingScoreUpdates.Clear();
            _pendingMatchEnd = null;
            _restartAtTick = null;
            _winnerPlayerId = null;
            _tick = 0;
            _roundElapsedSeconds = 0f;
            _currentArenaHalfExtents = _options.Arena.ArenaHalfExtents;
            _nextBotNumber = 1;
        }

        private void UpdateArenaBounds(float deltaTime)
        {
            if (_players.Count < _options.MinPlayersToStart || _winnerPlayerId is not null)
            {
                _roundElapsedSeconds = 0f;
                _currentArenaHalfExtents = _options.Arena.ArenaHalfExtents;
                return;
            }

            _roundElapsedSeconds += deltaTime;

            var baseHalfExtents = _options.Arena.ArenaHalfExtents;
            var finalHalfExtents = new Vector2(
                MathF.Min(baseHalfExtents.x, MathF.Max(2f, _options.FinalArenaHalfExtents.x)),
                MathF.Min(baseHalfExtents.y, MathF.Max(2f, _options.FinalArenaHalfExtents.y)));

            if (_roundElapsedSeconds <= _options.ShrinkStartDelaySeconds ||
                _options.ShrinkDurationSeconds <= 0f)
            {
                _currentArenaHalfExtents = baseHalfExtents;
            }
            else
            {
                var progress = Math.Clamp(
                    (_roundElapsedSeconds - _options.ShrinkStartDelaySeconds) / _options.ShrinkDurationSeconds,
                    0f,
                    1f);
                _currentArenaHalfExtents = new Vector2(
                    baseHalfExtents.x + ((finalHalfExtents.x - baseHalfExtents.x) * progress),
                    baseHalfExtents.y + ((finalHalfExtents.y - baseHalfExtents.y) * progress));
            }

            foreach (var pickup in _pickups.Values)
            {
                if (!pickup.Active)
                {
                    continue;
                }

                if (MathF.Abs(pickup.Position.x) <= _currentArenaHalfExtents.x &&
                    MathF.Abs(pickup.Position.y) <= _currentArenaHalfExtents.y)
                {
                    continue;
                }

                pickup.Active = false;
                pickup.RespawnRemaining = 1f;
            }
        }

        private void SimulatePlayers(float deltaTime)
        {
            foreach (var player in _players.Values)
            {
                if (!player.Alive)
                {
                    if (player.RespawnRemaining > 0f)
                    {
                        player.RespawnRemaining = MathF.Max(0f, player.RespawnRemaining - deltaTime);
                    }

                    if (player.RespawnRemaining <= 0f)
                    {
                        RespawnPlayer(player);
                    }

                    player.Velocity = Zero;
                    continue;
                }

                if (player.StunRemaining > 0f) player.StunRemaining = MathF.Max(0f, player.StunRemaining - deltaTime);
                if (player.DashRemaining > 0f) player.DashRemaining = MathF.Max(0f, player.DashRemaining - deltaTime);
                if (player.SpeedBoostRemaining > 0f) player.SpeedBoostRemaining = MathF.Max(0f, player.SpeedBoostRemaining - deltaTime);
                if (player.KnockbackBoostRemaining > 0f) player.KnockbackBoostRemaining = MathF.Max(0f, player.KnockbackBoostRemaining - deltaTime);
                if (player.ShieldRemaining > 0f) player.ShieldRemaining = MathF.Max(0f, player.ShieldRemaining - deltaTime);

                var desired = player.Input;
                if (LengthSquared(desired) > 1f)
                {
                    desired = Normalize(desired);
                }

                if (player.PendingDash && LengthSquared(desired) > 0f && player.DashRemaining <= 0f && player.StunRemaining <= 0f)
                {
                    player.DashRemaining = _options.DashTimeSeconds;
                    player.PendingDash = false;
                    player.DashDirection = desired;
                }
                else
                {
                    player.PendingDash = false;
                }

                if (player.StunRemaining > 0f)
                {
                    player.Velocity *= 0.85f;
                }
                else if (player.DashRemaining > 0f)
                {
                    player.Velocity = player.DashDirection * _options.DashSpeed;
                }
                else if (LengthSquared(desired) > 0f)
                {
                    player.Velocity = desired * GetMoveSpeed(player);
                }
                else
                {
                    player.Velocity = Zero;
                }

                player.Position += player.Velocity * deltaTime;
            }
        }

        private void UpdatePickups(float deltaTime)
        {
            foreach (var pickupType in _options.EnabledPickupTypes)
            {
                if (!_pickups.TryGetValue(pickupType, out var pickup))
                {
                    pickup = new ArenaPickup(pickupType) { RespawnRemaining = NextPickupRespawnDelaySeconds() };
                    _pickups.Add(pickupType, pickup);
                }

                if (pickup.Active)
                {
                    continue;
                }

                pickup.RespawnRemaining = MathF.Max(0f, pickup.RespawnRemaining - deltaTime);
                if (pickup.RespawnRemaining > 0f)
                {
                    continue;
                }

                pickup.Position = GetRandomPickupPosition();
                pickup.Active = true;
                pickup.RespawnRemaining = 0f;
            }
        }

        private void ResolvePushes()
        {
            var alivePlayers = _players.Values.Where(static p => p.Alive).ToArray();
            for (var i = 0; i < alivePlayers.Length; i++)
            {
                for (var j = i + 1; j < alivePlayers.Length; j++)
                {
                    var a = alivePlayers[i];
                    var b = alivePlayers[j];
                    var offset = b.Position - a.Position;
                    var distance = MathF.Sqrt(LengthSquared(offset));
                    var minDistance = _options.Arena.PlayerCollisionRadius * 2f;
                    if (distance <= 0.0001f || distance >= minDistance)
                    {
                        continue;
                    }

                    var direction = offset / distance;
                    var overlap = minDistance - distance;
                    var separation = direction * (overlap * 0.5f);
                    a.Position -= separation;
                    b.Position += separation;

                    var pushFromA = direction * (_options.PushForce * GetPushScale(a));
                    var pushFromB = direction * (_options.PushForce * GetPushScale(b));
                    a.Velocity -= pushFromB;
                    b.Velocity += pushFromA;
                    a.StunRemaining = MathF.Max(a.StunRemaining, _options.StunTimeSeconds);
                    b.StunRemaining = MathF.Max(b.StunRemaining, _options.StunTimeSeconds);
                    a.LastTouchedByPlayerId = b.PlayerId;
                    a.LastTouchedTick = _tick;
                    b.LastTouchedByPlayerId = a.PlayerId;
                    b.LastTouchedTick = _tick;
                }
            }
        }

        private void ResolvePickupCollections()
        {
            if (_pickups.Count == 0)
            {
                return;
            }

            var collectionDistance = _options.Arena.PlayerCollisionRadius + _options.Arena.PickupCollisionRadius;
            var collectionDistanceSquared = collectionDistance * collectionDistance;

            foreach (var player in _players.Values)
            {
                if (!player.Alive)
                {
                    continue;
                }

                foreach (var pickup in _pickups.Values)
                {
                    if (!pickup.Active || LengthSquared(player.Position - pickup.Position) > collectionDistanceSquared)
                    {
                        continue;
                    }

                    ApplyPickup(player, pickup.Type);
                    pickup.Active = false;
                    pickup.RespawnRemaining = NextPickupRespawnDelaySeconds();
                }
            }
        }

        private void ResolveEliminations()
        {
            var botsToRemove = new List<string>();

            foreach (var player in _players.Values)
            {
                if (!player.Alive)
                {
                    continue;
                }

                if (MathF.Abs(player.Position.x) <= _currentArenaHalfExtents.x &&
                    MathF.Abs(player.Position.y) <= _currentArenaHalfExtents.y)
                {
                    continue;
                }

                if (player.ShieldRemaining > 0f)
                {
                    player.Velocity = Zero;
                    player.Input = Zero;
                    player.DashDirection = Zero;
                    player.DashRemaining = 0f;
                    player.PendingDash = false;
                    player.Position = new Vector2(
                        Math.Clamp(player.Position.x, -_currentArenaHalfExtents.x, _currentArenaHalfExtents.x),
                        Math.Clamp(player.Position.y, -_currentArenaHalfExtents.y, _currentArenaHalfExtents.y));
                    player.ShieldRemaining = 0f;
                    player.StunRemaining = MathF.Max(player.StunRemaining, _options.StunTimeSeconds * 1.5f);
                    continue;
                }

                player.Alive = false;
                player.Velocity = Zero;
                player.Input = Zero;
                player.DashDirection = Zero;
                player.DashRemaining = 0f;
                player.StunRemaining = 0f;
                player.PendingDash = false;
                player.RespawnRemaining = Math.Max(1f, _options.RespawnDelaySeconds);
                player.SpeedBoostRemaining = 0f;
                player.KnockbackBoostRemaining = 0f;
                player.ShieldRemaining = 0f;

                if (TryGetScoringPlayer(player, out var scorer))
                {
                    AdjustScore(scorer, 1);
                }

                _pendingDeaths.Add(new PlayerDead
                {
                    PlayerId = player.PlayerId,
                    Tick = _tick
                });

                if (player.IsBot && HumanPlayerCount() >= _options.TargetParticipantCount)
                {
                    botsToRemove.Add(player.PlayerId);
                }
            }

            foreach (var botPlayerId in botsToRemove)
            {
                RemoveBot(botPlayerId);
            }
        }

        private void ResolveMatchLifecycle()
        {
            if (_restartAtTick is int restartTick && _tick >= restartTick)
            {
                ResetMatch();
                return;
            }

            if (_winnerPlayerId is not null)
            {
                return;
            }

            var alivePlayers = _players.Values.Where(static p => p.Alive).ToArray();
            if (_players.Count < _options.MinPlayersToStart)
            {
                return;
            }

            if (alivePlayers.Length <= 1 || _roundElapsedSeconds >= _options.MaxRoundSeconds)
            {
                var winner = _players.Values
                    .OrderByDescending(static player => player.Score)
                    .ThenByDescending(static player => player.Alive)
                    .ThenBy(static player => player.PlayerId, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (winner is null)
                {
                    return;
                }

                _winnerPlayerId = winner.PlayerId;
                _restartAtTick = _tick + _options.RestartDelayTicks;
                _pendingMatchEnd = new MatchEnd
                {
                    WinnerPlayerId = _winnerPlayerId,
                    Tick = _tick
                };
            }
        }

        private void ResetMatchIfNeeded()
        {
            if (_players.Count >= _options.MinPlayersToStart && _players.Values.All(static p => !p.Alive))
            {
                ResetMatch();
            }
        }

        private void ResetMatch()
        {
            ClearMatchState();

            foreach (var player in _players.Values.OrderBy(static p => p.PlayerId, StringComparer.Ordinal))
            {
                player.Position = GetSpawnPosition(player.SpawnIndex);
                player.Velocity = Zero;
                player.Input = Zero;
                player.DashDirection = Zero;
                player.DashRemaining = 0f;
                player.StunRemaining = 0f;
                player.PendingDash = false;
                player.Alive = true;
                player.RespawnRemaining = 0f;
                player.LastTouchedByPlayerId = null;
                player.LastTouchedTick = 0;
                player.SpeedBoostRemaining = 0f;
                player.KnockbackBoostRemaining = 0f;
                player.ShieldRemaining = 0f;
                player.Score = SpawnScore;
                _pendingScoreUpdates.RemoveAll(update => string.Equals(update.PlayerId, player.PlayerId, StringComparison.Ordinal));
                _pendingScoreUpdates.Add(new ArenaScoreUpdate
                {
                    PlayerId = player.PlayerId,
                    Score = player.Score,
                    IsBot = player.IsBot
                });
            }
        }

        private void ClearMatchState()
        {
            _winnerPlayerId = null;
            _pendingMatchEnd = null;
            _restartAtTick = null;
            _roundElapsedSeconds = 0f;
            _currentArenaHalfExtents = _options.Arena.ArenaHalfExtents;
        }

        private int ClaimSpawnIndex(int preferredSpawnIndex)
        {
            if (preferredSpawnIndex >= 0 && _players.Values.All(player => player.SpawnIndex != preferredSpawnIndex))
            {
                return preferredSpawnIndex;
            }

            return GetNextSpawnIndex();
        }

        private int GetNextSpawnIndex()
        {
            var used = _players.Values.Select(static player => player.SpawnIndex).ToHashSet();
            var index = 0;
            while (used.Contains(index))
            {
                index++;
            }

            return index;
        }

        private bool TryGetScoringPlayer(ArenaPlayer eliminatedPlayer, out ArenaPlayer scorer)
        {
            scorer = null!;
            var scoringPlayerId = eliminatedPlayer.LastTouchedByPlayerId;
            if (string.IsNullOrWhiteSpace(scoringPlayerId))
            {
                return false;
            }

            if (_tick - eliminatedPlayer.LastTouchedTick > _options.EliminationCreditWindowTicks)
            {
                return false;
            }

            if (!_players.TryGetValue(scoringPlayerId, out var candidate) ||
                string.Equals(candidate.PlayerId, eliminatedPlayer.PlayerId, StringComparison.Ordinal))
            {
                return false;
            }

            scorer = candidate;
            return true;
        }

        private void AdjustScore(ArenaPlayer player, int delta)
        {
            player.Score = NormalizeScore(player.Score + delta);
            _pendingScoreUpdates.RemoveAll(update => string.Equals(update.PlayerId, player.PlayerId, StringComparison.Ordinal));
            _pendingScoreUpdates.Add(new ArenaScoreUpdate
            {
                PlayerId = player.PlayerId,
                Score = player.Score,
                IsBot = player.IsBot
            });
        }

        private void RespawnPlayer(ArenaPlayer player)
        {
            player.Position = GetSpawnPosition(player.SpawnIndex);
            player.Velocity = Zero;
            player.Input = Zero;
            player.DashDirection = Zero;
            player.DashRemaining = 0f;
            player.StunRemaining = 0f;
            player.PendingDash = false;
            player.Alive = true;
            player.RespawnRemaining = 0f;
            player.LastTouchedByPlayerId = null;
            player.LastTouchedTick = 0;
            player.SpeedBoostRemaining = 0f;
            player.KnockbackBoostRemaining = 0f;
            player.ShieldRemaining = 0f;
            player.Score = SpawnScore;
            _pendingScoreUpdates.RemoveAll(update => string.Equals(update.PlayerId, player.PlayerId, StringComparison.Ordinal));
            _pendingScoreUpdates.Add(new ArenaScoreUpdate
            {
                PlayerId = player.PlayerId,
                Score = player.Score,
                IsBot = player.IsBot
            });
        }

        private static PlayerLifeState GetLifeState(ArenaPlayer player)
        {
            if (!player.Alive) return PlayerLifeState.Dead;
            if (player.StunRemaining > 0f) return PlayerLifeState.Stunned;
            if (player.DashRemaining > 0f) return PlayerLifeState.Dash;
            return LengthSquared(player.Input) > 0.001f ? PlayerLifeState.Move : PlayerLifeState.Idle;
        }

        private static int NormalizeScore(int score)
        {
            return Math.Max(0, score);
        }

        private float GetMoveSpeed(ArenaPlayer player)
        {
            return _options.BaseSpeed * (player.SpeedBoostRemaining > 0f ? _options.SpeedBoostMultiplier : 1f);
        }

        private float GetPushScale(ArenaPlayer player)
        {
            var pushScale = player.DashRemaining > 0f ? 1.75f : 1f;
            if (player.KnockbackBoostRemaining > 0f)
            {
                pushScale *= _options.KnockbackBoostMultiplier;
            }

            return pushScale;
        }

        private void ApplyPickup(ArenaPlayer player, PickupType pickupType)
        {
            switch (pickupType)
            {
                case PickupType.SpeedBoost:
                    player.SpeedBoostRemaining = _options.SpeedBoostDurationSeconds;
                    break;
                case PickupType.KnockbackBoost:
                    player.KnockbackBoostRemaining = _options.KnockbackBoostDurationSeconds;
                    break;
                case PickupType.ScorePoint:
                    AdjustScore(player, 1);
                    break;
                case PickupType.Shield:
                    player.ShieldRemaining = _options.ShieldDurationSeconds;
                    break;
                case PickupType.BonusScore:
                    AdjustScore(player, _options.BonusScoreAmount);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(pickupType), pickupType, null);
            }
        }

        private static float LengthSquared(Vector2 value)
        {
            return value.x * value.x + value.y * value.y;
        }

        private static Vector2 Normalize(Vector2 value)
        {
            var length = MathF.Sqrt(LengthSquared(value));
            return length <= 0.0001f ? Zero : value / length;
        }

        private Vector2 GetSpawnPosition(int index)
        {
            var insetX = MathF.Max(0.5f, _currentArenaHalfExtents.x - _options.Arena.RespawnInset);
            var insetY = MathF.Max(0.5f, _currentArenaHalfExtents.y - _options.Arena.RespawnInset);
            var points = new[]
            {
                new Vector2(-insetX, -insetY),
                new Vector2(insetX, -insetY),
                new Vector2(-insetX, insetY),
                new Vector2(insetX, insetY),
                new Vector2(0f, -insetY),
                new Vector2(0f, insetY),
                new Vector2(-insetX, 0f),
                new Vector2(insetX, 0f)
            };

            return points[index % points.Length];
        }

        private Vector2 GetRandomPickupPosition()
        {
            var minX = -MathF.Max(0.5f, _currentArenaHalfExtents.x - _options.Arena.PickupSpawnInset);
            var maxX = MathF.Max(0.5f, _currentArenaHalfExtents.x - _options.Arena.PickupSpawnInset);
            var minY = -MathF.Max(0.5f, _currentArenaHalfExtents.y - _options.Arena.PickupSpawnInset);
            var maxY = MathF.Max(0.5f, _currentArenaHalfExtents.y - _options.Arena.PickupSpawnInset);

            return new Vector2(
                (float)(_random.NextDouble() * (maxX - minX)) + minX,
                (float)(_random.NextDouble() * (maxY - minY)) + minY);
        }

        private float NextPickupRespawnDelaySeconds()
        {
            return _options.PickupRespawnMinSeconds +
                   ((float)_random.NextDouble() * (_options.PickupRespawnMaxSeconds - _options.PickupRespawnMinSeconds));
        }

        private void UpdateBotInputs()
        {
            var alivePlayers = _players.Values.Where(static player => player.Alive).ToArray();
            foreach (var bot in _players.Values.Where(static player => player.IsBot))
            {
                if (!bot.Alive)
                {
                    bot.Input = Zero;
                    bot.PendingDash = false;
                    continue;
                }

                var target = alivePlayers
                    .Where(player => !string.Equals(player.PlayerId, bot.PlayerId, StringComparison.Ordinal))
                    .OrderBy(player => LengthSquared(player.Position - bot.Position))
                    .FirstOrDefault();

                var chase = target is null ? -bot.Position : target.Position - bot.Position;
                if (LengthSquared(chase) <= 0.0001f)
                {
                    chase = new Vector2(MathF.Sin((_tick * 0.13f) + bot.BotNumber), MathF.Cos((_tick * 0.11f) + bot.BotNumber));
                }

                var edgeAvoidance = ComputeBotEdgeAvoidance(bot.Position);
                var desired = chase + edgeAvoidance;
                if (LengthSquared(desired) <= 0.0001f)
                {
                    desired = chase;
                }

                bot.Input = Normalize(desired);
                var shouldDash = target is not null &&
                                 LengthSquared(edgeAvoidance) < 0.25f &&
                                 LengthSquared(target.Position - bot.Position) > 9f &&
                                 bot.DashRemaining <= 0f &&
                                 bot.StunRemaining <= 0f &&
                                 (_tick + bot.BotNumber) % 18 == 0;
                if (shouldDash)
                {
                    bot.PendingDash = true;
                }

                bot.LastInputTick = _tick;
            }
        }

        private Vector2 ComputeBotEdgeAvoidance(Vector2 position)
        {
            var safeLimitX = MathF.Max(0.5f, _currentArenaHalfExtents.x - _options.Arena.PlayerCollisionRadius);
            var safeLimitY = MathF.Max(0.5f, _currentArenaHalfExtents.y - _options.Arena.PlayerCollisionRadius);

            var avoidance = Zero;
            avoidance.x = ComputeAxisAvoidance(position.x, safeLimitX);
            avoidance.y = ComputeAxisAvoidance(position.y, safeLimitY);

            var marginX = safeLimitX - MathF.Abs(position.x);
            var marginY = safeLimitY - MathF.Abs(position.y);
            var emergencyMargin = MathF.Min(marginX, marginY);
            if (emergencyMargin < _options.BotEmergencyEdgeDistance)
            {
                var toCenter = -position;
                if (LengthSquared(toCenter) > 0.0001f)
                {
                    avoidance += Normalize(toCenter) * 2.5f;
                }
            }

            return avoidance;
        }

        private float ComputeAxisAvoidance(float coordinate, float safeLimit)
        {
            var distanceToEdge = safeLimit - MathF.Abs(coordinate);
            if (distanceToEdge >= _options.BotEdgeAvoidDistance)
            {
                return 0f;
            }

            var directionToCenter = coordinate > 0f ? -1f : 1f;
            var pressure = 1f - Math.Clamp(distanceToEdge / _options.BotEdgeAvoidDistance, 0f, 1f);
            return directionToCenter * pressure * pressure * 2f;
        }

        private void RebalanceBots()
        {
            var humanCount = HumanPlayerCount();
            var desiredBotCount = Math.Max(0, _options.TargetParticipantCount - humanCount);
            var currentBotCount = _players.Values.Count(static player => player.IsBot);

            while (currentBotCount < desiredBotCount)
            {
                AddBot();
                currentBotCount++;
            }

            while (currentBotCount > desiredBotCount)
            {
                var removableBot = _players.Values
                    .Where(static player => player.IsBot)
                    .OrderByDescending(static player => player.BotNumber)
                    .FirstOrDefault();
                if (removableBot is null)
                {
                    break;
                }

                RemoveBot(removableBot.PlayerId);
                currentBotCount--;
            }
        }

        private int HumanPlayerCount()
        {
            return _players.Values.Count(static player => !player.IsBot);
        }

        private void AddBot()
        {
            var botName = $"{_options.BotPrefix}{_nextBotNumber:D2}";
            while (_players.ContainsKey(botName))
            {
                _nextBotNumber++;
                botName = $"{_options.BotPrefix}{_nextBotNumber:D2}";
            }

            var botNumber = _nextBotNumber;
            _nextBotNumber++;
            UpsertPlayer(new ArenaPlayerRegistration
            {
                PlayerId = botName,
                Score = SpawnScore,
                PreferredSpawnIndex = GetNextSpawnIndex(),
                IsBot = true,
                BotNumber = botNumber
            });
        }

        private void RemoveBot(string playerId)
        {
            if (!_players.Remove(playerId, out var bot))
            {
                return;
            }

            ReleaseBot(bot);
            _pendingDeaths.RemoveAll(deadEvent => string.Equals(deadEvent.PlayerId, playerId, StringComparison.Ordinal));
        }

        private void RemoveAllBots()
        {
            var botIds = _players.Values.Where(static player => player.IsBot).Select(static player => player.PlayerId).ToArray();
            foreach (var botId in botIds)
            {
                RemoveBot(botId);
            }
        }

        private void ReleaseBot(ArenaPlayer bot)
        {
            _pendingDeaths.RemoveAll(deadEvent => string.Equals(deadEvent.PlayerId, bot.PlayerId, StringComparison.Ordinal));
        }

        private static ArenaPlayerSnapshot CreateSnapshot(ArenaPlayer player)
        {
            return new ArenaPlayerSnapshot
            {
                PlayerId = player.PlayerId,
                Score = player.Score,
                SpawnIndex = player.SpawnIndex,
                IsBot = player.IsBot,
                BotNumber = player.BotNumber
            };
        }

        private sealed class ArenaPickup
        {
            public ArenaPickup(PickupType type)
            {
                Type = type;
            }

            public PickupType Type { get; }
            public bool Active { get; set; }
            public Vector2 Position { get; set; }
            public float RespawnRemaining { get; set; }
        }

        private sealed class ArenaPlayer
        {
            public ArenaPlayer(string playerId, int spawnIndex, int score, bool isBot, int botNumber)
            {
                PlayerId = playerId;
                SpawnIndex = spawnIndex;
                Position = Zero;
                Score = NormalizeScore(score);
                Alive = true;
                IsBot = isBot;
                BotNumber = botNumber;
            }

            public string PlayerId { get; }
            public int SpawnIndex { get; }
            public bool IsBot { get; }
            public int BotNumber { get; }
            public Vector2 Position { get; set; }
            public Vector2 Velocity { get; set; }
            public Vector2 Input { get; set; }
            public Vector2 DashDirection { get; set; }
            public float DashRemaining { get; set; }
            public float StunRemaining { get; set; }
            public bool PendingDash { get; set; }
            public bool Alive { get; set; }
            public float RespawnRemaining { get; set; }
            public int LastInputTick { get; set; }
            public int Score { get; set; }
            public string? LastTouchedByPlayerId { get; set; }
            public int LastTouchedTick { get; set; }
            public float SpeedBoostRemaining { get; set; }
            public float KnockbackBoostRemaining { get; set; }
            public float ShieldRemaining { get; set; }
        }
    }
}
