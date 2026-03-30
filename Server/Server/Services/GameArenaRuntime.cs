using Orleans.Contracts.Users;
using Server.Orleans;
using Shared.Gameplay;
using Shared.Interfaces;
using UnityEngine;

namespace Server.Services;

public sealed class GameArenaRuntime
{
    private const float BaseSpeed = 6f;
    private const float DashSpeed = 12f;
    private const float DashTimeSeconds = 0.3f;
    private const float PushForce = 10f;
    private const float StunTimeSeconds = 0.2f;
    private const int MinPlayersToStart = 2;
    private const int TargetParticipantCount = 4;
    private const int RestartDelayTicks = 60;
    private const int EliminationCreditWindowTicks = 20;
    private const string BotPrefix = "AI";
    private const float BotEdgeAvoidDistance = 2.25f;
    private const float BotEmergencyEdgeDistance = 1.0f;
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

    private static readonly Vector2 Zero = new(0f, 0f);

    private readonly object _gate = new();
    private readonly List<PlayerDead> _pendingDeaths = new();
    private readonly List<ScoreUpdate> _pendingScoreUpdates = new();
    private readonly Dictionary<string, ConnectedPlayer> _players = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlayerProfile> _profiles = new(StringComparer.Ordinal);
    private readonly float _respawnDelaySeconds;
    private readonly int _respawnDelaySecondsCeiling;
    private readonly ArenaConfig _arenaConfig;
    private MatchEnd? _pendingMatchEnd;
    private int? _restartAtTick;
    private int _tick;
    private string? _winnerPlayerId;
    private int _nextBotNumber = 1;

    public GameArenaRuntime(GameArenaOptions options)
    {
        _arenaConfig = options.Arena ?? ArenaConfig.CreateDefault();
        _respawnDelaySeconds = Math.Max(1f, options.RespawnDelaySeconds);
        _respawnDelaySecondsCeiling = (int)MathF.Ceiling(_respawnDelaySeconds);
    }

    public ValueTask<LoginReply> RegisterPlayerAsync(UserLoginResult loginResult, IPlayerCallback callback)
    {
        WorldState snapshot;

        lock (_gate)
        {
            if (_players.TryGetValue(loginResult.UserId, out var existing))
            {
                existing.Callback = callback;
                existing.Connected = true;
                existing.Score = NormalizeScore(existing.Score);
            }
            else
            {
                if (!_profiles.TryGetValue(loginResult.UserId, out var profile))
                {
                    profile = new PlayerProfile
                    {
                        Score = NormalizeScore(loginResult.Score <= 0 ? 1 : loginResult.Score),
                        SpawnIndex = GetNextSpawnIndexLocked()
                    };
                    _profiles.Add(loginResult.UserId, profile);
                }
                else
                {
                    profile.Score = NormalizeScore(profile.Score <= 0 ? loginResult.Score : profile.Score);
                }

                profile.SpawnIndex = ClaimSpawnIndexLocked(profile.SpawnIndex);

                _players.Add(
                    loginResult.UserId,
                    new ConnectedPlayer(
                        loginResult.UserId,
                        callback,
                        profile.SpawnIndex,
                        GetSpawnPosition(profile.SpawnIndex),
                        profile.Score,
                        isBot: false));
            }

            RebalanceBotsLocked();
            ResetMatchIfNeededLocked();
            snapshot = CreateWorldStateLocked();
        }

        SafeInvoke(callback, cb => cb.OnWorldState(snapshot));

        return ValueTask.FromResult(new LoginReply
        {
            Code = 0,
            Token = loginResult.SessionToken,
            PlayerId = loginResult.UserId
        });
    }

    public void SubmitInput(InputMessage input)
    {
        lock (_gate)
        {
            if (!_players.TryGetValue(input.PlayerId, out var player) || !player.Alive) return;

            player.LastInputTick = input.Tick;
            player.Input = new Vector2(
                Math.Clamp(input.MoveX, -1f, 1f),
                Math.Clamp(input.MoveY, -1f, 1f));

            if (input.Dash) player.PendingDash = true;
        }
    }

