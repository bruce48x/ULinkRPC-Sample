using Shared.Interfaces;

namespace Server.Services;

internal sealed class SessionRegistration
{
    public SessionRegistration(string playerId, IPlayerCallback callback)
    {
        PlayerId = playerId;
        Callback = callback;
    }

    public string PlayerId { get; }
    public IPlayerCallback Callback { get; }
    public string? RoomId { get; set; }
}
