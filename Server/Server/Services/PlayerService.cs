using Orleans.Contracts.Users;
using Orleans.Contracts;
using Orleans.Contracts.Sessions;
using Server.Realtime;
using Shared.Interfaces;
using Microsoft.Extensions.Logging;
using ULinkHost.Runtime;

namespace Server.Services;

public sealed class PlayerService : IPlayerService, IDisposable, IAsyncDisposable
{
    private readonly IClusterClient _clusterClient;
    private readonly IPlayerCallback _callback;
    private readonly SessionDirectory _sessionDirectory;
    private readonly GatewayMatchmakingService _gatewayMatchmaking;
    private readonly GatewayNodeIdentity _gatewayNodeIdentity;
    private readonly RoomRuntimeHost _roomRuntimeHost;
    private readonly ILogger<PlayerService> _logger;
    private bool _disposed;
    private string? _playerId;
    private string? _connectionId;
    private bool _isRealtimeConnection;
    private bool _controlLoggedIn;

    public PlayerService(IPlayerCallback callback)
    {
        _callback = callback;
        _clusterClient = ULinkHostRuntime.GetRequiredService<IClusterClient>();
        _sessionDirectory = ULinkHostRuntime.GetRequiredService<SessionDirectory>();
        _gatewayMatchmaking = ULinkHostRuntime.GetRequiredService<GatewayMatchmakingService>();
        _gatewayNodeIdentity = ULinkHostRuntime.GetRequiredService<GatewayNodeIdentity>();
        _roomRuntimeHost = ULinkHostRuntime.GetRequiredService<RoomRuntimeHost>();
        _logger = ULinkHostRuntime.GetRequiredService<ILogger<PlayerService>>();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        DisposeAsyncCore().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask<LoginReply> LoginAsync(LoginRequest req)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(req.Account) || string.IsNullOrWhiteSpace(req.Password))
        {
            return new LoginReply { Code = 1 };
        }

        UserLoginResult loginResult;
        try
        {
            loginResult = await _clusterClient.GetGrain<IUserGrain>(req.Account)
                .LoginAsync(req.Password)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Login rejected for account {Account}.", req.Account);
            return new LoginReply { Code = 2 };
        }

        _playerId = loginResult.UserId;
        _connectionId = Guid.NewGuid().ToString("N");
        _controlLoggedIn = true;

        _sessionDirectory.Register(loginResult.UserId, loginResult.SessionToken, _connectionId, _callback);
        await _clusterClient.GetGrain<IPlayerSessionGrain>(loginResult.UserId)
            .AttachAsync(new PlayerSessionAttachRequest
            {
                UserId = loginResult.UserId,
                SessionToken = loginResult.SessionToken,
                ConnectionId = _connectionId,
                AttachedAtUtc = DateTime.UtcNow,
                ControlGateway = CloneGateway(_gatewayNodeIdentity.RealtimeEndpoint)
            })
            .ConfigureAwait(false);

