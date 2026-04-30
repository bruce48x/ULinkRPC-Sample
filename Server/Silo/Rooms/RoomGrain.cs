using Orleans;
using Orleans.Contracts;
using Orleans.Contracts.Rooms;
using Orleans.Contracts.Sessions;
using Orleans.Runtime;

namespace ULinkRPC.Sample.Silo.Rooms;

public sealed class RoomGrain : Grain, IRoomGrain
{
    private readonly IPersistentState<RoomState> _state;

    public RoomGrain([PersistentState("room", "rooms")] IPersistentState<RoomState> state)
    {
        _state = state;
    }

    public async Task<RoomSettlementResult> CreateAsync(RoomCreateRequest request)
    {
        var roomId = NormalizeRoomId(request.RoomId);
        var createdAtUtc = NormalizeUtc(request.CreatedAtUtc);
        var maxPlayers = NormalizeRoomSize(request.MaxPlayers);

        if (_state.RecordExists)
        {
            return new RoomSettlementResult
            {
                RoomId = roomId,
                Succeeded = true,
                AlreadyApplied = true,
                WinnerUserId = _state.State.WinnerUserId,
                Message = "Room already exists.",
                UpdatedAtUtc = _state.State.LastUpdatedAtUtc,
                SettlementId = _state.State.SettlementId,
                Snapshot = BuildSnapshot()
            };
        }

        _state.State = new RoomState
        {
            RoomId = roomId,
            MatchId = request.MatchId,
            Status = RoomStatus.WaitingForPlayers,
            MaxPlayers = maxPlayers,
            CreatedAtUtc = createdAtUtc,
            LastUpdatedAtUtc = createdAtUtc,
            RuntimeGateway = CloneGateway(request.RuntimeGateway)
        };

        foreach (var player in request.Players)
        {
            if (!UpsertPlayer(player, createdAtUtc))
            {
                return BuildFailure("Room capacity exceeded while creating the room.", createdAtUtc);
            }
        }

        _state.State.Revision += 1;
        await _state.WriteStateAsync();

        return new RoomSettlementResult
        {
            RoomId = roomId,
            Succeeded = true,
            AlreadyApplied = false,
            Message = "Room created.",
            UpdatedAtUtc = createdAtUtc,
            Snapshot = BuildSnapshot()
        };
    }

    public async Task<RoomSettlementResult> JoinAsync(PlayerRoomAssignment request)
    {
        var roomId = NormalizeRoomId(request.RoomId);
        var joinedAtUtc = NormalizeUtc(request.AssignedAtUtc);
        EnsureInitialized(roomId, request.MatchId, joinedAtUtc);

        if (string.IsNullOrWhiteSpace(_state.State.RoomId))
        {
            return BuildFailure("Room has not been created.", joinedAtUtc);
        }

        if (_state.State.Status == RoomStatus.Finished)
        {
            return BuildFailure("Room is already finished.", joinedAtUtc);
        }

        if (FindPlayer(request.UserId) is null && _state.State.Players.Count >= _state.State.MaxPlayers)
        {
            return BuildFailure("Room is full.", joinedAtUtc);
        }

        if (!UpsertPlayer(request, joinedAtUtc))
        {
            return BuildFailure("Room is full.", joinedAtUtc);
        }
        if (_state.State.Status == RoomStatus.Created)
        {
            _state.State.Status = RoomStatus.WaitingForPlayers;
        }

        _state.State.Revision += 1;
        _state.State.LastUpdatedAtUtc = joinedAtUtc;
        await _state.WriteStateAsync();

        return BuildSuccess("Player joined the room.", joinedAtUtc);
    }

    public async Task<RoomSettlementResult> LeaveAsync(RoomPlayerLeaveRequest request)
    {
        var roomId = NormalizeRoomId(request.RoomId);
        var leftAtUtc = NormalizeUtc(request.LeftAtUtc);

        if (!_state.RecordExists)
        {
            return BuildFailure("Room has not been created.", leftAtUtc);
        }

        var player = FindPlayer(request.UserId);
        if (player is null)
        {
            return BuildFailure("Player is not in the room.", leftAtUtc);
        }

        player.IsConnected = false;
        player.IsReady = false;
        player.LeftAtUtc = leftAtUtc;
        player.LeaveReason = request.Reason;
        player.LastSeenAtUtc = leftAtUtc;

        _state.State.Revision += 1;
        _state.State.LastUpdatedAtUtc = leftAtUtc;
        await _state.WriteStateAsync();

        return BuildSuccess("Player left the room.", leftAtUtc);
    }

