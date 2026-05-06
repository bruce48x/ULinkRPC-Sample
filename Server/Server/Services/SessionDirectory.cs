using Shared.Interfaces;

namespace Server.Services;

internal sealed class SessionDirectory
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, SessionRegistration> _byPlayerId = new(StringComparer.Ordinal);

    public void Register(string playerId, string sessionToken, string connectionId, IPlayerCallback callback, bool preserveSessionState)
    {
        lock (_gate)
        {
            if (_byPlayerId.TryGetValue(playerId, out var registration))
            {
                registration.SessionToken = sessionToken;
                registration.ConnectionId = connectionId;
                registration.ControlCallback = callback;
                registration.ControlDisconnectedAtUtc = null;
                if (!preserveSessionState)
                {
                    registration.RealtimeConnectionId = null;
                    registration.RealtimeCallback = null;
                    registration.RoomId = null;
                    registration.MatchId = null;
                    registration.SeatIndex = -1;
                    registration.MatchmakingTicketId = null;
                }

                return;
            }

            _byPlayerId[playerId] = new SessionRegistration(playerId, sessionToken, connectionId, callback);
        }
    }

    public void MarkControlDisconnected(string playerId, string? connectionId, DateTime disconnectedAtUtc)
    {
        lock (_gate)
        {
            if (!_byPlayerId.TryGetValue(playerId, out var registration))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(connectionId) &&
                !string.Equals(registration.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                return;
            }

            registration.ConnectionId = string.Empty;
            registration.ControlCallback = null;
            registration.ControlDisconnectedAtUtc = disconnectedAtUtc;
        }
    }

    public void SetQueueTicket(string playerId, string? ticketId)
    {
        lock (_gate)
        {
            if (_byPlayerId.TryGetValue(playerId, out var registration))
            {
                registration.MatchmakingTicketId = string.IsNullOrWhiteSpace(ticketId) ? null : ticketId;
            }
        }
    }

    public void AssignRoom(string playerId, string roomId, string matchId, int seatIndex)
    {
        lock (_gate)
        {
            if (_byPlayerId.TryGetValue(playerId, out var registration))
            {
                registration.RoomId = roomId;
                registration.MatchId = matchId;
                registration.SeatIndex = seatIndex;
                registration.MatchmakingTicketId = null;
            }
        }
    }

    public bool AttachRealtime(string playerId, string sessionToken, string roomId, string matchId, string connectionId, IPlayerCallback callback)
    {
        lock (_gate)
        {
            if (!_byPlayerId.TryGetValue(playerId, out var registration))
            {
                registration = new SessionRegistration(playerId, sessionToken, connectionId, controlCallback: null)
                {
                    RoomId = roomId,
                    MatchId = matchId
                };
                _byPlayerId[playerId] = registration;
            }

            if (!string.Equals(registration.SessionToken, sessionToken, StringComparison.Ordinal))
            {
                return false;
            }

            registration.RoomId = roomId;
            registration.MatchId = matchId;
            registration.RealtimeConnectionId = connectionId;
            registration.RealtimeCallback = callback;
            return true;
        }
    }

    public void DetachRealtime(string playerId, string? connectionId = null)
    {
        lock (_gate)
        {
            if (!_byPlayerId.TryGetValue(playerId, out var registration))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(connectionId) &&
                !string.Equals(registration.RealtimeConnectionId, connectionId, StringComparison.Ordinal))
            {
                return;
            }

            registration.RealtimeConnectionId = null;
            registration.RealtimeCallback = null;
            if (registration.ControlCallback is null)
            {
                _byPlayerId.Remove(playerId);
            }
        }
    }

    public void ClearRoom(string playerId, string? expectedRoomId = null)
    {
        lock (_gate)
        {
            if (!_byPlayerId.TryGetValue(playerId, out var registration))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(expectedRoomId) &&
                !string.Equals(registration.RoomId, expectedRoomId, StringComparison.Ordinal))
            {
                return;
            }

            registration.RoomId = null;
            registration.MatchId = null;
            registration.SeatIndex = -1;
        }
    }

    public SessionRegistration? Get(string playerId)
    {
        lock (_gate)
        {
            return _byPlayerId.TryGetValue(playerId, out var registration)
                ? registration
                : null;
        }
    }

    public IReadOnlyList<SessionRegistration> GetMany(IEnumerable<string> playerIds)
    {
        lock (_gate)
        {
            return playerIds
                .Select(playerId => _byPlayerId.TryGetValue(playerId, out var registration) ? registration : null)
                .Where(static registration => registration is not null)
                .Cast<SessionRegistration>()
                .ToArray();
        }
    }

    public IReadOnlyList<SessionRegistration> GetByRoom(string roomId)
    {
        lock (_gate)
        {
            return _byPlayerId.Values
                .Where(static registration => !string.IsNullOrWhiteSpace(registration.RoomId))
                .Where(registration => string.Equals(registration.RoomId, roomId, StringComparison.Ordinal))
                .ToArray();
        }
    }

    public IReadOnlyList<SessionRegistration> GetExpiredControlDisconnects(DateTime nowUtc, TimeSpan gracePeriod)
    {
        lock (_gate)
        {
            return _byPlayerId.Values
                .Where(registration => registration.ControlCallback is null)
                .Where(registration => registration.ControlDisconnectedAtUtc is DateTime disconnectedAtUtc &&
                                       nowUtc - disconnectedAtUtc >= gracePeriod)
                .ToArray();
        }
    }

    public void Remove(string playerId)
    {
        lock (_gate)
        {
            _byPlayerId.Remove(playerId);
        }
    }
}
