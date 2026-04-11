using Orleans.Contracts.Users;
using Server.Orleans;
using Shared.Gameplay;
using Shared.Interfaces;

namespace Server.Services;

public sealed class GameArenaRuntime
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

    private readonly object _gate = new();
    private readonly Dictionary<string, ConnectedPlayer> _connections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlayerProfile> _profiles = new(StringComparer.Ordinal);
    private readonly ArenaSimulation _simulation;

    public GameArenaRuntime(GameArenaOptions options)
    {
        _simulation = new ArenaSimulation(new ArenaSimulationOptions
        {
            Arena = options.Arena ?? ArenaConfig.CreateDefault(),
            RespawnDelaySeconds = options.RespawnDelaySeconds
        });
    }

    public ValueTask<LoginReply> RegisterPlayerAsync(UserLoginResult loginResult, IPlayerCallback callback)
    {
        WorldState snapshot;

        lock (_gate)
        {
            if (!_profiles.TryGetValue(loginResult.UserId, out var profile))
            {
                profile = new PlayerProfile
                {
                    Score = 0,
                    SpawnIndex = -1
                };
                _profiles.Add(loginResult.UserId, profile);
            }
            else
            {
                profile.Score = NormalizeScore(profile.Score);
            }

            if (!_simulation.TryGetPlayerSnapshot(loginResult.UserId, out _))
            {
                _simulation.UpsertPlayer(new ArenaPlayerRegistration
                {
                    PlayerId = loginResult.UserId,
                    Score = profile.Score,
                    PreferredSpawnIndex = profile.SpawnIndex
                });
            }

            _connections[loginResult.UserId] = new ConnectedPlayer(loginResult.UserId, callback);
            snapshot = _simulation.CreateWorldState();
        }

        SafeInvoke(callback, cb => cb.OnWorldState(snapshot));

        return ValueTask.FromResult(new LoginReply
        {
            Code = 0,
            Token = loginResult.SessionToken,
            PlayerId = loginResult.UserId,
            WinCount = loginResult.WinCount
        });
    }

    public void SubmitInput(InputMessage input)
    {
        lock (_gate)
        {
            _simulation.SubmitInput(input);
        }
    }

    public ValueTask UnregisterPlayerAsync(string playerId)
    {
        IPlayerCallback[] callbacks;
        WorldState snapshot;

        lock (_gate)
        {
            if (!_connections.Remove(playerId))
            {
                return ValueTask.CompletedTask;
            }

            if (_simulation.RemovePlayer(playerId, out var playerSnapshot) && !playerSnapshot.IsBot)
            {
                CaptureProfile(playerSnapshot);
            }

            callbacks = GetCallbacksLocked();
            snapshot = _simulation.CreateWorldState();
        }

        foreach (var callback in callbacks)
        {
            SafeInvoke(callback, cb => cb.OnWorldState(snapshot));
        }

        return ValueTask.CompletedTask;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                ArenaStepResult result;
                IPlayerCallback[] callbacks;

                lock (_gate)
                {
                    result = _simulation.Tick((float)TickInterval.TotalSeconds);
                    callbacks = GetCallbacksLocked();
                }

                PublishBatch(callbacks, result);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private IPlayerCallback[] GetCallbacksLocked()
    {
        return _connections.Values
            .Where(static connection => connection.Connected && connection.Callback is not null)
            .Select(static connection => connection.Callback!)
            .Distinct()
            .ToArray();
    }

    private void PublishBatch(IPlayerCallback[] callbacks, ArenaStepResult result)
    {
        foreach (var callback in callbacks)
        {
            SafeInvoke(callback, cb => cb.OnWorldState(result.WorldState));
        }

        foreach (var deadEvent in result.Deaths)
        {
            foreach (var callback in callbacks)
            {
                SafeInvoke(callback, cb => cb.OnPlayerDead(deadEvent));
            }
        }

        if (result.MatchEnd is not null)
        {
            foreach (var callback in callbacks)
            {
                SafeInvoke(callback, cb => cb.OnMatchEnd(result.MatchEnd));
            }
        }

        if (result.MatchEnd is not null && _profiles.ContainsKey(result.MatchEnd.WinnerPlayerId))
        {
            _ = PersistWinAsync(result.MatchEnd.WinnerPlayerId);
        }
    }

    private void CaptureProfile(ArenaPlayerSnapshot snapshot)
    {
        if (!_profiles.TryGetValue(snapshot.PlayerId, out var profile))
        {
            profile = new PlayerProfile();
            _profiles.Add(snapshot.PlayerId, profile);
        }

        profile.SpawnIndex = snapshot.SpawnIndex;
        profile.Score = NormalizeScore(snapshot.Score);
    }

    private static int NormalizeScore(int score)
    {
        return Math.Max(0, score);
    }

    private static async Task PersistWinAsync(string playerId)
    {
        try
        {
            var userGrain = ClusterClientRuntime.GrainFactory.GetGrain<IUserGrain>(playerId);
            await userGrain.AddWinAsync().ConfigureAwait(false);
        }
        catch
        {
        }
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

    private sealed class PlayerProfile
    {
        public int SpawnIndex { get; set; } = -1;
        public int Score { get; set; }
    }

    private sealed class ConnectedPlayer
    {
        public ConnectedPlayer(string playerId, IPlayerCallback callback)
        {
            PlayerId = playerId;
            Callback = callback;
            Connected = true;
        }

        public string PlayerId { get; }
        public IPlayerCallback? Callback { get; set; }
        public bool Connected { get; set; }
    }
}

public sealed class GameArenaOptions
{
    public float RespawnDelaySeconds { get; set; } = 5f;
    public ArenaConfig Arena { get; set; } = ArenaConfig.CreateDefault();
}
