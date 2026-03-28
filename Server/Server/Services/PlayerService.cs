using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shared.Interfaces;
using UnityEngine;

namespace Server.Services;

public sealed class PlayerService : IPlayerService, IDisposable, IAsyncDisposable
{
    private const int AiCount = 3;
    private const float MapMin = 0f;
    private const float MapMax = 9f;
    private static readonly TimeSpan AiMoveInterval = TimeSpan.FromMilliseconds(500);

    private readonly IPlayerCallback _callback;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();

    private GameState? _game;
    private Task? _aiLoopTask;
    private bool _disposed;

    public PlayerService(IPlayerCallback callback)
    {
        _callback = callback;
    }

    public ValueTask<LoginReply> LoginAsync(LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Account) || string.IsNullOrWhiteSpace(req.Password))
        {
            return ValueTask.FromResult(new LoginReply { Code = 1 });
        }

        ThrowIfDisposed();

        List<PlayerPosition> snapshot;
        string playerId;
        var startAiLoop = false;

        lock (_gate)
        {
            if (_game is null)
            {
                _game = CreateGame(req.Account);
                startAiLoop = true;
            }

            playerId = _game.PlayerId;
            snapshot = CreateSnapshotLocked();
        }

        if (startAiLoop)
        {
            _aiLoopTask = RunAiLoopAsync(_cts.Token);
        }

        PublishSnapshot(new PlayerPositions() { playerPositions = snapshot });
        return ValueTask.FromResult(new LoginReply { Code = 0, Token = playerId });
    }

    public ValueTask Move(MoveRequest req)
    {
        ThrowIfDisposed();

        List<PlayerPosition>? snapshot = null;

        lock (_gate)
        {
            if (_game is null)
            {
                return ValueTask.CompletedTask;
            }

            if (!string.IsNullOrWhiteSpace(req.PlayerId) && !string.Equals(req.PlayerId, _game.PlayerId, StringComparison.Ordinal))
            {
                return ValueTask.CompletedTask;
            }

            _game.Player.Position = MoveOneStep(_game.Player.Position, req.Direction);
            snapshot = CreateSnapshotLocked();
        }

        PublishSnapshot(new PlayerPositions() { playerPositions = snapshot });
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        DisposeAsyncCore(asyncDispose: false).GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore(asyncDispose: true).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeAsyncCore(bool asyncDispose)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();

        if (_aiLoopTask is not null)
        {
            try
            {
                if (asyncDispose)
                {
                    await _aiLoopTask.ConfigureAwait(false);
                }
                else
                {
                    _aiLoopTask.GetAwaiter().GetResult();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        lock (_gate)
        {
            _game = null;
        }

        _cts.Dispose();
    }

    private async Task RunAiLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(AiMoveInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                List<PlayerPosition>? snapshot = null;

                lock (_gate)
                {
                    if (_game is null)
                    {
                        return;
                    }

                    foreach (var ai in _game.Ais)
                    {
                        ai.Position = MoveOneStep(ai.Position, Random.Shared.Next(0, 4) + 1);
                    }

                    snapshot = CreateSnapshotLocked();
                }

                PublishSnapshot(new PlayerPositions() { playerPositions = snapshot });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void PublishSnapshot(PlayerPositions snapshot)
    {
        _callback.OnMove(snapshot);
    }

    private GameState CreateGame(string account)
    {
        var player = new ActorState(account, new Vector2(0, 0));
        var ais = Enumerable.Range(1, AiCount)
            .Select(index => new ActorState($"ai-{index}", new Vector2(index * 2, index)))
            .ToArray();
        return new GameState(player, ais);
    }

    private List<PlayerPosition> CreateSnapshotLocked()
    {
        if (_game is null)
        {
            return new List<PlayerPosition>();
        }

        var snapshot = new List<PlayerPosition>(1 + _game.Ais.Length)
        {
            new()
            {
                PlayerId = _game.Player.PlayerId,
                Position = _game.Player.Position
            }
        };

        foreach (var ai in _game.Ais)
        {
            snapshot.Add(new PlayerPosition
            {
                PlayerId = ai.PlayerId,
                Position = ai.Position
            });
        }

        return snapshot;
    }

    private static Vector2 MoveOneStep(Vector2 current, int direction)
    {
        var next = direction switch
        {
            1 => new Vector2(current.x + 1, current.y),
            2 => new Vector2(current.x, current.y - 1),
            3 => new Vector2(current.x - 1, current.y),
            _ => new Vector2(current.x, current.y + 1)
        };

        next.x = Math.Clamp(next.x, MapMin, MapMax);
        next.y = Math.Clamp(next.y, MapMin, MapMax);
        return next;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class GameState
    {
        public GameState(ActorState player, ActorState[] ais)
        {
            Player = player;
            Ais = ais;
        }

        public ActorState Player { get; }
        public string PlayerId => Player.PlayerId;
        public ActorState[] Ais { get; }
    }

    private sealed class ActorState
    {
        public ActorState(string playerId, Vector2 position)
        {
            PlayerId = playerId;
            Position = position;
        }

        public string PlayerId { get; }
        public Vector2 Position { get; set; }
    }
}
