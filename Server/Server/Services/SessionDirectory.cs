using Shared.Interfaces;

namespace Server.Services;

internal sealed class SessionDirectory
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, SessionRegistration> _byPlayerId = new(StringComparer.Ordinal);

    public void Register(string playerId, IPlayerCallback callback)
    {
        lock (_gate)
        {
            _byPlayerId[playerId] = new SessionRegistration(playerId, callback);
        }
    }

    public void AssignRoom(string playerId, string roomId)
    {
        lock (_gate)
        {
            if (_byPlayerId.TryGetValue(playerId, out var registration))
            {
                registration.RoomId = roomId;
            }
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

    public void Remove(string playerId)
    {
        lock (_gate)
        {
            _byPlayerId.Remove(playerId);
        }
    }
}
