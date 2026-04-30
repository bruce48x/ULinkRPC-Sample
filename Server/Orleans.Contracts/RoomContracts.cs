using Orleans;
using Orleans.Contracts;
using Orleans.Contracts.Sessions;

namespace Orleans.Contracts.Rooms;

public interface IRoomGrain : IGrainWithStringKey
{
    Task<RoomSettlementResult> CreateAsync(RoomCreateRequest request);
    Task<RoomSettlementResult> JoinAsync(PlayerRoomAssignment request);
    Task<RoomSettlementResult> LeaveAsync(RoomPlayerLeaveRequest request);
    Task<RoomSettlementResult> SetReadyAsync(RoomPlayerReadyRequest request);
    Task<RoomSettlementResult> StartAsync(RoomStartRequest request);
    Task<RoomSettlementResult> CompleteAsync(RoomMatchCompletion request);
    Task<RoomSnapshot> GetSnapshotAsync();
}

[GenerateSerializer]
public sealed class RoomCreateRequest
{
    [Id(0)]
    public string RoomId { get; set; } = "";

    [Id(1)]
    public string MatchId { get; set; } = "";

    [Id(2)]
    public string CreatedByUserId { get; set; } = "";

    [Id(3)]
    public DateTime CreatedAtUtc { get; set; }

    [Id(4)]
    public int MaxPlayers { get; set; } = 10;

    [Id(5)]
    public List<PlayerRoomAssignment> Players { get; set; } = [];

    [Id(6)]
    public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
}

[GenerateSerializer]
public sealed class RoomPlayerLeaveRequest
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public string RoomId { get; set; } = "";

    [Id(2)]
    public DateTime LeftAtUtc { get; set; }

    [Id(3)]
    public string Reason { get; set; } = "";
}

[GenerateSerializer]
public sealed class RoomPlayerReadyRequest
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public string RoomId { get; set; } = "";

    [Id(2)]
    public bool IsReady { get; set; }

    [Id(3)]
    public DateTime UpdatedAtUtc { get; set; }
}

[GenerateSerializer]
public sealed class RoomStartRequest
{
    [Id(0)]
    public string StartedByUserId { get; set; } = "";

    [Id(1)]
    public string RoomId { get; set; } = "";

    [Id(2)]
    public DateTime StartedAtUtc { get; set; }
}

[GenerateSerializer]
public sealed class RoomMatchCompletion
{
    [Id(0)]
    public string RoomId { get; set; } = "";

    [Id(1)]
    public string SettlementId { get; set; } = "";

    [Id(2)]
    public DateTime FinishedAtUtc { get; set; }

    [Id(3)]
    public string WinnerUserId { get; set; } = "";

    [Id(4)]
    public string Reason { get; set; } = "";

    [Id(5)]
    public List<RoomSettlementEntry> Results { get; set; } = [];
}

[GenerateSerializer]
public sealed class RoomSettlementEntry
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public int Rank { get; set; }

    [Id(2)]
    public int ScoreDelta { get; set; }

    [Id(3)]
    public bool IsWinner { get; set; }
}

[GenerateSerializer]
public sealed class RoomSettlementResult
{
    [Id(0)]
    public string RoomId { get; set; } = "";

    [Id(1)]
    public string SettlementId { get; set; } = "";

    [Id(2)]
    public bool Succeeded { get; set; }

    [Id(3)]
    public bool AlreadyApplied { get; set; }

    [Id(4)]
    public string WinnerUserId { get; set; } = "";

    [Id(5)]
    public string Message { get; set; } = "";

    [Id(6)]
    public DateTime UpdatedAtUtc { get; set; }

    [Id(7)]
    public RoomSnapshot Snapshot { get; set; } = new();
}

[GenerateSerializer]
public sealed class RoomSnapshot
{
    [Id(0)]
    public string RoomId { get; set; } = "";

    [Id(1)]
    public string MatchId { get; set; } = "";

