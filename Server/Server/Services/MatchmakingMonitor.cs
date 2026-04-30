using Orleans.Contracts.Matchmaking;
using Orleans.Contracts.Rooms;
using Orleans.Contracts.Sessions;
using Server.Realtime;
using Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace Server.Services;

internal sealed class MatchmakingMonitor
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly IClusterClient _clusterClient;
    private readonly SessionDirectory _sessionDirectory;
    private readonly RoomRuntimeHost _roomRuntimeHost;
    private readonly ILogger<MatchmakingMonitor> _logger;

    public MatchmakingMonitor(
        IClusterClient clusterClient,
        SessionDirectory sessionDirectory,
        RoomRuntimeHost roomRuntimeHost,
        ILogger<MatchmakingMonitor> logger)
    {
        _clusterClient = clusterClient;
        _sessionDirectory = sessionDirectory;
        _roomRuntimeHost = roomRuntimeHost;
        _logger = logger;
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
                var room = await _clusterClient.GetGrain<IRoomGrain>(snapshot.CurrentRoomId)
                    .GetSnapshotAsync()
                    .ConfigureAwait(false);
                var player = room.Players.FirstOrDefault(entry => string.Equals(entry.UserId, playerId, StringComparison.Ordinal));
                _sessionDirectory.AssignRoom(
                    playerId,
                    snapshot.CurrentRoomId,
                    snapshot.CurrentMatchId,
                    player?.SeatIndex ?? -1);

                await _roomRuntimeHost.EnsureRoomReadyAsync(room).ConfigureAwait(false);

                var registration = _sessionDirectory.Get(playerId);
                if (registration is not null)
                {
                    SafeInvoke(registration.ControlCallback, callback => callback.OnMatchmakingStatus(new MatchmakingStatusUpdate
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
                SafeInvoke(callbackRegistration.ControlCallback, callback => callback.OnMatchmakingStatus(new MatchmakingStatusUpdate
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

    private void SafeInvoke(IPlayerCallback callback, Action<IPlayerCallback> action)
    {
        try
        {
            action(callback);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push monitor matchmaking callback.");
        }
    }
}
