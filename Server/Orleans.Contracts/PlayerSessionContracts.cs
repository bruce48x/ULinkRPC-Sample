using Orleans;
using Orleans.Contracts;

namespace Orleans.Contracts.Sessions;

public interface IPlayerSessionGrain : IGrainWithStringKey
{
    Task<PlayerSessionSnapshot> AttachAsync(PlayerSessionAttachRequest request);
    Task<PlayerSessionSnapshot> ReconnectAsync(PlayerSessionReconnectRequest request);
    Task<PlayerSessionSnapshot> MarkQueuedAsync(PlayerSessionQueueRequest request);
    Task<PlayerSessionSnapshot> ClearQueueAsync(PlayerSessionQueueClearRequest request);
    Task<PlayerSessionSnapshot> AssignRoomAsync(PlayerRoomAssignment request);
    Task<PlayerSessionSnapshot> ClearRoomAsync(PlayerRoomClearRequest request);
    Task<PlayerSessionSnapshot> MarkDisconnectedAsync(PlayerSessionDisconnectRequest request);
    Task<PlayerSessionSnapshot> HeartbeatAsync(PlayerSessionHeartbeatRequest request);
    Task<PlayerSessionSnapshot> GetSnapshotAsync();
}

[GenerateSerializer]
public sealed class PlayerSessionReconnectRequest
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public string SessionToken { get; set; } = "";

    [Id(2)]
    public string ConnectionId { get; set; } = "";

    [Id(3)]
    public DateTime ReconnectedAtUtc { get; set; }

    [Id(4)]
    public GatewayEndpointDescriptor ControlGateway { get; set; } = new();
}

[GenerateSerializer]
public sealed class PlayerSessionAttachRequest
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public string SessionToken { get; set; } = "";

    [Id(2)]
    public string ConnectionId { get; set; } = "";

    [Id(3)]
    public DateTime AttachedAtUtc { get; set; }

    [Id(4)]
    public GatewayEndpointDescriptor ControlGateway { get; set; } = new();
}

[GenerateSerializer]
public sealed class PlayerSessionQueueRequest
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public string QueueId { get; set; } = "";

    [Id(2)]
    public string TicketId { get; set; } = "";

    [Id(3)]
    public DateTime QueuedAtUtc { get; set; }
}

[GenerateSerializer]
public sealed class PlayerSessionQueueClearRequest
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public string QueueId { get; set; } = "";

    [Id(2)]
    public string TicketId { get; set; } = "";

    [Id(3)]
    public DateTime ClearedAtUtc { get; set; }

    [Id(4)]
    public string Reason { get; set; } = "";
}

[GenerateSerializer]
public sealed class PlayerRoomAssignment
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public string RoomId { get; set; } = "";

    [Id(2)]
    public string MatchId { get; set; } = "";

    [Id(3)]
    public int SeatIndex { get; set; } = -1;

    [Id(4)]
    public string SessionToken { get; set; } = "";

    [Id(5)]
    public string ConnectionId { get; set; } = "";

    [Id(6)]
    public DateTime AssignedAtUtc { get; set; }

    [Id(7)]
    public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
}

[GenerateSerializer]
public sealed class PlayerRoomClearRequest
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public string RoomId { get; set; } = "";

    [Id(2)]
    public DateTime ClearedAtUtc { get; set; }

    [Id(3)]
    public string Reason { get; set; } = "";
}

[GenerateSerializer]
public sealed class PlayerSessionDisconnectRequest
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public string ConnectionId { get; set; } = "";

    [Id(2)]
    public DateTime DisconnectedAtUtc { get; set; }

    [Id(3)]
    public string Reason { get; set; } = "";
}

[GenerateSerializer]
public sealed class PlayerSessionHeartbeatRequest
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public DateTime ObservedAtUtc { get; set; }
}

[GenerateSerializer]
public sealed class PlayerSessionSnapshot
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public string SessionToken { get; set; } = "";

    [Id(2)]
    public string ConnectionId { get; set; } = "";

    [Id(3)]
    public bool IsOnline { get; set; }

    [Id(4)]
    public bool IsQueued { get; set; }

    [Id(5)]
    public string QueueId { get; set; } = "";

    [Id(6)]
    public string MatchmakingTicketId { get; set; } = "";

    [Id(7)]
    public string CurrentRoomId { get; set; } = "";

    [Id(8)]
    public string CurrentMatchId { get; set; } = "";

    [Id(9)]
    public int SeatIndex { get; set; } = -1;

    [Id(10)]
    public DateTime AttachedAtUtc { get; set; }

    [Id(11)]
    public DateTime LastQueuedAtUtc { get; set; }

    [Id(12)]
    public DateTime LastConnectedAtUtc { get; set; }

    [Id(13)]
    public DateTime LastDisconnectedAtUtc { get; set; }

    [Id(14)]
    public DateTime LastHeartbeatAtUtc { get; set; }

    [Id(15)]
    public string ReconnectToken { get; set; } = "";

    [Id(16)]
    public GatewayEndpointDescriptor ControlGateway { get; set; } = new();

    [Id(17)]
    public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
}

[GenerateSerializer]
public sealed class PlayerSessionState
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public string SessionToken { get; set; } = "";

    [Id(2)]
    public string ConnectionId { get; set; } = "";

    [Id(3)]
    public bool IsOnline { get; set; }

    [Id(4)]
    public bool IsQueued { get; set; }

    [Id(5)]
    public string QueueId { get; set; } = "";

    [Id(6)]
    public string MatchmakingTicketId { get; set; } = "";

    [Id(7)]
    public string CurrentRoomId { get; set; } = "";

    [Id(8)]
    public string CurrentMatchId { get; set; } = "";

    [Id(9)]
    public int SeatIndex { get; set; } = -1;

    [Id(10)]
    public DateTime AttachedAtUtc { get; set; }

    [Id(11)]
    public DateTime LastQueuedAtUtc { get; set; }

    [Id(12)]
    public DateTime LastConnectedAtUtc { get; set; }

    [Id(13)]
    public DateTime LastDisconnectedAtUtc { get; set; }

    [Id(14)]
    public DateTime LastHeartbeatAtUtc { get; set; }

    [Id(15)]
    public string ReconnectToken { get; set; } = "";

    [Id(16)]
    public GatewayEndpointDescriptor ControlGateway { get; set; } = new();

    [Id(17)]
    public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
}
