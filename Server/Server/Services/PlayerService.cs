using Orleans.Contracts.Users;
using Server.Orleans;
using Shared.Interfaces;

namespace Server.Services;

public sealed class PlayerService : IPlayerService, IDisposable, IAsyncDisposable
{
    private readonly IPlayerCallback _callback;
    private readonly GameArenaRuntime _arenaRuntime;
    private string? _playerId;
    private bool _disposed;

    public PlayerService(IPlayerCallback callback)
    {
        _callback = callback;
        _arenaRuntime = GameArenaRuntimeRegistry.Instance;
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
            var userGrain = ClusterClientRuntime.GrainFactory.GetGrain<IUserGrain>(req.Account);
            loginResult = await userGrain.LoginAsync(req.Password).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return new LoginReply { Code = 2 };
        }

        _playerId = loginResult.UserId;
        return await _arenaRuntime.RegisterPlayerAsync(loginResult, _callback).ConfigureAwait(false);
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

        req.PlayerId = _playerId;
        _arenaRuntime.SubmitInput(req);
        return ValueTask.CompletedTask;
    }

    public async ValueTask LogoutAsync()
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(_playerId))
        {
            return;
        }

        var playerId = _playerId;
        _playerId = null;

        await _arenaRuntime.UnregisterPlayerAsync(playerId).ConfigureAwait(false);

        try
        {
            var userGrain = ClusterClientRuntime.GrainFactory.GetGrain<IUserGrain>(playerId);
            await userGrain.SetOnlineAsync(false).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        DisposeAsyncCore().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
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
            await _arenaRuntime.UnregisterPlayerAsync(_playerId).ConfigureAwait(false);

            try
            {
                var userGrain = ClusterClientRuntime.GrainFactory.GetGrain<IUserGrain>(_playerId);
                await userGrain.SetOnlineAsync(false).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

