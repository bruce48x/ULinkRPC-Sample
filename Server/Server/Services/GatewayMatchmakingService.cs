using Orleans.Contracts.Rooms;
using Shared.Interfaces;
using Server.Realtime;
using Microsoft.Extensions.Logging;

namespace Server.Services;

internal sealed class GatewayMatchmakingService
{
    private const int DefaultRoomSize = 10;
    private static readonly TimeSpan MaxFrontQueueWait = TimeSpan.FromSeconds(5);

    private readonly Lock _gate = new();
    private readonly List<QueueEntry> _queue = [];
    private readonly SessionDirectory _sessionDirectory;
    private readonly RoomRuntimeHost _roomRuntimeHost;
    private readonly GatewayRealtimeOptions _realtimeOptions;
    private readonly ILogger<GatewayMatchmakingService> _logger;

    public GatewayMatchmakingService(
        SessionDirectory sessionDirectory,
        RoomRuntimeHost roomRuntimeHost,
        GatewayRealtimeOptions realtimeOptions,
        ILogger<GatewayMatchmakingService> logger)
    {
        _sessionDirectory = sessionDirectory;
        _roomRuntimeHost = roomRuntimeHost;
        _realtimeOptions = realtimeOptions;
        _logger = logger;
    }

    public async Task EnqueueAsync(string playerId)
    {
        List<QueuedStatusDispatch> queuedDispatches;
        List<MatchedStatusDispatch> matchedDispatches;

        lock (_gate)
        {
            var registration = _sessionDirectory.Get(playerId)
                ?? throw new InvalidOperationException($"Player '{playerId}' is not registered.");

            if (!string.IsNullOrWhiteSpace(registration.RoomId))
            {
                queuedDispatches = [];
                matchedDispatches = [CreateMatchedDispatch(registration, GetRoomRegistrations(registration.RoomId))];
            }
            else
            {
                var existing = _queue.FindIndex(entry => string.Equals(entry.PlayerId, playerId, StringComparison.Ordinal));
                if (existing < 0)
                {
                    var ticketId = Guid.NewGuid().ToString("N");
                    _queue.Add(new QueueEntry(ticketId, playerId, DateTime.UtcNow));
                    _sessionDirectory.SetQueueTicket(playerId, ticketId);
                }

                matchedDispatches = CollectReadyMatchesLocked(DateTime.UtcNow);
                queuedDispatches = BuildQueuedDispatchesLocked();
            }
        }

        PublishQueuedDispatches(queuedDispatches);
        await PublishMatchedDispatchesAsync(matchedDispatches).ConfigureAwait(false);
    }

    public async Task TryDispatchReadyMatchesAsync()
    {
        List<QueuedStatusDispatch> queuedDispatches;
        List<MatchedStatusDispatch> matchedDispatches;

        lock (_gate)
        {
            matchedDispatches = CollectReadyMatchesLocked(DateTime.UtcNow);
            if (matchedDispatches.Count == 0)
            {
                return;
            }

            queuedDispatches = BuildQueuedDispatchesLocked();
        }

        PublishQueuedDispatches(queuedDispatches);
        await PublishMatchedDispatchesAsync(matchedDispatches).ConfigureAwait(false);
    }

    public Task CancelAsync(string playerId, string reason)
    {
        List<QueuedStatusDispatch> queuedDispatches;
        SessionRegistration? registration;

        lock (_gate)
        {
            var index = _queue.FindIndex(entry => string.Equals(entry.PlayerId, playerId, StringComparison.Ordinal));
            if (index >= 0)
            {
                _queue.RemoveAt(index);
                _sessionDirectory.SetQueueTicket(playerId, null);
            }

            registration = _sessionDirectory.Get(playerId);
            queuedDispatches = BuildQueuedDispatchesLocked();
        }

        if (registration is not null)
        {
            SafeInvoke(registration.ControlCallback, callback => callback.OnMatchmakingStatus(new MatchmakingStatusUpdate
            {
                State = MatchmakingState.Canceled,
                QueueSize = 0,
                RoomCapacity = DefaultRoomSize,
                RoomId = string.Empty,
                MatchedPlayerCount = 0,
                Message = string.IsNullOrWhiteSpace(reason) ? "Matchmaking cancelled" : reason
            }));
        }

        PublishQueuedDispatches(queuedDispatches);
        return Task.CompletedTask;
    }

