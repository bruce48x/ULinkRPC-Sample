using Microsoft.Extensions.DependencyInjection;
using Orleans.Contracts.Rooms;
using Orleans.Contracts.Sessions;
using Orleans.Contracts.Users;
using Server.Services;
using Shared.Gameplay;
using Shared.Interfaces;

namespace Server.Realtime;

internal sealed class RoomRuntime : IAsyncDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

    private readonly Lock _gate = new();
    private readonly SessionDirectory _sessionDirectory;
    private readonly IClusterClient _clusterClient;
    private readonly ArenaSimulation _simulation;
    private readonly string _roomId;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loopTask;
    private bool _matchCommitted;

    public RoomRuntime(RoomSnapshot room, IServiceProvider services)
    {
        _roomId = room.RoomId;
        _sessionDirectory = services.GetRequiredService<SessionDirectory>();
        _clusterClient = services.GetRequiredService<IClusterClient>();
        _simulation = new ArenaSimulation(new ArenaSimulationOptions
        {
            Arena = ArenaConfig.CreateDefault(),
            RespawnDelaySeconds = 5f,
            TargetParticipantCount = room.MaxPlayers,
            MinPlayersToStart = room.MaxPlayers,
            EnableBots = false
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
        }
    }

    private void PublishWorldState(ArenaStepResult result)
    {
        var registrations = _sessionDirectory.GetByRoom(_roomId);
        foreach (var registration in registrations)
        {
            SafeInvoke(registration.Callback, callback => callback.OnWorldState(result.WorldState));
        }

        foreach (var deadEvent in result.Deaths)
        {
            foreach (var registration in registrations)
            {
                SafeInvoke(registration.Callback, callback => callback.OnPlayerDead(deadEvent));
            }
        }

        if (result.MatchEnd is null)
        {
            return;
        }

        foreach (var registration in registrations)
        {
            SafeInvoke(registration.Callback, callback => callback.OnMatchEnd(result.MatchEnd));
        }
    }

    private async Task PersistMatchEndAsync(ArenaStepResult result)
    {
        await _clusterClient.GetGrain<IRoomGrain>(_roomId)
            .CompleteAsync(new RoomMatchCompletion
            {
                RoomId = _roomId,
                SettlementId = Guid.NewGuid().ToString("N"),
                FinishedAtUtc = DateTime.UtcNow,
                WinnerUserId = result.MatchEnd!.WinnerPlayerId,
                Reason = "Match finished",
                Results = result.WorldState.Players
                    .OrderByDescending(player => player.Score)
                    .Select((player, index) => new RoomSettlementEntry
                    {
                        UserId = player.PlayerId,
                        Rank = index + 1,
                        ScoreDelta = player.Score,
                        IsWinner = string.Equals(player.PlayerId, result.MatchEnd.WinnerPlayerId, StringComparison.Ordinal)
                    })
                    .ToList()
            })
            .ConfigureAwait(false);

        foreach (var player in result.WorldState.Players)
        {
            await _clusterClient.GetGrain<IUserGrain>(player.PlayerId)
                .AddScoreAsync(player.Score)
                .ConfigureAwait(false);
        }

        var registrations = _sessionDirectory.GetByRoom(_roomId);
        foreach (var registration in registrations)
        {
            _sessionDirectory.Remove(registration.PlayerId);
            await _clusterClient.GetGrain<IPlayerSessionGrain>(registration.PlayerId)
                .ClearRoomAsync(new PlayerRoomClearRequest
                {
                    UserId = registration.PlayerId,
                    RoomId = _roomId,
                    ClearedAtUtc = DateTime.UtcNow,
                    Reason = "Match finished"
                })
                .ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(result.MatchEnd.WinnerPlayerId))
        {
            await _clusterClient.GetGrain<IUserGrain>(result.MatchEnd.WinnerPlayerId).AddWinAsync().ConfigureAwait(false);
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
}
