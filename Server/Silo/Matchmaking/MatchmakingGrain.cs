using Orleans;
using Orleans.Contracts;
using Orleans.Contracts.Matchmaking;
using Orleans.Contracts.Rooms;
using Orleans.Contracts.Sessions;
using Orleans.Runtime;

namespace ULinkRPC.Sample.Silo.Matchmaking;

public sealed class MatchmakingGrain : Grain, IMatchmakingGrain
{
    private const int DefaultRoomSize = 10;
    private static readonly TimeSpan MaxFrontQueueWait = TimeSpan.FromSeconds(5);
    private readonly IPersistentState<MatchmakingState> _state;

    public MatchmakingGrain([PersistentState("matchmaking", "matchmaking")] IPersistentState<MatchmakingState> state)
    {
        _state = state;
    }

    public async Task<MatchmakingEnqueueResult> EnqueueAsync(MatchmakingEnqueueRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var enqueuedAtUtc = NormalizeUtc(request.EnqueuedAtUtc);
        EnsureState();

        var sessionGrain = GrainFactory.GetGrain<IPlayerSessionGrain>(userId);
        var sessionSnapshot = await sessionGrain.GetSnapshotAsync();
        if (!string.IsNullOrWhiteSpace(sessionSnapshot.CurrentRoomId))
        {
            return new MatchmakingEnqueueResult
            {
                UserId = userId,
                Queued = false,
                Matched = true,
                Message = "Player is already assigned to a room.",
                UpdatedAtUtc = enqueuedAtUtc,
                RoomAssignment = BuildRoomAssignmentFromSession(sessionSnapshot, enqueuedAtUtc)
            };
        }

        var existingTicket = _state.State.PendingTickets.FirstOrDefault(ticket => string.Equals(ticket.UserId, userId, StringComparison.Ordinal));
        if (existingTicket is not null)
        {
            return new MatchmakingEnqueueResult
            {
                UserId = userId,
                TicketId = existingTicket.TicketId,
                Queued = true,
                Matched = false,
                QueuePosition = GetQueuePosition(existingTicket.TicketId),
                Message = "Player is already queued.",
                UpdatedAtUtc = enqueuedAtUtc
            };
        }

        var ticket = new MatchmakingQueueTicket
        {
            TicketId = Guid.NewGuid().ToString("N"),
            UserId = userId,
            SessionToken = request.SessionToken,
            EnqueuedAtUtc = enqueuedAtUtc,
            QueueId = _state.State.QueueId,
            Priority = request.Priority
        };

        _state.State.PendingTickets.Add(ticket);
        SortQueue();
        _state.State.LastUpdatedAtUtc = enqueuedAtUtc;
        await _state.WriteStateAsync();

        await sessionGrain.MarkQueuedAsync(new PlayerSessionQueueRequest
        {
            UserId = userId,
            QueueId = _state.State.QueueId,
            TicketId = ticket.TicketId,
            QueuedAtUtc = enqueuedAtUtc
        });

        var assignments = await TryMatchAsync(enqueuedAtUtc);
        if (assignments.TryGetValue(userId, out var roomAssignment))
        {
            return new MatchmakingEnqueueResult
            {
                UserId = userId,
                TicketId = ticket.TicketId,
                Queued = false,
                Matched = true,
                Message = "Matched to a room.",
                UpdatedAtUtc = enqueuedAtUtc,
                RoomAssignment = roomAssignment
            };
        }

        return new MatchmakingEnqueueResult
        {
            UserId = userId,
            TicketId = ticket.TicketId,
            Queued = true,
            Matched = false,
            QueuePosition = GetQueuePosition(ticket.TicketId),
            Message = "Queued for matchmaking.",
            UpdatedAtUtc = enqueuedAtUtc
        };
    }

    public async Task<MatchmakingCancelResult> CancelAsync(MatchmakingCancelRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var cancelledAtUtc = NormalizeUtc(request.CancelledAtUtc);
        EnsureState();

        var index = FindTicketIndex(request.TicketId, userId);
        if (index < 0)
        {
            return new MatchmakingCancelResult
            {
                UserId = userId,
                TicketId = request.TicketId,
                Cancelled = false,
                Message = "No queued ticket was found.",
                UpdatedAtUtc = cancelledAtUtc
            };
        }

        var ticket = _state.State.PendingTickets[index];
        _state.State.PendingTickets.RemoveAt(index);
        _state.State.LastUpdatedAtUtc = cancelledAtUtc;
        await _state.WriteStateAsync();

        var sessionGrain = GrainFactory.GetGrain<IPlayerSessionGrain>(userId);
        await sessionGrain.ClearQueueAsync(new PlayerSessionQueueClearRequest
        {
            UserId = userId,
            QueueId = ticket.QueueId,
            TicketId = ticket.TicketId,
            ClearedAtUtc = cancelledAtUtc,
            Reason = request.Reason
        });

        return new MatchmakingCancelResult
        {
            UserId = userId,
            TicketId = ticket.TicketId,
            Cancelled = true,
            QueuePosition = index + 1,
            Message = "Matchmaking cancelled.",
            UpdatedAtUtc = cancelledAtUtc
        };
    }

