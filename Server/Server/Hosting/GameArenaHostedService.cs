using Microsoft.Extensions.Hosting;
using Server.Services;

namespace Server.Hosting;

internal sealed class GameArenaHostedService : BackgroundService
{
    private readonly GameArenaRuntime _arenaRuntime;

    public GameArenaHostedService(GameArenaRuntime arenaRuntime)
    {
        _arenaRuntime = arenaRuntime;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _arenaRuntime.RunAsync(stoppingToken);
    }
}