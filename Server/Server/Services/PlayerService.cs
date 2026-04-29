using Orleans.Contracts.Matchmaking;
using Orleans.Contracts.Rooms;
using Orleans.Contracts.Sessions;
using Orleans.Contracts.Users;
using Server.Realtime;
using Server.Runtime;
using Shared.Interfaces;

namespace Server.Services;

public sealed class PlayerService : IPlayerService, IDisposable, IAsyncDisposable
{
    private readonly IClusterClient _clusterClient;
    private readonly IPlayerCallback _callback;
    private readonly SessionDirectory _sessionDirectory;
    private readonly MatchmakingMonitor _matchmakingMonitor;
    private readonly RoomRuntimeHost _roomRuntimeHost;
    private bool _disposed;
    private string? _playerId;
    private string? _sessionToken;
    private string? _connectionId;
    private CancellationTokenSource? _assignmentWatchCts;

    public PlayerService(IPlayerCallback callback)
    {
        _callback = callback;
        _clusterClient = ServerRuntime.GetRequiredService<IClusterClient>();
        _sessionDirectory = ServerRuntime.GetRequiredService<SessionDirectory>();
        _matchmakingMonitor = ServerRuntime.GetRequiredService<MatchmakingMonitor>();
        _roomRuntimeHost = ServerRuntime.GetRequiredService<RoomRuntimeHost>();
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
        catch (InvalidOperationException)
        {
            return new LoginReply { Code = 2 };
        }

        _playerId = loginResult.UserId;
        _sessionToken = loginResult.SessionToken;
        _connectionId = Guid.NewGuid().ToString("N");

        _sessionDirectory.Register(loginResult.UserId, _callback);

        await _clusterClient.GetGrain<IPlayerSessionGrain>(loginResult.UserId)
            .AttachAsync(new PlayerSessionAttachRequest
            {
                UserId = loginResult.UserId,
                SessionToken = loginResult.SessionToken,
                ConnectionId = _connectionId,
                AttachedAtUtc = DateTime.UtcNow
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

        CancelAssignmentWatch();

        var result = await _clusterClient.GetGrain<IMatchmakingGrain>("default")
            .EnqueueAsync(new MatchmakingEnqueueRequest
            {
                UserId = _playerId,
                SessionToken = req.Token,
                EnqueuedAtUtc = DateTime.UtcNow,
                Priority = 0
            })
            .ConfigureAwait(false);

        SafeInvoke(_callback, callback => callback.OnMatchmakingStatus(new MatchmakingStatusUpdate
        {
            State = result.Matched ? Shared.Interfaces.MatchmakingState.Matched : Shared.Interfaces.MatchmakingState.Queued,
            QueuePosition = result.QueuePosition,
            QueueSize = result.QueuePosition > 0 ? result.QueuePosition : result.RoomAssignment.Players.Count,
            RoomCapacity = result.RoomAssignment.Players.Count > 0 ? result.RoomAssignment.Players.Count : 10,
            RoomId = result.RoomAssignment.RoomId,
            MatchedPlayerCount = result.RoomAssignment.Players.Count,
            Message = result.Message
        }));

        _assignmentWatchCts = new CancellationTokenSource();
        _ = _matchmakingMonitor.WatchForRoomAssignmentAsync(_playerId, _assignmentWatchCts.Token);
    }

    public async ValueTask CancelMatchmakingAsync(CancelMatchmakingRequest req)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(_playerId))
        {
            return;
        }

        CancelAssignmentWatch();

        await _clusterClient.GetGrain<IMatchmakingGrain>("default")
            .CancelAsync(new MatchmakingCancelRequest
            {
                UserId = _playerId,
                TicketId = req.Token,
                CancelledAtUtc = DateTime.UtcNow,
                Reason = "Client cancelled"
            })
            .ConfigureAwait(false);

        SafeInvoke(_callback, callback => callback.OnMatchmakingStatus(new MatchmakingStatusUpdate
        {
            State = Shared.Interfaces.MatchmakingState.Canceled,
            QueueSize = 0,
            RoomCapacity = 10,
            RoomId = string.Empty,
            MatchedPlayerCount = 0,
            Message = "Matchmaking cancelled"
        }));
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
        _sessionToken = null;
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
            await ReleasePlayerAsync(_playerId, "Dispose").ConfigureAwait(false);
            _playerId = null;
            _sessionToken = null;
            _connectionId = null;
        }
    }

    private async Task ReleasePlayerAsync(string playerId, string reason)
    {
        CancelAssignmentWatch();

        var registration = _sessionDirectory.Get(playerId);
        if (registration is not null && !string.IsNullOrWhiteSpace(registration.RoomId))
        {
            await _clusterClient.GetGrain<IRoomGrain>(registration.RoomId)
                .LeaveAsync(new RoomPlayerLeaveRequest
                {
                    UserId = playerId,
                    RoomId = registration.RoomId,
                    LeftAtUtc = DateTime.UtcNow,
                    Reason = reason
                })
                .ConfigureAwait(false);
            await _roomRuntimeHost.RemovePlayerAsync(registration.RoomId, playerId).ConfigureAwait(false);
        }
        _sessionDirectory.Remove(playerId);

        try
        {
            await _clusterClient.GetGrain<IMatchmakingGrain>("default")
                .CancelAsync(new MatchmakingCancelRequest
                {
                    UserId = playerId,
                    TicketId = string.Empty,
                    CancelledAtUtc = DateTime.UtcNow,
                    Reason = reason
                })
                .ConfigureAwait(false);

            await _clusterClient.GetGrain<IPlayerSessionGrain>(playerId)
                .MarkDisconnectedAsync(new PlayerSessionDisconnectRequest
                {
                    UserId = playerId,
                    ConnectionId = _connectionId ?? string.Empty,
                    DisconnectedAtUtc = DateTime.UtcNow,
                    Reason = reason
                })
                .ConfigureAwait(false);

            if (registration is not null && !string.IsNullOrWhiteSpace(registration.RoomId))
            {
                await _clusterClient.GetGrain<IPlayerSessionGrain>(playerId)
                    .ClearRoomAsync(new PlayerRoomClearRequest
                    {
                        UserId = playerId,
                        RoomId = registration.RoomId,
                        ClearedAtUtc = DateTime.UtcNow,
                        Reason = reason
                    })
                    .ConfigureAwait(false);
            }

            await _clusterClient.GetGrain<IUserGrain>(playerId)
                .SetOnlineAsync(false)
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void CancelAssignmentWatch()
    {
        if (_assignmentWatchCts is null)
        {
            return;
        }

        _assignmentWatchCts.Cancel();
        _assignmentWatchCts.Dispose();
        _assignmentWatchCts = null;
    }

    private static void SafeInvoke(IPlayerCallback callback, Action<IPlayerCallback> action)
    {
        try
        {
            action(callback);
        }
        catch
        {
        }
    }
}