    public ValueTask UnregisterPlayerAsync(string playerId)
    {
        IPlayerCallback[] callbacks;
        WorldState? snapshot = null;

        lock (_gate)
        {
            if (!_players.Remove(playerId, out var player)) return ValueTask.CompletedTask;

            CaptureProfileLocked(player);
            _pendingDeaths.RemoveAll(deadEvent =>
                string.Equals(deadEvent.PlayerId, playerId, StringComparison.Ordinal));

            if (player.IsBot)
            {
                ReleaseBotLocked(player);
            }

            RebalanceBotsLocked();

            if (HumanPlayerCountLocked() == 0)
            {
                RemoveAllBotsLocked();
                ClearMatchStateLocked();
                _tick = 0;
                return ValueTask.CompletedTask;
            }

            if (_players.Count < MinPlayersToStart ||
                string.Equals(_winnerPlayerId, playerId, StringComparison.Ordinal)) ClearMatchStateLocked();

            callbacks = _players.Values
                .Where(static p => p.Connected && p.Callback is not null)
                .Select(static p => p.Callback!)
                .Distinct()
                .ToArray();
            snapshot = CreateWorldStateLocked();
        }

        foreach (var callback in callbacks) SafeInvoke(callback, cb => cb.OnWorldState(snapshot));

        return ValueTask.CompletedTask;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) TickSimulation();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void TickSimulation()
    {
        BroadcastBatch batch;

        lock (_gate)
        {
            _tick++;
            _pendingDeaths.Clear();

            if (_players.Count > 0)
            {
                UpdateBotInputsLocked();
                SimulatePlayersLocked((float)TickInterval.TotalSeconds);
                ResolvePushesLocked();
                ResolveEliminationsLocked();
                ResolveMatchLifecycleLocked();
            }

            batch = CreateBroadcastBatchLocked();
        }

        PublishBatch(batch);
    }

    private void SimulatePlayersLocked(float deltaTime)
    {
        foreach (var player in _players.Values)
        {
            if (!player.Alive)
            {
                if (player.RespawnRemaining > 0f)
                    player.RespawnRemaining = MathF.Max(0f, player.RespawnRemaining - deltaTime);

                if (player.RespawnRemaining <= 0f) RespawnPlayerLocked(player);

                player.Velocity = Zero;
                continue;
            }

            if (player.StunRemaining > 0f) player.StunRemaining = MathF.Max(0f, player.StunRemaining - deltaTime);

            if (player.DashRemaining > 0f) player.DashRemaining = MathF.Max(0f, player.DashRemaining - deltaTime);

            var desired = player.Input;
            if (LengthSquared(desired) > 1f) desired = Normalize(desired);

            if (player.PendingDash && LengthSquared(desired) > 0f && player.DashRemaining <= 0f &&
                player.StunRemaining <= 0f)
            {
                player.DashRemaining = DashTimeSeconds;
                player.PendingDash = false;
                player.DashDirection = desired;
            }
            else
            {
                player.PendingDash = false;
            }

            if (player.StunRemaining > 0f)
                player.Velocity *= 0.85f;
            else if (player.DashRemaining > 0f)
                player.Velocity = player.DashDirection * DashSpeed;
            else if (LengthSquared(desired) > 0f)
                player.Velocity = desired * BaseSpeed;
            else
                player.Velocity = Zero;

            player.Position += player.Velocity * deltaTime;
        }
    }

    private void ResolvePushesLocked()
    {
        var alivePlayers = _players.Values.Where(static p => p.Alive).ToArray();
        for (var i = 0; i < alivePlayers.Length; i++)
        for (var j = i + 1; j < alivePlayers.Length; j++)
        {
            var a = alivePlayers[i];
            var b = alivePlayers[j];
            var offset = b.Position - a.Position;
            var distance = MathF.Sqrt(LengthSquared(offset));
            var minDistance = _arenaConfig.PlayerCollisionRadius * 2f;
            if (distance <= 0.0001f || distance >= minDistance) continue;

            var direction = offset / distance;
            var overlap = minDistance - distance;
            var separation = direction * (overlap * 0.5f);
            a.Position -= separation;
            b.Position += separation;

            var pushScale = a.DashRemaining > 0f || b.DashRemaining > 0f ? 1.75f : 1f;
            var impulse = direction * (PushForce * pushScale);
            a.Velocity -= impulse;
            b.Velocity += impulse;
            a.StunRemaining = MathF.Max(a.StunRemaining, StunTimeSeconds);
            b.StunRemaining = MathF.Max(b.StunRemaining, StunTimeSeconds);
            a.LastTouchedByPlayerId = b.PlayerId;
            a.LastTouchedTick = _tick;
            b.LastTouchedByPlayerId = a.PlayerId;
            b.LastTouchedTick = _tick;
        }
    }

