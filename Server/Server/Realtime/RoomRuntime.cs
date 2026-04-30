using Microsoft.Extensions.DependencyInjection;
using Orleans.Contracts.Rooms;
using Orleans.Contracts.Users;
using Server.Services;
using Shared.Gameplay;
using Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace Server.Realtime;

internal sealed class RoomRuntime : IAsyncDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

    private readonly Lock _gate = new();
    private readonly SessionDirectory _sessionDirectory;
    private readonly ArenaSimulation _simulation;
    private readonly string _roomId;
    private readonly ILogger<RoomRuntime> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loopTask;
    private bool _matchCommitted;

    public RoomRuntime(RoomSnapshot room, IServiceProvider services)
    {
        _roomId = room.RoomId;
        _sessionDirectory = services.GetRequiredService<SessionDirectory>();
        _logger = services.GetRequiredService<ILogger<RoomRuntime>>();
        _simulation = new ArenaSimulation(new ArenaSimulationOptions
        {
            Arena = ArenaConfig.CreateDefault(),
            RespawnDelaySeconds = 5f,
            TargetParticipantCount = room.MaxPlayers,
            MinPlayersToStart = room.MaxPlayers,
            EnableBots = true
        });

        foreach (var player in room.Players)
        {
            _simulation.UpsertPlayer(new ArenaPlayerRegistration
            {
                PlayerId = player.UserId,
                Score = Math.Max(0, player.Score),
                PreferredSpawnIndex = player.SeatIndex,
                IsBot = false
            });
        }

        _loopTask = RunAsync(_cts.Token);
    }

    public ValueTask AddOrUpdatePlayerAsync(string playerId)
    {
        lock (_gate)
        {
            if (!_simulation.TryGetPlayerSnapshot(playerId, out _))
            {
                _simulation.UpsertPlayer(new ArenaPlayerRegistration
                {
                    PlayerId = playerId,
                    Score = 0,
                    PreferredSpawnIndex = -1,
                    IsBot = false
                });
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask SubmitInputAsync(string playerId, InputMessage input)
    {
        lock (_gate)
        {
            input.PlayerId = playerId;
            _simulation.SubmitInput(input);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> RemovePlayerAsync(string playerId)
    {
        lock (_gate)
        {
            _simulation.RemovePlayer(playerId, out _);
            var remaining = _sessionDirectory.GetByRoom(_roomId)
                .Count(registration => !string.Equals(registration.PlayerId, playerId, StringComparison.Ordinal));
            return ValueTask.FromResult(remaining == 0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await _loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Room runtime {RoomId} dispose cancelled.", _roomId);
        }
        _cts.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                ArenaStepResult result;

                lock (_gate)
                {
                    result = _simulation.Tick((float)TickInterval.TotalSeconds);
                }

                PublishWorldState(result);

                if (result.MatchEnd is not null && !_matchCommitted)
                {
                    _matchCommitted = true;
                    await PersistMatchEndAsync(result).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Room runtime loop stopped for room {RoomId}.", _roomId);
        }
    }

    private void PublishWorldState(ArenaStepResult result)
    {
        var registrations = _sessionDirectory.GetByRoom(_roomId);
        foreach (var registration in registrations)
        {
            var callback = registration.GetRealtimePreferredCallback();
            if (callback is not null)
            {
                SafeInvoke(callback, target => target.OnWorldState(result.WorldState));
            }
        }

        foreach (var deadEvent in result.Deaths)
        {
            foreach (var registration in registrations)
            {
                var callback = registration.GetRealtimePreferredCallback();
                if (callback is not null)
                {
                    SafeInvoke(callback, target => target.OnPlayerDead(deadEvent));
                }
            }
        }

        if (result.MatchEnd is null)
        {
            return;
        }

        foreach (var registration in registrations)
        {
            var callback = registration.GetRealtimePreferredCallback();
            if (callback is not null)
            {
                SafeInvoke(callback, target => target.OnMatchEnd(result.MatchEnd));
            }
        }
    }

    private async Task PersistMatchEndAsync(ArenaStepResult result)
    {
        var clusterClient = Server.Runtime.ServerRuntime.GetRequiredService<IClusterClient>();

        foreach (var player in result.WorldState.Players)
        {
            await clusterClient.GetGrain<IUserGrain>(player.PlayerId)
                .AddScoreAsync(player.Score)
                .ConfigureAwait(false);
        }

        var registrations = _sessionDirectory.GetByRoom(_roomId);
        foreach (var registration in registrations)
        {
            _sessionDirectory.ClearRoom(registration.PlayerId, _roomId);
        }

        var winnerPlayerId = result.MatchEnd?.WinnerPlayerId;
        if (!string.IsNullOrWhiteSpace(winnerPlayerId))
        {
            await clusterClient.GetGrain<IUserGrain>(winnerPlayerId).AddWinAsync().ConfigureAwait(false);
        }
    }

    private void SafeInvoke(IPlayerCallback callback, Action<IPlayerCallback> action)
    {
        try
        {
            action(callback);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push room event in room {RoomId}.", _roomId);
        }
    }
}
