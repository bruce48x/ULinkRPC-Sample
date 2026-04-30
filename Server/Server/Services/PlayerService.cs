using Orleans.Contracts.Users;
using Server.Realtime;
using Server.Runtime;
using Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace Server.Services;

public sealed class PlayerService : IPlayerService, IDisposable, IAsyncDisposable
{
    private readonly IClusterClient _clusterClient;
    private readonly IPlayerCallback _callback;
    private readonly SessionDirectory _sessionDirectory;
    private readonly GatewayMatchmakingService _gatewayMatchmaking;
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
        _clusterClient = ServerRuntime.GetRequiredService<IClusterClient>();
        _sessionDirectory = ServerRuntime.GetRequiredService<SessionDirectory>();
        _gatewayMatchmaking = ServerRuntime.GetRequiredService<GatewayMatchmakingService>();
        _roomRuntimeHost = ServerRuntime.GetRequiredService<RoomRuntimeHost>();
        _logger = ServerRuntime.GetRequiredService<ILogger<PlayerService>>();
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

    public ValueTask<RealtimeAttachReply> AttachRealtimeAsync(RealtimeAttachRequest req)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(req.PlayerId) ||
            string.IsNullOrWhiteSpace(req.Token) ||
            string.IsNullOrWhiteSpace(req.RoomId) ||
            string.IsNullOrWhiteSpace(req.MatchId))
        {
            return ValueTask.FromResult(new RealtimeAttachReply
            {
                Code = 1,
                Message = "Realtime attach request is incomplete."
            });
        }

        _playerId = req.PlayerId;
        _connectionId = Guid.NewGuid().ToString("N");
        _isRealtimeConnection = true;

        var attached = _sessionDirectory.AttachRealtime(req.PlayerId, req.Token, req.RoomId, req.MatchId, _connectionId, _callback);
        if (!attached)
        {
            _playerId = null;
            _connectionId = null;
            _isRealtimeConnection = false;
            return ValueTask.FromResult(new RealtimeAttachReply
            {
                Code = 2,
                Message = "Realtime session attach rejected."
            });
        }

        return ValueTask.FromResult(new RealtimeAttachReply
        {
            Code = 0,
            Message = "Realtime session attached.",
            PlayerId = req.PlayerId,
            RoomId = req.RoomId,
            MatchId = req.MatchId
        });
    }

    public ValueTask SubmitInput(InputMessage req)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(_playerId))
        {
            return ValueTask.CompletedTask;
        }

        if (!string.IsNullOrWhiteSpace(req.PlayerId) &&
            !string.Equals(req.PlayerId, _playerId, StringComparison.Ordinal))
        {
            return ValueTask.CompletedTask;
        }

        var registration = _sessionDirectory.Get(_playerId);
        if (registration is null || string.IsNullOrWhiteSpace(registration.RoomId))
        {
            return ValueTask.CompletedTask;
        }

        req.PlayerId = _playerId;
        return new ValueTask(_roomRuntimeHost.SubmitInputAsync(registration.RoomId, _playerId, req));
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

}