    public async Task ReleasePlayerAsync(string playerId, string reason)
    {
        string? roomId;

        lock (_gate)
        {
            var queueIndex = _queue.FindIndex(entry => string.Equals(entry.PlayerId, playerId, StringComparison.Ordinal));
            if (queueIndex >= 0)
            {
                _queue.RemoveAt(queueIndex);
                _sessionDirectory.SetQueueTicket(playerId, null);
            }

            roomId = _sessionDirectory.Get(playerId)?.RoomId;
            _sessionDirectory.ClearRoom(playerId);
        }

        if (!string.IsNullOrWhiteSpace(roomId))
        {
            await _roomRuntimeHost.RemovePlayerAsync(roomId, playerId).ConfigureAwait(false);
        }
    }

    private List<MatchedStatusDispatch> CollectReadyMatchesLocked(DateTime nowUtc)
    {
        var dispatches = new List<MatchedStatusDispatch>();

        while (_queue.Count >= DefaultRoomSize)
        {
            dispatches.Add(CreateMatchedDispatchLocked(_queue.Take(DefaultRoomSize).ToArray(), nowUtc));
        }

        if (_queue.Count > 0 && nowUtc - _queue[0].EnqueuedAtUtc >= MaxFrontQueueWait)
        {
            dispatches.Add(CreateMatchedDispatchLocked(_queue.ToArray(), nowUtc));
        }

        return dispatches;
    }

    private MatchedStatusDispatch CreateMatchedDispatchLocked(QueueEntry[] batch, DateTime nowUtc)
    {
        var roomId = $"room-{Guid.NewGuid():N}";
        var matchId = $"match-{Guid.NewGuid():N}";
        _queue.RemoveRange(0, batch.Length);

        var room = new RoomSnapshot
        {
            RoomId = roomId,
            MatchId = matchId,
            Status = RoomStatus.InProgress,
            MaxPlayers = DefaultRoomSize,
            CreatedAtUtc = nowUtc,
            StartedAtUtc = nowUtc,
            LastUpdatedAtUtc = nowUtc,
            Players = []
        };

        foreach (var (entry, seatIndex) in batch.Select((entry, seatIndex) => (entry, seatIndex)))
        {
            _sessionDirectory.SetQueueTicket(entry.PlayerId, null);
            _sessionDirectory.AssignRoom(entry.PlayerId, roomId, matchId, seatIndex);
            var registration = _sessionDirectory.Get(entry.PlayerId);
            if (registration is null)
            {
                continue;
            }

            room.Players.Add(new RoomPlayerSnapshot
            {
                UserId = entry.PlayerId,
                SessionToken = registration.SessionToken,
                ConnectionId = registration.ConnectionId,
                SeatIndex = seatIndex,
                IsConnected = true,
                JoinedAtUtc = nowUtc,
                LastSeenAtUtc = nowUtc
            });
        }

        var registrations = GetRoomRegistrations(roomId);
        return new MatchedStatusDispatch(room, registrations);
    }

    private List<QueuedStatusDispatch> BuildQueuedDispatchesLocked()
    {
        var dispatches = new List<QueuedStatusDispatch>(_queue.Count);
        for (var index = 0; index < _queue.Count; index++)
        {
            var entry = _queue[index];
            var registration = _sessionDirectory.Get(entry.PlayerId);
            if (registration is null)
            {
                continue;
            }

            dispatches.Add(new QueuedStatusDispatch(
                registration,
                new MatchmakingStatusUpdate
                {
                    State = MatchmakingState.Queued,
                    QueuePosition = index + 1,
                    QueueSize = _queue.Count,
                    RoomCapacity = DefaultRoomSize,
                    RoomId = string.Empty,
                    MatchedPlayerCount = Math.Min(_queue.Count, DefaultRoomSize),
                    Message = "Queued for matchmaking"
                }));
        }

        return dispatches;
    }