        return new LoginReply
        {
            Code = 0,
            Token = loginResult.SessionToken,
            PlayerId = loginResult.UserId,
            WinCount = loginResult.WinCount
        };
    }

    public async ValueTask StartMatchmakingAsync(MatchmakingRequest req)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(_playerId))
        {
            return;
        }

        await _gatewayMatchmaking.EnqueueAsync(_playerId).ConfigureAwait(false);
    }

    public async ValueTask CancelMatchmakingAsync(CancelMatchmakingRequest req)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(_playerId))
        {
            return;
        }

        await _gatewayMatchmaking.CancelAsync(_playerId, "Matchmaking cancelled").ConfigureAwait(false);
    }

    public async ValueTask<RealtimeAttachReply> AttachRealtimeAsync(RealtimeAttachRequest req)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(req.PlayerId) ||
            string.IsNullOrWhiteSpace(req.Token) ||
            string.IsNullOrWhiteSpace(req.RoomId) ||
            string.IsNullOrWhiteSpace(req.MatchId))
        {
            return new RealtimeAttachReply
            {
                Code = 1,
                Message = "Realtime attach request is incomplete."
            };
        }

        var sessionSnapshot = await _clusterClient.GetGrain<IPlayerSessionGrain>(req.PlayerId)
            .GetSnapshotAsync()
            .ConfigureAwait(false);
        if (!string.Equals(sessionSnapshot.SessionToken, req.Token, StringComparison.Ordinal) ||
            !string.Equals(sessionSnapshot.CurrentRoomId, req.RoomId, StringComparison.Ordinal) ||
            !string.Equals(sessionSnapshot.CurrentMatchId, req.MatchId, StringComparison.Ordinal))
        {
            return new RealtimeAttachReply
            {
                Code = 2,
                Message = "Realtime session attach rejected."
            };
        }

        if (!_gatewayNodeIdentity.IsRuntimeOwner(sessionSnapshot.RuntimeGateway))
        {
            return new RealtimeAttachReply
            {
                Code = 3,
                Message = "Realtime session must attach to the runtime owner gateway."
            };
        }

        var room = await _clusterClient.GetGrain<Orleans.Contracts.Rooms.IRoomGrain>(req.RoomId)
            .GetSnapshotAsync()
            .ConfigureAwait(false);
        await _roomRuntimeHost.EnsureRoomReadyAsync(room).ConfigureAwait(false);

        _playerId = req.PlayerId;
        _connectionId = Guid.NewGuid().ToString("N");
        _isRealtimeConnection = true;

        var attached = _sessionDirectory.AttachRealtime(req.PlayerId, req.Token, req.RoomId, req.MatchId, _connectionId, _callback);
        if (!attached)
        {
            _playerId = null;
            _connectionId = null;
            _isRealtimeConnection = false;
            return new RealtimeAttachReply
            {
                Code = 2,
                Message = "Realtime session attach rejected."
            };
        }

        return new RealtimeAttachReply
        {
            Code = 0,
            Message = "Realtime session attached.",
            PlayerId = req.PlayerId,
            RoomId = req.RoomId,
            MatchId = req.MatchId
        };
    }

    public async ValueTask SubmitInput(InputMessage req)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(_playerId))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(req.PlayerId) &&
            !string.Equals(req.PlayerId, _playerId, StringComparison.Ordinal))
        {
            return;
        }

        var sessionSnapshot = await _clusterClient.GetGrain<IPlayerSessionGrain>(_playerId)
            .GetSnapshotAsync()
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(sessionSnapshot.CurrentRoomId) ||
            !_gatewayNodeIdentity.IsRuntimeOwner(sessionSnapshot.RuntimeGateway))
        {
            return;
        }

        req.PlayerId = _playerId;
        await _roomRuntimeHost.SubmitInputAsync(sessionSnapshot.CurrentRoomId, _playerId, req).ConfigureAwait(false);
    }

    public async ValueTask LogoutAsync(LogoutRequest req)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(_playerId))
        {
            return;
        }

        await ReleasePlayerAsync(_playerId, "Logout").ConfigureAwait(false);
        _playerId = null;
        _connectionId = null;
    }

    private async ValueTask DisposeAsyncCore()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!string.IsNullOrWhiteSpace(_playerId))
        {
            if (_isRealtimeConnection && !_controlLoggedIn)
            {
                await ReleaseRealtimeAsync(_playerId, "Realtime disconnect").ConfigureAwait(false);
            }
            else
            {
                await ReleasePlayerAsync(_playerId, "Dispose").ConfigureAwait(false);
            }

            _playerId = null;
            _connectionId = null;
        }
    }

    private async Task ReleasePlayerAsync(string playerId, string reason)
    {
        var registration = _sessionDirectory.Get(playerId);
        try
        {
            await _gatewayMatchmaking.ReleasePlayerAsync(playerId, reason).ConfigureAwait(false);
            await _clusterClient.GetGrain<IPlayerSessionGrain>(playerId)
                .MarkDisconnectedAsync(new PlayerSessionDisconnectRequest
                {
                    UserId = playerId,
                    ConnectionId = registration?.ConnectionId ?? string.Empty,
                    DisconnectedAtUtc = DateTime.UtcNow,
                    Reason = reason
                })
                .ConfigureAwait(false);
            await _clusterClient.GetGrain<IUserGrain>(playerId)
                .SetOnlineAsync(false)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release player {PlayerId} during {Reason}.", playerId, reason);
        }

        if (registration is not null && !string.IsNullOrWhiteSpace(registration.RoomId))
        {
            _sessionDirectory.ClearRoom(playerId, registration.RoomId);
        }

        _sessionDirectory.Remove(playerId);
    }

    private Task ReleaseRealtimeAsync(string playerId, string reason)
    {
        _sessionDirectory.DetachRealtime(playerId, _connectionId);
        return Task.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static GatewayEndpointDescriptor CloneGateway(GatewayEndpointDescriptor gateway)
    {
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
