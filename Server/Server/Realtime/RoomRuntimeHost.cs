using Microsoft.Extensions.DependencyInjection;
using Orleans.Contracts.Rooms;
using Orleans.Contracts.Sessions;
using Shared.Interfaces;

namespace Server.Realtime;

internal sealed class RoomRuntimeHost
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, RoomRuntime> _runtimes = new(StringComparer.Ordinal);
    private readonly IServiceProvider _services;

    public RoomRuntimeHost(IServiceProvider services)
    {
        _services = services;
    }

    public async Task EnsurePlayerJoinedAsync(RoomSnapshot room, string playerId)
    {
        RoomRuntime runtime;
        lock (_gate)
        {
            if (!_runtimes.TryGetValue(room.RoomId, out runtime!))
            {
                runtime = new RoomRuntime(room, _services);
                _runtimes.Add(room.RoomId, runtime);
            }
        }

        var roomGrain = _services.GetRequiredService<IClusterClient>().GetGrain<IRoomGrain>(room.RoomId);
        var player = room.Players.FirstOrDefault(entry => string.Equals(entry.UserId, playerId, StringComparison.Ordinal));
        if (player is not null)
        {
            await roomGrain.JoinAsync(new PlayerRoomAssignment
            {
                UserId = player.UserId,
                RoomId = room.RoomId,
                MatchId = room.MatchId,
                SeatIndex = player.SeatIndex,
                SessionToken = player.SessionToken,
                ConnectionId = player.ConnectionId,
                AssignedAtUtc = player.JoinedAtUtc
            }).ConfigureAwait(false);
        }

        await runtime.AddOrUpdatePlayerAsync(playerId).ConfigureAwait(false);
    }

    public async Task SubmitInputAsync(string roomId, string playerId, InputMessage input)
    {
        RoomRuntime? runtime;
        lock (_gate)
        {
            _runtimes.TryGetValue(roomId, out runtime);
        }

        if (runtime is not null)
        {
            await runtime.SubmitInputAsync(playerId, input).ConfigureAwait(false);
        }
    }

    public async Task RemovePlayerAsync(string roomId, string playerId)
    {
        RoomRuntime? runtime;
        lock (_gate)
        {
            _runtimes.TryGetValue(roomId, out runtime);
        }

        if (runtime is null)
        {
            return;
        }

        var shouldDispose = await runtime.RemovePlayerAsync(playerId).ConfigureAwait(false);
        if (!shouldDispose)
        {
            return;
        }

        lock (_gate)
        {
            _runtimes.Remove(roomId);
        }

        await runtime.DisposeAsync().ConfigureAwait(false);
    }
}