    private MatchedStatusDispatch CreateMatchedDispatch(SessionRegistration registration, IReadOnlyList<SessionRegistration> roomRegistrations)
    {
        return new MatchedStatusDispatch(
            BuildRoomSnapshot(registration.RoomId!, registration.MatchId!, roomRegistrations),
            roomRegistrations);
    }

    private RoomSnapshot BuildRoomSnapshot(string roomId, string matchId, IReadOnlyList<SessionRegistration> roomRegistrations)
    {
        var nowUtc = DateTime.UtcNow;
        var snapshot = new RoomSnapshot
        {
            RoomId = roomId,
            MatchId = matchId,
            Status = RoomStatus.InProgress,
            MaxPlayers = DefaultRoomSize,
            CreatedAtUtc = nowUtc,
            StartedAtUtc = nowUtc,
            LastUpdatedAtUtc = nowUtc,
            Players = []
        };

        foreach (var registration in roomRegistrations)
        {
            snapshot.Players.Add(new RoomPlayerSnapshot
            {
                UserId = registration.PlayerId,
                SessionToken = registration.SessionToken,
                ConnectionId = registration.ConnectionId,
                SeatIndex = registration.SeatIndex,
                IsConnected = true,
                JoinedAtUtc = nowUtc,
                LastSeenAtUtc = nowUtc
            });
        }

        return snapshot;
    }

    private IReadOnlyList<SessionRegistration> GetRoomRegistrations(string roomId)
    {
        return _sessionDirectory.GetByRoom(roomId)
            .OrderBy(registration => registration.SeatIndex)
            .ToArray();
    }

    private void PublishQueuedDispatches(IEnumerable<QueuedStatusDispatch> dispatches)
    {
        foreach (var dispatch in dispatches)
        {
            SafeInvoke(dispatch.Registration.ControlCallback, callback => callback.OnMatchmakingStatus(dispatch.Status));
        }
    }

    private async Task PublishMatchedDispatchesAsync(IEnumerable<MatchedStatusDispatch> dispatches)
    {
        foreach (var dispatch in dispatches)
        {
            await _roomRuntimeHost.EnsureRoomReadyAsync(dispatch.Room).ConfigureAwait(false);

            foreach (var registration in dispatch.Registrations)
            {
                SafeInvoke(registration.ControlCallback, callback => callback.OnMatchmakingStatus(new MatchmakingStatusUpdate
                {
                    State = MatchmakingState.Matched,
                    QueueSize = dispatch.Room.MemberCount > 0 ? dispatch.Room.MemberCount : dispatch.Room.Players.Count,
                    RoomCapacity = dispatch.Room.MaxPlayers,
                    RoomId = dispatch.Room.RoomId,
                    MatchedPlayerCount = dispatch.Room.Players.Count,
                    Message = $"Matched into room {dispatch.Room.RoomId}",
                    RealtimeConnection = new RealtimeConnectionInfo
                    {
                        Transport = _realtimeOptions.Transport,
                        Host = _realtimeOptions.Host,
                        Port = _realtimeOptions.Port,
                        Path = _realtimeOptions.Path,
                        RoomId = dispatch.Room.RoomId,
                        MatchId = dispatch.Room.MatchId,
                        SessionToken = registration.SessionToken
                    }
                }));
            }
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
            _logger.LogWarning(ex, "Failed to push matchmaking callback.");
        }
    }

    private readonly record struct QueueEntry(string TicketId, string PlayerId, DateTime EnqueuedAtUtc);

    private readonly record struct QueuedStatusDispatch(SessionRegistration Registration, MatchmakingStatusUpdate Status);

    private readonly record struct MatchedStatusDispatch(RoomSnapshot Room, IReadOnlyList<SessionRegistration> Registrations);
}
