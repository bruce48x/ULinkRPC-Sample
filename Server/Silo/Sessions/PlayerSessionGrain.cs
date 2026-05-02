using Orleans;
using Orleans.Contracts;
using Orleans.Contracts.Sessions;
using Orleans.Runtime;

namespace ULinkRPC.Sample.Silo.Sessions;

public sealed class PlayerSessionGrain : Grain, IPlayerSessionGrain
{
    private readonly IPersistentState<PlayerSessionState> _state;

    public PlayerSessionGrain([PersistentState("player-session", "sessions")] IPersistentState<PlayerSessionState> state)
    {
        _state = state;
    }

    public async Task<PlayerSessionSnapshot> AttachAsync(PlayerSessionAttachRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var attachedAtUtc = NormalizeUtc(request.AttachedAtUtc);
        EnsureState(userId);

        _state.State.UserId = userId;
        _state.State.SessionToken = request.SessionToken;
        _state.State.ConnectionId = request.ConnectionId;
        _state.State.IsOnline = true;
        _state.State.IsQueued = false;
        _state.State.QueueId = "";
        _state.State.MatchmakingTicketId = "";
        _state.State.CurrentRoomId = "";
        _state.State.CurrentMatchId = "";
        _state.State.SeatIndex = -1;
        _state.State.AttachedAtUtc = attachedAtUtc;
        _state.State.LastConnectedAtUtc = attachedAtUtc;
        _state.State.LastHeartbeatAtUtc = attachedAtUtc;
        _state.State.ReconnectToken = EnsureReconnectToken(_state.State.ReconnectToken);
        _state.State.ControlGateway = CloneGateway(request.ControlGateway);
        _state.State.RuntimeGateway = new GatewayEndpointDescriptor();

        await _state.WriteStateAsync();
        return BuildSnapshot();
    }

    public async Task<PlayerSessionSnapshot> MarkQueuedAsync(PlayerSessionQueueRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var queuedAtUtc = NormalizeUtc(request.QueuedAtUtc);
        EnsureState(userId);

        _state.State.UserId = userId;
        _state.State.IsQueued = true;
        _state.State.QueueId = request.QueueId;
        _state.State.MatchmakingTicketId = request.TicketId;
        _state.State.LastQueuedAtUtc = queuedAtUtc;

        await _state.WriteStateAsync();
        return BuildSnapshot();
    }

    public async Task<PlayerSessionSnapshot> ClearQueueAsync(PlayerSessionQueueClearRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        EnsureState(userId);

        _state.State.IsQueued = false;
        _state.State.QueueId = "";
        _state.State.MatchmakingTicketId = "";

        await _state.WriteStateAsync();
        return BuildSnapshot();
    }

    public async Task<PlayerSessionSnapshot> AssignRoomAsync(PlayerRoomAssignment request)
    {
        var userId = NormalizeUserId(request.UserId);
        var assignedAtUtc = NormalizeUtc(request.AssignedAtUtc);
        EnsureState(userId);

        _state.State.UserId = userId;
        _state.State.SessionToken = string.IsNullOrWhiteSpace(request.SessionToken) ? _state.State.SessionToken : request.SessionToken;
        _state.State.ConnectionId = string.IsNullOrWhiteSpace(request.ConnectionId) ? _state.State.ConnectionId : request.ConnectionId;
        _state.State.CurrentRoomId = request.RoomId;
        _state.State.CurrentMatchId = request.MatchId;
        _state.State.SeatIndex = request.SeatIndex;
        _state.State.IsQueued = false;
        _state.State.QueueId = "";
        _state.State.MatchmakingTicketId = "";
        _state.State.IsOnline = true;
        _state.State.LastConnectedAtUtc = assignedAtUtc;
        _state.State.LastHeartbeatAtUtc = assignedAtUtc;
        _state.State.ReconnectToken = EnsureReconnectToken(_state.State.ReconnectToken);
        _state.State.RuntimeGateway = CloneGateway(request.RuntimeGateway);

        await _state.WriteStateAsync();
        return BuildSnapshot();
    }