    [Id(2)]
    public RoomStatus Status { get; set; } = RoomStatus.Created;

    [Id(3)]
    public int MaxPlayers { get; set; } = 10;

    [Id(4)]
    public DateTime CreatedAtUtc { get; set; }

    [Id(5)]
    public DateTime StartedAtUtc { get; set; }

    [Id(6)]
    public DateTime FinishedAtUtc { get; set; }

    [Id(7)]
    public long Revision { get; set; }

    [Id(8)]
    public List<RoomPlayerSnapshot> Players { get; set; } = [];

    [Id(9)]
    public string WinnerUserId { get; set; } = "";

    [Id(10)]
    public string SettlementId { get; set; } = "";

    [Id(11)]
    public DateTime LastUpdatedAtUtc { get; set; }

    [Id(12)]
    public string Message { get; set; } = "";

    [Id(13)]
    public int MemberCount { get; set; }

    [Id(14)]
    public int ConnectedCount { get; set; }

    [Id(15)]
    public int ReadyCount { get; set; }

    [Id(16)]
    public int CapacityRemaining { get; set; }

    [Id(17)]
    public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
}

[GenerateSerializer]
public sealed class RoomPlayerSnapshot
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public string SessionToken { get; set; } = "";

    [Id(2)]
    public string ConnectionId { get; set; } = "";

    [Id(3)]
    public int SeatIndex { get; set; } = -1;

    [Id(4)]
    public bool IsReady { get; set; }

    [Id(5)]
    public bool IsConnected { get; set; }

    [Id(6)]
    public DateTime JoinedAtUtc { get; set; }

    [Id(7)]
    public DateTime LastSeenAtUtc { get; set; }

    [Id(8)]
    public DateTime LeftAtUtc { get; set; }

    [Id(9)]
    public string LeaveReason { get; set; } = "";

    [Id(10)]
    public int Score { get; set; }

    [Id(11)]
    public int Rank { get; set; }
}

[GenerateSerializer]
public sealed class RoomState
{
    [Id(0)]
    public string RoomId { get; set; } = "";

    [Id(1)]
    public string MatchId { get; set; } = "";

    [Id(2)]
    public RoomStatus Status { get; set; } = RoomStatus.Created;

    [Id(3)]
    public int MaxPlayers { get; set; } = 10;

    [Id(4)]
    public DateTime CreatedAtUtc { get; set; }

    [Id(5)]
    public DateTime StartedAtUtc { get; set; }

    [Id(6)]
    public DateTime FinishedAtUtc { get; set; }

    [Id(7)]
    public long Revision { get; set; }

    [Id(8)]
    public List<RoomPlayerState> Players { get; set; } = [];

    [Id(9)]
    public string WinnerUserId { get; set; } = "";

    [Id(10)]
    public string SettlementId { get; set; } = "";

    [Id(11)]
    public DateTime LastUpdatedAtUtc { get; set; }

    [Id(12)]
    public string Message { get; set; } = "";

    [Id(13)]
    public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
}

[GenerateSerializer]
public sealed class RoomPlayerState
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public string SessionToken { get; set; } = "";

    [Id(2)]
    public string ConnectionId { get; set; } = "";

    [Id(3)]
    public int SeatIndex { get; set; } = -1;

    [Id(4)]
    public bool IsReady { get; set; }

    [Id(5)]
    public bool IsConnected { get; set; }

    [Id(6)]
    public DateTime JoinedAtUtc { get; set; }

    [Id(7)]
    public DateTime LastSeenAtUtc { get; set; }

    [Id(8)]
    public DateTime LeftAtUtc { get; set; }

    [Id(9)]
    public string LeaveReason { get; set; } = "";

    [Id(10)]
    public int Score { get; set; }

    [Id(11)]
    public int Rank { get; set; }
}

public enum RoomStatus
{
    Created = 0,
    WaitingForPlayers = 1,
    InProgress = 2,
    Finished = 3,
    Cancelled = 4
}
