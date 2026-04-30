using Orleans.Contracts.Matchmaking;
using Orleans.Contracts.Rooms;
using Orleans.Contracts.Sessions;
using Server.Realtime;
using Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace Server.Services;

internal sealed class GatewayMatchmakingService
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, CancellationTokenSource> _watchers = new(StringComparer.Ordinal);
    private readonly IClusterClient _clusterClient;
    private readonly SessionDirectory _sessionDirectory;
    private readonly MatchmakingMonitor _matchmakingMonitor;
    private readonly RoomRuntimeHost _roomRuntimeHost;
    private readonly GatewayNodeIdentity _gatewayNodeIdentity;
    private readonly ILogger<GatewayMatchmakingService> _logger;

    public GatewayMatchmakingService(
        IClusterClient clusterClient,
        SessionDirectory sessionDirectory,
        MatchmakingMonitor matchmakingMonitor,
        RoomRuntimeHost roomRuntimeHost,
        GatewayNodeIdentity gatewayNodeIdentity,
        ILogger<GatewayMatchmakingService> logger)
    {
        _clusterClient = clusterClient;
        _sessionDirectory = sessionDirectory;
        _matchmakingMonitor = matchmakingMonitor;
        _roomRuntimeHost = roomRuntimeHost;
        _gatewayNodeIdentity = gatewayNodeIdentity;
        _logger = logger;
    }

    public async Task EnqueueAsync(string playerId)
    {
        var registration = _sessionDirectory.Get(playerId)
            ?? throw new InvalidOperationException($"Player '{playerId}' is not registered.");

        var matchmaking = _clusterClient.GetGrain<IMatchmakingGrain>("default");
        var result = await matchmaking.EnqueueAsync(new MatchmakingEnqueueRequest
        {
            UserId = playerId,
            SessionToken = registration.SessionToken,
            EnqueuedAtUtc = DateTime.UtcNow
        }).ConfigureAwait(false);

        _sessionDirectory.SetQueueTicket(playerId, string.IsNullOrWhiteSpace(result.TicketId) ? null : result.TicketId);

        if (result.Matched)
        {
            StopWatcher(playerId);
            await PublishMatchedAsync(playerId, result.RoomAssignment).ConfigureAwait(false);
            return;
        }

        PublishQueued(registration, result);
        EnsureWatcher(playerId);
    }

    public async Task CancelAsync(string playerId, string reason)
    {
        StopWatcher(playerId);

        var registration = _sessionDirectory.Get(playerId);
        if (registration is null)
        {
            return;
        }

        var matchmaking = _clusterClient.GetGrain<IMatchmakingGrain>("default");
        await matchmaking.CancelAsync(new MatchmakingCancelRequest
        {
            UserId = playerId,
            TicketId = registration.MatchmakingTicketId ?? string.Empty,
            CancelledAtUtc = DateTime.UtcNow,
            Reason = reason
        }).ConfigureAwait(false);

        _sessionDirectory.SetQueueTicket(playerId, null);
        if (registration.ControlCallback is null)
        {
            return;
        }

        SafeInvoke(registration.ControlCallback, callback => callback.OnMatchmakingStatus(new MatchmakingStatusUpdate
        {
            State = Shared.Interfaces.MatchmakingState.Canceled,
            QueueSize = 0,
            RoomCapacity = 10,
            RoomId = string.Empty,
            MatchedPlayerCount = 0,
            Message = string.IsNullOrWhiteSpace(reason) ? "Matchmaking cancelled" : reason
        }));
    }

    public async Task ReleasePlayerAsync(string playerId, string reason)
    {
        StopWatcher(playerId);

        var registration = _sessionDirectory.Get(playerId);
        if (registration is not null && !string.IsNullOrWhiteSpace(registration.MatchmakingTicketId))
        {
            var matchmaking = _clusterClient.GetGrain<IMatchmakingGrain>("default");
            await matchmaking.CancelAsync(new MatchmakingCancelRequest
            {
                UserId = playerId,
                TicketId = registration.MatchmakingTicketId,
                CancelledAtUtc = DateTime.UtcNow,
                Reason = reason
            }).ConfigureAwait(false);
            _sessionDirectory.SetQueueTicket(playerId, null);
        }

        var roomId = registration?.RoomId;
        _sessionDirectory.ClearRoom(playerId);
        if (!string.IsNullOrWhiteSpace(roomId))
        {
            await _roomRuntimeHost.RemovePlayerAsync(roomId, playerId).ConfigureAwait(false);
        }
    }

    private void PublishQueued(SessionRegistration registration, MatchmakingEnqueueResult result)
    {
        if (registration.ControlCallback is null)
        {
            return;
        }

        SafeInvoke(registration.ControlCallback, callback => callback.OnMatchmakingStatus(new MatchmakingStatusUpdate
        {
            State = Shared.Interfaces.MatchmakingState.Queued,
            QueuePosition = result.QueuePosition,
            QueueSize = Math.Max(result.QueuePosition, 1),
            RoomCapacity = 10,
            RoomId = string.Empty,
            MatchedPlayerCount = 0,
            Message = string.IsNullOrWhiteSpace(result.Message) ? "Queued for matchmaking" : result.Message
        }));
    }

    private async Task PublishMatchedAsync(string playerId, RoomAssignment assignment)
    {
        var room = await _clusterClient.GetGrain<IRoomGrain>(assignment.RoomId)
            .GetSnapshotAsync()
            .ConfigureAwait(false);

        if (_gatewayNodeIdentity.IsRuntimeOwner(room.RuntimeGateway))
        {
            await _roomRuntimeHost.EnsureRoomReadyAsync(room).ConfigureAwait(false);
        }

        foreach (var player in room.Players)
        {
            var registration = _sessionDirectory.Get(player.UserId);
            if (registration is null)
            {
                continue;
            }

            _sessionDirectory.SetQueueTicket(player.UserId, null);
            _sessionDirectory.AssignRoom(player.UserId, room.RoomId, room.MatchId, player.SeatIndex);

            if (registration.ControlCallback is null)
            {
                continue;
            }

            SafeInvoke(registration.ControlCallback, callback => callback.OnMatchmakingStatus(new MatchmakingStatusUpdate
            {
                State = Shared.Interfaces.MatchmakingState.Matched,
                QueueSize = room.MemberCount > 0 ? room.MemberCount : room.Players.Count,
                RoomCapacity = room.MaxPlayers,
                RoomId = room.RoomId,
                MatchedPlayerCount = room.Players.Count,
                Message = $"Matched into room {room.RoomId}",
                RealtimeConnection = GatewayEndpointMapper.ToRealtimeConnectionInfo(
                    assignment.RuntimeGateway,
                    room.RoomId,
                    room.MatchId,
                    player.SessionToken)
            }));
        }
    }

    private void EnsureWatcher(string playerId)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_watchers.ContainsKey(playerId))
            {
                return;
            }

            cts = new CancellationTokenSource();
            _watchers.Add(playerId, cts);
        }

        _ = WatchForAssignmentAsync(playerId, cts);
    }

    private async Task WatchForAssignmentAsync(string playerId, CancellationTokenSource cts)
    {
        try
        {
            await _matchmakingMonitor.WatchForRoomAssignmentAsync(playerId, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed while watching matchmaking assignment for player {PlayerId}.", playerId);
        }
        finally
        {
            lock (_gate)
            {
                if (_watchers.TryGetValue(playerId, out var current) && ReferenceEquals(current, cts))
                {
                    _watchers.Remove(playerId);
                }
            }

            cts.Dispose();
        }
    }

    private void StopWatcher(string playerId)
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            if (!_watchers.TryGetValue(playerId, out cts))
            {
                return;
            }

            _watchers.Remove(playerId);
        }

        cts.Cancel();
        cts.Dispose();
    }

    private void SafeInvoke(IPlayerCallback callback, Action<IPlayerCallback> action)
    {
        try
        {
            action(callback);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push matchmaking callback.");
        }
    }
}