    public async Task<RoomSettlementResult> SetReadyAsync(RoomPlayerReadyRequest request)
    {
        var roomId = NormalizeRoomId(request.RoomId);
        var updatedAtUtc = NormalizeUtc(request.UpdatedAtUtc);

        if (!_state.RecordExists)
        {
            return BuildFailure("Room has not been created.", updatedAtUtc);
        }

        var player = FindPlayer(request.UserId);
        if (player is null)
        {
            return BuildFailure("Player is not in the room.", updatedAtUtc);
        }

        player.IsReady = request.IsReady;
        player.LastSeenAtUtc = updatedAtUtc;

        _state.State.Revision += 1;
        _state.State.LastUpdatedAtUtc = updatedAtUtc;
        await _state.WriteStateAsync();

        return BuildSuccess("Ready state updated.", updatedAtUtc);
    }

    public async Task<RoomSettlementResult> StartAsync(RoomStartRequest request)
    {
        var roomId = NormalizeRoomId(request.RoomId);
        var startedAtUtc = NormalizeUtc(request.StartedAtUtc);

        if (!_state.RecordExists)
        {
            return BuildFailure("Room has not been created.", startedAtUtc);
        }

        if (_state.State.Status is RoomStatus.InProgress or RoomStatus.Finished)
        {
            return new RoomSettlementResult
            {
                RoomId = roomId,
                Succeeded = true,
                AlreadyApplied = true,
                WinnerUserId = _state.State.WinnerUserId,
                Message = "Room already started or finished.",
                UpdatedAtUtc = _state.State.LastUpdatedAtUtc,
                SettlementId = _state.State.SettlementId,
                Snapshot = BuildSnapshot()
            };
        }

        if (_state.State.Players.Count == 0)
        {
            return BuildFailure("Room has no players.", startedAtUtc);
        }

        _state.State.Status = RoomStatus.InProgress;
        _state.State.StartedAtUtc = startedAtUtc;
        _state.State.LastUpdatedAtUtc = startedAtUtc;
        _state.State.Revision += 1;
        await _state.WriteStateAsync();

        return BuildSuccess("Room started.", startedAtUtc);
    }

    public async Task<RoomSettlementResult> CompleteAsync(RoomMatchCompletion request)
    {
        var roomId = NormalizeRoomId(request.RoomId);
        var finishedAtUtc = NormalizeUtc(request.FinishedAtUtc);

        if (!_state.RecordExists)
        {
            return BuildFailure("Room has not been created.", finishedAtUtc);
        }

        if (string.IsNullOrWhiteSpace(request.SettlementId))
        {
            return BuildFailure("Settlement id is required for idempotent completion.", finishedAtUtc);
        }

        if (!string.IsNullOrWhiteSpace(_state.State.SettlementId) &&
            string.Equals(_state.State.SettlementId, request.SettlementId, StringComparison.Ordinal))
        {
            return new RoomSettlementResult
            {
                RoomId = roomId,
                SettlementId = request.SettlementId,
                Succeeded = true,
                AlreadyApplied = true,
                WinnerUserId = _state.State.WinnerUserId,
                Message = "Settlement already applied.",
                UpdatedAtUtc = _state.State.LastUpdatedAtUtc,
                Snapshot = BuildSnapshot()
            };
        }

        _state.State.Status = RoomStatus.Finished;
        _state.State.FinishedAtUtc = finishedAtUtc;
        _state.State.WinnerUserId = request.WinnerUserId;
        _state.State.SettlementId = request.SettlementId;
        _state.State.Message = request.Reason;
        _state.State.LastUpdatedAtUtc = finishedAtUtc;

        foreach (var result in request.Results)
        {
            var player = FindOrCreatePlayer(result.UserId);
            player.Rank = result.Rank;
            player.Score += result.ScoreDelta;
            player.IsReady = false;
            player.IsConnected = false;
            player.LastSeenAtUtc = finishedAtUtc;
            if (result.IsWinner)
            {
                _state.State.WinnerUserId = result.UserId;
            }
        }

        _state.State.Revision += 1;
        await _state.WriteStateAsync();

        return new RoomSettlementResult
        {
            RoomId = roomId,
            SettlementId = request.SettlementId,
            Succeeded = true,
            AlreadyApplied = false,
            WinnerUserId = _state.State.WinnerUserId,
            Message = "Settlement applied.",
            UpdatedAtUtc = finishedAtUtc,
            Snapshot = BuildSnapshot()
        };
    }

    public Task<RoomSnapshot> GetSnapshotAsync()
    {
        return Task.FromResult(BuildSnapshot());
    }

    private void EnsureState(string roomId)
    {
        if (_state.RecordExists)
        {
            if (string.IsNullOrWhiteSpace(_state.State.RoomId))
            {
                _state.State.RoomId = roomId;
            }

            return;
        }
    }

    private void EnsureInitialized(string roomId, string matchId, DateTime createdAtUtc)
    {
        if (!_state.RecordExists)
        {
            _state.State = new RoomState
            {
                RoomId = roomId,
                MatchId = matchId,
                Status = RoomStatus.WaitingForPlayers,
                MaxPlayers = 10,
                CreatedAtUtc = createdAtUtc,
                LastUpdatedAtUtc = createdAtUtc
            };
        }
    }