    public Task<MatchmakingStatusSnapshot> GetStatusAsync()
    {
        EnsureState();
        return Task.FromResult(new MatchmakingStatusSnapshot
        {
            QueueId = _state.State.QueueId,
            DefaultRoomSize = _state.State.DefaultRoomSize,
            QueuedCount = _state.State.PendingTickets.Count,
            LastMatchId = _state.State.LastMatchId,
            LastRoomId = _state.State.LastRoomId,
            LastUpdatedAtUtc = _state.State.LastUpdatedAtUtc,
            PendingTickets = _state.State.PendingTickets.Select(CloneTicket).ToList()
        });
    }

    public async Task TickAsync(MatchmakingTickRequest request)
    {
        EnsureState();
        if (_state.State.PendingTickets.Count == 0)
        {
            return;
        }

        var observedAtUtc = NormalizeUtc(request.ObservedAtUtc);
        var roomSize = Math.Clamp(_state.State.DefaultRoomSize <= 0 ? DefaultRoomSize : _state.State.DefaultRoomSize, 1, DefaultRoomSize);
        var waitedLongEnough = observedAtUtc - _state.State.PendingTickets[0].EnqueuedAtUtc >= MaxFrontQueueWait;
        if (_state.State.PendingTickets.Count < roomSize && !waitedLongEnough)
        {
            return;
        }

        await TryMatchAsync(observedAtUtc).ConfigureAwait(false);
    }

    private async Task<Dictionary<string, RoomAssignment>> TryMatchAsync(DateTime nowUtc)
    {
        var assignments = new Dictionary<string, RoomAssignment>(StringComparer.Ordinal);
        var roomSize = Math.Clamp(_state.State.DefaultRoomSize <= 0 ? DefaultRoomSize : _state.State.DefaultRoomSize, 1, DefaultRoomSize);

        while (_state.State.PendingTickets.Count >= roomSize)
        {
            var batch = _state.State.PendingTickets.Take(roomSize).ToList();
            _state.State.PendingTickets.RemoveRange(0, roomSize);

            var roomId = $"room-{Guid.NewGuid():N}";
            var matchId = $"match-{Guid.NewGuid():N}";
            var runtimeGateway = await ResolveRuntimeGatewayAsync(batch).ConfigureAwait(false);
            var playerAssignments = batch.Select((ticket, seatIndex) => new PlayerRoomAssignment
            {
                UserId = ticket.UserId,
                RoomId = roomId,
                MatchId = matchId,
                SeatIndex = seatIndex,
                SessionToken = ticket.SessionToken,
                ConnectionId = "",
                AssignedAtUtc = nowUtc,
                RuntimeGateway = CloneGateway(runtimeGateway)
            }).ToList();

            var roomGrain = GrainFactory.GetGrain<IRoomGrain>(roomId);
            var createResult = await roomGrain.CreateAsync(new RoomCreateRequest
            {
                RoomId = roomId,
                MatchId = matchId,
                CreatedByUserId = batch[0].UserId,
                CreatedAtUtc = nowUtc,
                MaxPlayers = roomSize,
                Players = playerAssignments.Select(CloneAssignment).ToList(),
                RuntimeGateway = CloneGateway(runtimeGateway)
            });

            if (!createResult.Succeeded)
            {
                _state.State.PendingTickets.InsertRange(0, batch);
                break;
            }

            await roomGrain.StartAsync(new RoomStartRequest
            {
                RoomId = roomId,
                StartedByUserId = batch[0].UserId,
                StartedAtUtc = nowUtc
            });

            foreach (var playerAssignment in playerAssignments)
            {
                var sessionGrain = GrainFactory.GetGrain<IPlayerSessionGrain>(playerAssignment.UserId);
                await sessionGrain.AssignRoomAsync(playerAssignment);
            }

            var roomAssignment = new RoomAssignment
            {
                RoomId = roomId,
                MatchId = matchId,
                AssignedAtUtc = nowUtc,
                Players = playerAssignments.Select(CloneAssignment).ToList(),
                RuntimeGateway = CloneGateway(runtimeGateway)
            };

            foreach (var playerAssignment in playerAssignments)
            {
                assignments[playerAssignment.UserId] = roomAssignment;
            }

            _state.State.LastMatchId = matchId;
            _state.State.LastRoomId = roomId;
            _state.State.LastUpdatedAtUtc = nowUtc;
        }

        await _state.WriteStateAsync();
        return assignments;
    }

