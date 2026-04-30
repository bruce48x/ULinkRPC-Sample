using Shared.Interfaces;

namespace Server.Services;

internal sealed class SessionRegistration
{
    public SessionRegistration(string playerId, string sessionToken, string connectionId, IPlayerCallback? controlCallback)
    {
        PlayerId = playerId;
        SessionToken = sessionToken;
        ConnectionId = connectionId;
        ControlCallback = controlCallback;
    }

    public string PlayerId { get; }
    public string SessionToken { get; set; }
    public string ConnectionId { get; set; }
    public IPlayerCallback? ControlCallback { get; set; }
    public IPlayerCallback? RealtimeCallback { get; set; }
    public string? RealtimeConnectionId { get; set; }
    public string? RoomId { get; set; }
    public string? MatchId { get; set; }
    public int SeatIndex { get; set; } = -1;
    public string? MatchmakingTicketId { get; set; }

    public IPlayerCallback? GetRealtimePreferredCallback()
    {
        return RealtimeCallback ?? ControlCallback;
    }
}