    private bool UpsertPlayer(PlayerRoomAssignment request, DateTime joinedAtUtc)
    {
        var existing = FindPlayer(request.UserId);
        if (existing is null)
        {
            if (_state.State.Players.Count >= _state.State.MaxPlayers)
            {
                return false;
            }

            existing = new RoomPlayerState
            {
                UserId = request.UserId,
                JoinedAtUtc = joinedAtUtc
            };
            _state.State.Players.Add(existing);
        }

        existing.SessionToken = request.SessionToken;
        existing.ConnectionId = request.ConnectionId;
        existing.SeatIndex = request.SeatIndex;
        existing.IsConnected = true;
        existing.IsReady = false;
        existing.LeftAtUtc = default;
        existing.LeaveReason = "";
        existing.LastSeenAtUtc = joinedAtUtc;
        return true;
    }

    private RoomPlayerState? FindPlayer(string userId)
    {
        return _state.State.Players.FirstOrDefault(player => string.Equals(player.UserId, userId, StringComparison.Ordinal));
    }

    private RoomPlayerState FindOrCreatePlayer(string userId)
    {
        var player = FindPlayer(userId);
        if (player is not null)
        {
            return player;
        }

        player = new RoomPlayerState
        {
            UserId = userId,
            JoinedAtUtc = _state.State.StartedAtUtc == default ? DateTime.UtcNow : _state.State.StartedAtUtc
        };
        _state.State.Players.Add(player);
        return player;
    }

    private RoomSnapshot BuildSnapshot()
    {
        var players = _state.RecordExists
            ? _state.State.Players.Select(player => new RoomPlayerSnapshot
            {
                UserId = player.UserId,
                SessionToken = player.SessionToken,
                ConnectionId = player.ConnectionId,
                SeatIndex = player.SeatIndex,
                IsReady = player.IsReady,
                IsConnected = player.IsConnected,
                JoinedAtUtc = player.JoinedAtUtc,
                LastSeenAtUtc = player.LastSeenAtUtc,
                LeftAtUtc = player.LeftAtUtc,
                LeaveReason = player.LeaveReason,
                Score = player.Score,
                Rank = player.Rank
            }).ToList()
            : [];

        var memberCount = players.Count;
        var connectedCount = players.Count(player => player.IsConnected);
        var readyCount = players.Count(player => player.IsReady);
        var maxPlayers = _state.RecordExists ? _state.State.MaxPlayers : 10;

        return new RoomSnapshot
        {
            RoomId = _state.RecordExists ? _state.State.RoomId : this.GetPrimaryKeyString(),
            MatchId = _state.RecordExists ? _state.State.MatchId : "",
            Status = _state.RecordExists ? _state.State.Status : RoomStatus.Created,
            MaxPlayers = maxPlayers,
            CreatedAtUtc = _state.RecordExists ? _state.State.CreatedAtUtc : default,
            StartedAtUtc = _state.RecordExists ? _state.State.StartedAtUtc : default,
            FinishedAtUtc = _state.RecordExists ? _state.State.FinishedAtUtc : default,
            Revision = _state.RecordExists ? _state.State.Revision : 0,
            Players = players,
            WinnerUserId = _state.RecordExists ? _state.State.WinnerUserId : "",
            SettlementId = _state.RecordExists ? _state.State.SettlementId : "",
            LastUpdatedAtUtc = _state.RecordExists ? _state.State.LastUpdatedAtUtc : default,
            Message = _state.RecordExists ? _state.State.Message : "",
            MemberCount = memberCount,
            ConnectedCount = connectedCount,
            ReadyCount = readyCount,
            CapacityRemaining = Math.Max(0, maxPlayers - memberCount),
            RuntimeGateway = _state.RecordExists ? CloneGateway(_state.State.RuntimeGateway) : new GatewayEndpointDescriptor()
        };
    }

    private RoomSettlementResult BuildFailure(string message, DateTime updatedAtUtc)
    {
        return new RoomSettlementResult
        {
            RoomId = this.GetPrimaryKeyString(),
            Succeeded = false,
            AlreadyApplied = false,
            Message = message,
            UpdatedAtUtc = updatedAtUtc,
            Snapshot = BuildSnapshot()
        };
    }

    private RoomSettlementResult BuildSuccess(string message, DateTime updatedAtUtc)
    {
        return new RoomSettlementResult
        {
            RoomId = this.GetPrimaryKeyString(),
            Succeeded = true,
            AlreadyApplied = false,
            Message = message,
            UpdatedAtUtc = updatedAtUtc,
            Snapshot = BuildSnapshot()
        };
    }

    private static string NormalizeRoomId(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            throw new ArgumentException("Room id is required.", nameof(roomId));
        }

        return roomId;
    }

    private static int NormalizeRoomSize(int requestedSize)
    {
        return Math.Clamp(requestedSize <= 0 ? 10 : requestedSize, 1, 10);
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value == default ? DateTime.UtcNow : value;
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