    public async Task<PlayerSessionSnapshot> ClearRoomAsync(PlayerRoomClearRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        EnsureState(userId);

        if (string.IsNullOrWhiteSpace(request.RoomId) || string.Equals(_state.State.CurrentRoomId, request.RoomId, StringComparison.Ordinal))
        {
            _state.State.CurrentRoomId = "";
            _state.State.CurrentMatchId = "";
            _state.State.SeatIndex = -1;
        }

        await _state.WriteStateAsync();
        return BuildSnapshot();
    }

    public async Task<PlayerSessionSnapshot> MarkDisconnectedAsync(PlayerSessionDisconnectRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var disconnectedAtUtc = NormalizeUtc(request.DisconnectedAtUtc);
        EnsureState(userId);

        if (string.IsNullOrWhiteSpace(request.ConnectionId) || string.Equals(_state.State.ConnectionId, request.ConnectionId, StringComparison.Ordinal))
        {
            _state.State.ConnectionId = "";
        }

        _state.State.IsOnline = false;
        _state.State.LastDisconnectedAtUtc = disconnectedAtUtc;
        _state.State.LastHeartbeatAtUtc = disconnectedAtUtc;

        await _state.WriteStateAsync();
        return BuildSnapshot();
    }

    public async Task<PlayerSessionSnapshot> HeartbeatAsync(PlayerSessionHeartbeatRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var observedAtUtc = NormalizeUtc(request.ObservedAtUtc);
        EnsureState(userId);

        _state.State.LastHeartbeatAtUtc = observedAtUtc;
        if (_state.State.AttachedAtUtc == default)
        {
            _state.State.AttachedAtUtc = observedAtUtc;
        }

        await _state.WriteStateAsync();
        return BuildSnapshot();
    }

    public Task<PlayerSessionSnapshot> GetSnapshotAsync()
    {
        return Task.FromResult(BuildSnapshot());
    }

    private void EnsureState(string userId)
    {
        if (!_state.RecordExists)
        {
            _state.State = new PlayerSessionState
            {
                UserId = userId,
                ReconnectToken = Guid.NewGuid().ToString("N")
            };
            return;
        }

        if (!string.IsNullOrWhiteSpace(_state.State.UserId) && !string.Equals(_state.State.UserId, userId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Player session grain key does not match the requested user id.");
        }
    }

    private PlayerSessionSnapshot BuildSnapshot()
    {
        if (!_state.RecordExists)
        {
            return new PlayerSessionSnapshot
            {
                UserId = this.GetPrimaryKeyString()
            };
        }

        return new PlayerSessionSnapshot
        {
            UserId = _state.State.UserId,
            SessionToken = _state.State.SessionToken,
            ConnectionId = _state.State.ConnectionId,
            IsOnline = _state.State.IsOnline,
            IsQueued = _state.State.IsQueued,
            QueueId = _state.State.QueueId,
            MatchmakingTicketId = _state.State.MatchmakingTicketId,
            CurrentRoomId = _state.State.CurrentRoomId,
            CurrentMatchId = _state.State.CurrentMatchId,
            SeatIndex = _state.State.SeatIndex,
            AttachedAtUtc = _state.State.AttachedAtUtc,
            LastQueuedAtUtc = _state.State.LastQueuedAtUtc,
            LastConnectedAtUtc = _state.State.LastConnectedAtUtc,
            LastDisconnectedAtUtc = _state.State.LastDisconnectedAtUtc,
            LastHeartbeatAtUtc = _state.State.LastHeartbeatAtUtc,
            ReconnectToken = _state.State.ReconnectToken,
            ControlGateway = CloneGateway(_state.State.ControlGateway),
            RuntimeGateway = CloneGateway(_state.State.RuntimeGateway)
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

    private static string EnsureReconnectToken(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value;
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