    private void ResolveEliminationsLocked()
    {
        var botsToRemove = new List<string>();

        foreach (var player in _players.Values)
        {
            if (!player.Alive) continue;

            if (MathF.Abs(player.Position.x) > _arenaConfig.ArenaHalfExtents.x ||
                MathF.Abs(player.Position.y) > _arenaConfig.ArenaHalfExtents.y)
            {
                player.Alive = false;
                player.State = PlayerLifeState.Dead;
                player.Velocity = Zero;
                player.Input = Zero;
                player.DashDirection = Zero;
                player.DashRemaining = 0f;
                player.StunRemaining = 0f;
                player.PendingDash = false;
                player.RespawnRemaining = _respawnDelaySeconds;

                if (TryGetScoringPlayerLocked(player, out var scorer)) AdjustScoreLocked(scorer, 1);

                var penalty = Math.Max(1, (player.Score + 1) / 2);
                AdjustScoreLocked(player, -penalty);
                CaptureProfileLocked(player);

                _pendingDeaths.Add(new PlayerDead
                {
                    PlayerId = player.PlayerId,
                    Tick = _tick
                });

                if (player.IsBot && HumanPlayerCountLocked() >= TargetParticipantCount)
                {
                    botsToRemove.Add(player.PlayerId);
                }
            }
        }

        foreach (var botPlayerId in botsToRemove)
        {
            RemoveBotLocked(botPlayerId);
        }
    }

    private void ResolveMatchLifecycleLocked()
    {
        if (_restartAtTick is int restartTick && _tick >= restartTick)
        {
            ResetMatchLocked();
            return;
        }

        if (_winnerPlayerId is not null) return;

        if (_players.Values.Any(static p => !p.Alive)) return;

        var alivePlayers = _players.Values.Where(static p => p.Alive).ToArray();
        if (alivePlayers.Length == 0) return;

        if (_players.Count < MinPlayersToStart) return;

        if (alivePlayers.Length == 1)
        {
            _winnerPlayerId = alivePlayers[0].PlayerId;
            _restartAtTick = _tick + RestartDelayTicks;
            _pendingMatchEnd = new MatchEnd
            {
                WinnerPlayerId = _winnerPlayerId,
                Tick = _tick
            };
        }
    }

    private BroadcastBatch CreateBroadcastBatchLocked()
    {
        var callbacks = _players.Values
            .Where(static p => p.Connected && p.Callback is not null)
            .Select(static p => p.Callback!)
            .Distinct()
            .ToArray();

        var batch = new BroadcastBatch(
            callbacks,
            CreateWorldStateLocked(),
            _pendingDeaths.ToArray(),
            _pendingMatchEnd,
            _pendingScoreUpdates.ToArray());
        _pendingMatchEnd = null;
        _pendingScoreUpdates.Clear();
        return batch;
    }

