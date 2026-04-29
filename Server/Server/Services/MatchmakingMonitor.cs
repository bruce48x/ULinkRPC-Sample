using Orleans.Contracts.Matchmaking;
using Orleans.Contracts.Rooms;
using Orleans.Contracts.Sessions;
using Server.Realtime;
using Shared.Interfaces;

namespace Server.Services;

internal sealed class MatchmakingMonitor
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly IClusterClient _clusterClient;
    private readonly SessionDirectory _sessionDirectory;
    private readonly RoomRuntimeHost _roomRuntimeHost;

    public MatchmakingMonitor(
        IClusterClient clusterClient,
        SessionDirectory sessionDirectory,
        RoomRuntimeHost roomRuntimeHost)
    {
        _clusterClient = clusterClient;
        _sessionDirectory = sessionDirectory;
        _roomRuntimeHost = roomRuntimeHost;
    }

    public async Task WatchForRoomAssignmentAsync(string playerId, CancellationToken cancellationToken)
    {
        var sessionGrain = _clusterClient.GetGrain<IPlayerSessionGrain>(playerId);
        var matchmaking = _clusterClient.GetGrain<IMatchmakingGrain>("default");

        while (!cancellationToken.IsCancellationRequested)
        {
            var snapshot = await sessionGrain.GetSnapshotAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(snapshot.CurrentRoomId))
            {
                _sessionDirectory.AssignRoom(playerId, snapshot.CurrentRoomId);

                var room = await _clusterClient.GetGrain<IRoomGrain>(snapshot.CurrentRoomId)
                    .GetSnapshotAsync()
                    .ConfigureAwait(false);
                await _roomRuntimeHost.EnsurePlayerJoinedAsync(room, playerId).ConfigureAwait(false);

                var registration = _sessionDirectory.Get(playerId);
                if (registration is not null)
                {
                    SafeInvoke(registration.Callback, callback => callback.OnMatchmakingStatus(new MatchmakingStatusUpdate
                    {
                        State = Shared.Interfaces.MatchmakingState.Matched,
                        QueueSize = room.MemberCount,
                        RoomCapacity = room.MaxPlayers,
                        RoomId = room.RoomId,
                        MatchedPlayerCount = room.MemberCount,
                        Message = $"Matched into room {room.RoomId}"
                    }));
                }

                return;
            }

            var status = await matchmaking.GetStatusAsync().ConfigureAwait(false);
            var position = status.PendingTickets.FindIndex(ticket => string.Equals(ticket.UserId, playerId, StringComparison.Ordinal));

            var callbackRegistration = _sessionDirectory.Get(playerId);
            if (callbackRegistration is not null)
            {
                SafeInvoke(callbackRegistration.Callback, callback => callback.OnMatchmakingStatus(new MatchmakingStatusUpdate
                {
                    State = position >= 0 ? Shared.Interfaces.MatchmakingState.Queued : Shared.Interfaces.MatchmakingState.Searching,
                    QueuePosition = position >= 0 ? position + 1 : 0,
                    QueueSize = status.QueuedCount,
                    RoomCapacity = status.DefaultRoomSize,
                    RoomId = status.LastRoomId,
                    MatchedPlayerCount = Math.Min(status.QueuedCount, status.DefaultRoomSize),
                    Message = position >= 0 ? "Queued for matchmaking" : "Waiting for room assignment"
                }));
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
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
