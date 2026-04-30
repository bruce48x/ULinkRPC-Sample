using Orleans;
using Orleans.Contracts;
using Orleans.Contracts.Sessions;
using Orleans.Contracts.Rooms;

namespace Orleans.Contracts.Matchmaking;

public interface IMatchmakingGrain : IGrainWithStringKey
{
    Task<MatchmakingEnqueueResult> EnqueueAsync(MatchmakingEnqueueRequest request);
    Task<MatchmakingCancelResult> CancelAsync(MatchmakingCancelRequest request);
    Task TickAsync(MatchmakingTickRequest request);
    Task<MatchmakingStatusSnapshot> GetStatusAsync();
}

[GenerateSerializer]
public sealed class MatchmakingEnqueueRequest
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public string SessionToken { get; set; } = "";

    [Id(2)]
    public DateTime EnqueuedAtUtc { get; set; }

    [Id(3)]
    public int Priority { get; set; }
}

[GenerateSerializer]
public sealed class MatchmakingCancelRequest
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public string TicketId { get; set; } = "";

    [Id(2)]
    public DateTime CancelledAtUtc { get; set; }

    [Id(3)]
    public string Reason { get; set; } = "";
}

[GenerateSerializer]
public sealed class MatchmakingTickRequest
{
    [Id(0)]
    public DateTime ObservedAtUtc { get; set; }
}

[GenerateSerializer]
public sealed class MatchmakingEnqueueResult
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public string TicketId { get; set; } = "";

    [Id(2)]
    public bool Queued { get; set; }

    [Id(3)]
    public bool Matched { get; set; }

    [Id(4)]
    public int QueuePosition { get; set; } = -1;

    [Id(5)]
    public string Message { get; set; } = "";

    [Id(6)]
    public DateTime UpdatedAtUtc { get; set; }

    [Id(7)]
    public RoomAssignment RoomAssignment { get; set; } = new();
}

[GenerateSerializer]
public sealed class MatchmakingCancelResult
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public string TicketId { get; set; } = "";

    [Id(2)]
    public bool Cancelled { get; set; }

    [Id(3)]
    public int QueuePosition { get; set; } = -1;

    [Id(4)]
    public string Message { get; set; } = "";

    [Id(5)]
    public DateTime UpdatedAtUtc { get; set; }
}

[GenerateSerializer]
public sealed class MatchmakingStatusSnapshot
{
    [Id(0)]
    public string QueueId { get; set; } = "";

    [Id(1)]
    public int DefaultRoomSize { get; set; } = 10;

    [Id(2)]
    public int QueuedCount { get; set; }

    [Id(3)]
    public string LastMatchId { get; set; } = "";

    [Id(4)]
    public string LastRoomId { get; set; } = "";

    [Id(5)]
    public DateTime LastUpdatedAtUtc { get; set; }

    [Id(6)]
    public List<MatchmakingQueueTicket> PendingTickets { get; set; } = [];
}

[GenerateSerializer]
public sealed class MatchmakingQueueTicket
{
    [Id(0)]
    public string TicketId { get; set; } = "";

    [Id(1)]
    public string UserId { get; set; } = "";

    [Id(2)]
    public string SessionToken { get; set; } = "";

    [Id(3)]
    public DateTime EnqueuedAtUtc { get; set; }

    [Id(4)]
    public string QueueId { get; set; } = "";

    [Id(5)]
    public int Priority { get; set; }
}

[GenerateSerializer]
public sealed class MatchmakingState
{
    [Id(0)]
    public string QueueId { get; set; } = "";

    [Id(1)]
    public int DefaultRoomSize { get; set; } = 10;

    [Id(2)]
    public List<MatchmakingQueueTicket> PendingTickets { get; set; } = [];

    [Id(3)]
    public string LastMatchId { get; set; } = "";

    [Id(4)]
    public string LastRoomId { get; set; } = "";

    [Id(5)]
    public DateTime LastUpdatedAtUtc { get; set; }
}

[GenerateSerializer]
public sealed class RoomAssignment
{
    [Id(0)]
    public string RoomId { get; set; } = "";

    [Id(1)]
    public string MatchId { get; set; } = "";

    [Id(2)]
    public DateTime AssignedAtUtc { get; set; }

    [Id(3)]
    public List<PlayerRoomAssignment> Players { get; set; } = [];

    [Id(4)]
    public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
}