    private WorldState CreateWorldStateLocked()
    {
        var state = new WorldState
        {
            Tick = _tick,
            RespawnDelaySeconds = _respawnDelaySecondsCeiling
        };

        foreach (var player in _players.Values.OrderBy(static p => p.PlayerId, StringComparer.Ordinal))
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
                Score = player.Score
            });

        return state;
    }

    private void PublishBatch(BroadcastBatch batch)
    {
        foreach (var callback in batch.Callbacks) SafeInvoke(callback, cb => cb.OnWorldState(batch.WorldState));

        foreach (var deadEvent in batch.Deaths)
        foreach (var callback in batch.Callbacks)
            SafeInvoke(callback, cb => cb.OnPlayerDead(deadEvent));

        if (batch.MatchEnd is not null)
            foreach (var callback in batch.Callbacks)
                SafeInvoke(callback, cb => cb.OnMatchEnd(batch.MatchEnd));

        foreach (var scoreUpdate in batch.ScoreUpdates) _ = PersistScoreAsync(scoreUpdate);
    }

    private void ResetMatchIfNeededLocked()
    {
        if (_players.Count >= MinPlayersToStart && _players.Values.All(static p => !p.Alive)) ResetMatchLocked();
    }

    private void ResetMatchLocked()
    {
        ClearMatchStateLocked();

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
            player.State = PlayerLifeState.Idle;
            player.LastTouchedByPlayerId = null;
            player.LastTouchedTick = 0;
        }
    }

    private void ClearMatchStateLocked()
    {
        _winnerPlayerId = null;
        _pendingMatchEnd = null;
        _restartAtTick = null;
    }

    private int ClaimSpawnIndexLocked(int preferredSpawnIndex)
    {
        if (preferredSpawnIndex >= 0 && _players.Values.All(player => player.SpawnIndex != preferredSpawnIndex))
            return preferredSpawnIndex;

        return GetNextSpawnIndexLocked();
    }

    private int GetNextSpawnIndexLocked()
    {
        var used = _players.Values
            .Select(static player => player.SpawnIndex)
            .ToHashSet();

        var index = 0;
        while (used.Contains(index)) index++;

        return index;
    }

    private bool TryGetScoringPlayerLocked(ConnectedPlayer eliminatedPlayer, out ConnectedPlayer scorer)
    {
        scorer = null!;
        var scoringPlayerId = eliminatedPlayer.LastTouchedByPlayerId;
        if (string.IsNullOrWhiteSpace(scoringPlayerId)) return false;

        if (_tick - eliminatedPlayer.LastTouchedTick > EliminationCreditWindowTicks) return false;

        if (!_players.TryGetValue(scoringPlayerId, out var candidate) ||
            string.Equals(candidate.PlayerId, eliminatedPlayer.PlayerId, StringComparison.Ordinal))
            return false;

        scorer = candidate;
        return true;
    }

    private void AdjustScoreLocked(ConnectedPlayer player, int delta)
    {
        player.Score = NormalizeScore(player.Score + delta);
        CaptureProfileLocked(player);
        if (!player.IsBot)
        {
            QueueScoreUpdateLocked(player.PlayerId, player.Score);
        }
    }

    private void CaptureProfileLocked(ConnectedPlayer player)
    {
        if (player.IsBot)
        {
            return;
        }

        if (!_profiles.TryGetValue(player.PlayerId, out var profile))
        {
            profile = new PlayerProfile();
            _profiles.Add(player.PlayerId, profile);
        }

        profile.SpawnIndex = player.SpawnIndex;
        profile.Score = NormalizeScore(player.Score);
    }

    private void QueueScoreUpdateLocked(string playerId, int score)
    {
        _pendingScoreUpdates.RemoveAll(update => string.Equals(update.PlayerId, playerId, StringComparison.Ordinal));
        _pendingScoreUpdates.Add(new ScoreUpdate(playerId, score));
    }

    private static async Task PersistScoreAsync(ScoreUpdate update)
    {
        try
        {
            var userGrain = ClusterClientRuntime.GrainFactory.GetGrain<IUserGrain>(update.PlayerId);
            await userGrain.SetScoreAsync(update.Score).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void RespawnPlayerLocked(ConnectedPlayer player)
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
        player.State = PlayerLifeState.Idle;
        player.LastTouchedByPlayerId = null;
        player.LastTouchedTick = 0;
    }

    private static PlayerLifeState GetLifeState(ConnectedPlayer player)
    {
        if (!player.Alive) return PlayerLifeState.Dead;

        if (player.StunRemaining > 0f) return PlayerLifeState.Stunned;

        if (player.DashRemaining > 0f) return PlayerLifeState.Dash;

        return LengthSquared(player.Input) > 0.001f ? PlayerLifeState.Move : PlayerLifeState.Idle;
    }

    private static int NormalizeScore(int score)
    {
        return Math.Max(1, score);
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
        var insetX = MathF.Max(0.5f, _arenaConfig.ArenaHalfExtents.x - _arenaConfig.RespawnInset);
        var insetY = MathF.Max(0.5f, _arenaConfig.ArenaHalfExtents.y - _arenaConfig.RespawnInset);
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

    private static void SafeInvoke(IPlayerCallback callback, Action<IPlayerCallback> action)
    {
        try
        {
            action(callback);
        }
        catch
        {
        }
    }

    private sealed record BroadcastBatch(
        IPlayerCallback[] Callbacks,
        WorldState WorldState,
        PlayerDead[] Deaths,
        MatchEnd? MatchEnd,
        ScoreUpdate[] ScoreUpdates);

    private sealed record ScoreUpdate(string PlayerId, int Score);

    private void UpdateBotInputsLocked()
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
                chase = new Vector2(MathF.Sin(_tick * 0.13f + bot.BotNumber), MathF.Cos(_tick * 0.11f + bot.BotNumber));
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
        var safeLimitX = MathF.Max(0.5f, _arenaConfig.ArenaHalfExtents.x - _arenaConfig.PlayerCollisionRadius);
        var safeLimitY = MathF.Max(0.5f, _arenaConfig.ArenaHalfExtents.y - _arenaConfig.PlayerCollisionRadius);

        var avoidance = Zero;
        avoidance.x = ComputeAxisAvoidance(position.x, safeLimitX);
        avoidance.y = ComputeAxisAvoidance(position.y, safeLimitY);

        var marginX = safeLimitX - MathF.Abs(position.x);
        var marginY = safeLimitY - MathF.Abs(position.y);
        var emergencyMargin = MathF.Min(marginX, marginY);

        if (emergencyMargin < BotEmergencyEdgeDistance)
        {
            var toCenter = -position;
            if (LengthSquared(toCenter) > 0.0001f)
            {
                avoidance += Normalize(toCenter) * 2.5f;
            }
        }

        return avoidance;
    }

    private static float ComputeAxisAvoidance(float coordinate, float safeLimit)
    {
        var distanceToEdge = safeLimit - MathF.Abs(coordinate);
        if (distanceToEdge >= BotEdgeAvoidDistance)
        {
            return 0f;
        }

        var directionToCenter = coordinate > 0f ? -1f : 1f;
        var pressure = 1f - Math.Clamp(distanceToEdge / BotEdgeAvoidDistance, 0f, 1f);
        return directionToCenter * pressure * pressure * 2f;
    }

    private void RebalanceBotsLocked()
    {
        var humanCount = HumanPlayerCountLocked();
        var desiredBotCount = Math.Max(0, TargetParticipantCount - humanCount);
        var currentBotCount = _players.Values.Count(static player => player.IsBot);

        while (currentBotCount < desiredBotCount)
        {
            AddBotLocked();
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

            RemoveBotLocked(removableBot.PlayerId);
            currentBotCount--;
        }
    }

    private int HumanPlayerCountLocked()
    {
        return _players.Values.Count(static player => !player.IsBot);
    }

    private void AddBotLocked()
    {
        var botName = $"{BotPrefix}{_nextBotNumber:D2}";
        while (_players.ContainsKey(botName))
        {
            _nextBotNumber++;
            botName = $"{BotPrefix}{_nextBotNumber:D2}";
        }

        var spawnIndex = GetNextSpawnIndexLocked();
        _players.Add(
            botName,
            new ConnectedPlayer(
                botName,
                callback: null,
                spawnIndex,
                GetSpawnPosition(spawnIndex),
                score: 1,
                isBot: true,
                botNumber: _nextBotNumber));
        _nextBotNumber++;
    }

    private void RemoveBotLocked(string playerId)
    {
        if (!_players.Remove(playerId, out var bot))
        {
            return;
        }

        ReleaseBotLocked(bot);
        _pendingDeaths.RemoveAll(deadEvent => string.Equals(deadEvent.PlayerId, playerId, StringComparison.Ordinal));
    }

    private void RemoveAllBotsLocked()
    {
        var botIds = _players.Values
            .Where(static player => player.IsBot)
            .Select(static player => player.PlayerId)
            .ToArray();

        foreach (var botId in botIds)
        {
            RemoveBotLocked(botId);
        }
    }

    private void ReleaseBotLocked(ConnectedPlayer bot)
    {
        _pendingDeaths.RemoveAll(deadEvent => string.Equals(deadEvent.PlayerId, bot.PlayerId, StringComparison.Ordinal));
    }

    private sealed class PlayerProfile
    {
        public int SpawnIndex { get; set; } = -1;
        public int Score { get; set; } = 1;
    }

    private sealed class ConnectedPlayer
    {
        public ConnectedPlayer(string playerId, IPlayerCallback? callback, int spawnIndex, Vector2 position, int score, bool isBot, int botNumber = 0)
        {
            PlayerId = playerId;
            Callback = callback;
            SpawnIndex = spawnIndex;
            Position = position;
            Score = NormalizeScore(score <= 0 ? 1 : score);
            Alive = true;
            Connected = true;
            IsBot = isBot;
            BotNumber = botNumber;
        }

        public string PlayerId { get; }
        public IPlayerCallback? Callback { get; set; }
        public int SpawnIndex { get; }
        public bool IsBot { get; }
        public int BotNumber { get; }
        public bool Connected { get; set; }
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
        public PlayerLifeState State { get; set; }
        public int Score { get; set; }
        public string? LastTouchedByPlayerId { get; set; }
        public int LastTouchedTick { get; set; }
    }
}

public sealed class GameArenaOptions
{
    public float RespawnDelaySeconds { get; set; } = 5f;
    public ArenaConfig Arena { get; set; } = ArenaConfig.CreateDefault();
}