    private void EnsureState()
    {
        if (_state.RecordExists)
        {
            if (string.IsNullOrWhiteSpace(_state.State.QueueId))
            {
                _state.State.QueueId = this.GetPrimaryKeyString();
            }

            if (_state.State.DefaultRoomSize <= 0)
            {
                _state.State.DefaultRoomSize = DefaultRoomSize;
            }

            return;
        }

        _state.State = new MatchmakingState
        {
            QueueId = this.GetPrimaryKeyString(),
            DefaultRoomSize = DefaultRoomSize,
            LastUpdatedAtUtc = DateTime.UtcNow
        };
    }

    private int GetQueuePosition(string ticketId)
    {
        var index = _state.State.PendingTickets.FindIndex(ticket => string.Equals(ticket.TicketId, ticketId, StringComparison.Ordinal));
        return index < 0 ? -1 : index + 1;
    }

    private int FindTicketIndex(string ticketId, string userId)
    {
        if (!string.IsNullOrWhiteSpace(ticketId))
        {
            var byTicket = _state.State.PendingTickets.FindIndex(ticket => string.Equals(ticket.TicketId, ticketId, StringComparison.Ordinal));
            if (byTicket >= 0)
            {
                return byTicket;
            }
        }

        return _state.State.PendingTickets.FindIndex(ticket => string.Equals(ticket.UserId, userId, StringComparison.Ordinal));
    }

    private void SortQueue()
    {
        _state.State.PendingTickets = _state.State.PendingTickets
            .OrderByDescending(ticket => ticket.Priority)
            .ThenBy(ticket => ticket.EnqueuedAtUtc)
            .ThenBy(ticket => ticket.TicketId, StringComparer.Ordinal)
            .ToList();
    }

    private static MatchmakingQueueTicket CloneTicket(MatchmakingQueueTicket ticket)
    {
        return new MatchmakingQueueTicket
        {
            TicketId = ticket.TicketId,
            UserId = ticket.UserId,
            SessionToken = ticket.SessionToken,
            EnqueuedAtUtc = ticket.EnqueuedAtUtc,
            QueueId = ticket.QueueId,
            Priority = ticket.Priority
        };
    }

    private static PlayerRoomAssignment CloneAssignment(PlayerRoomAssignment assignment)
    {
        return new PlayerRoomAssignment
        {
            UserId = assignment.UserId,
            RoomId = assignment.RoomId,
            MatchId = assignment.MatchId,
            SeatIndex = assignment.SeatIndex,
            SessionToken = assignment.SessionToken,
            ConnectionId = assignment.ConnectionId,
            AssignedAtUtc = assignment.AssignedAtUtc,
            RuntimeGateway = CloneGateway(assignment.RuntimeGateway)
        };
    }

    private static RoomAssignment BuildRoomAssignmentFromSession(PlayerSessionSnapshot sessionSnapshot, DateTime assignedAtUtc)
    {
        return new RoomAssignment
        {
            RoomId = sessionSnapshot.CurrentRoomId,
            MatchId = sessionSnapshot.CurrentMatchId,
            AssignedAtUtc = assignedAtUtc,
            RuntimeGateway = CloneGateway(sessionSnapshot.RuntimeGateway),
            Players = new List<PlayerRoomAssignment>
            {
                new()
                {
                    UserId = sessionSnapshot.UserId,
                    RoomId = sessionSnapshot.CurrentRoomId,
                    MatchId = sessionSnapshot.CurrentMatchId,
                    SeatIndex = sessionSnapshot.SeatIndex,
                    SessionToken = sessionSnapshot.SessionToken,
                    ConnectionId = sessionSnapshot.ConnectionId,
                    AssignedAtUtc = assignedAtUtc,
                    RuntimeGateway = CloneGateway(sessionSnapshot.RuntimeGateway)
                }
            }
        };
    }

    private static string NormalizeUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        return userId;
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value == default ? DateTime.UtcNow : value;
    }

    private async Task<GatewayEndpointDescriptor> ResolveRuntimeGatewayAsync(IReadOnlyList<MatchmakingQueueTicket> batch)
    {
        foreach (var ticket in batch)
        {
            var snapshot = await GrainFactory.GetGrain<IPlayerSessionGrain>(ticket.UserId)
                .GetSnapshotAsync()
                .ConfigureAwait(false);
            if (HasValidGateway(snapshot.ControlGateway))
            {
                return CloneGateway(snapshot.ControlGateway);
            }
        }

        return new GatewayEndpointDescriptor();
    }

    private static bool HasValidGateway(GatewayEndpointDescriptor? gateway)
    {
        return gateway is not null
            && !string.IsNullOrWhiteSpace(gateway.InstanceId)
            && !string.IsNullOrWhiteSpace(gateway.Host)
            && gateway.Port > 0;
    }

    private static GatewayEndpointDescriptor CloneGateway(GatewayEndpointDescriptor? gateway)
    {
        if (gateway is null)
        {
            return new GatewayEndpointDescriptor();
        }

        return new GatewayEndpointDescriptor
        {
            InstanceId = gateway.InstanceId,
            Transport = gateway.Transport,
            Host = gateway.Host,
            Port = gateway.Port,
            Path = gateway.Path
        };
    }
}
