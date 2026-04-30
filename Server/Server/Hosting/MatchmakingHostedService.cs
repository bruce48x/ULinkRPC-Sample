using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Contracts.Matchmaking;
using Server.Services;

namespace Server.Hosting;

internal sealed class MatchmakingHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IClusterClient _clusterClient;
    private readonly ILogger<MatchmakingHostedService> _logger;

    public MatchmakingHostedService(IClusterClient clusterClient, ILogger<MatchmakingHostedService> logger)
    {
        _clusterClient = clusterClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await _clusterClient.GetGrain<IMatchmakingGrain>("default")
                    .TickAsync(new MatchmakingTickRequest
                    {
                        ObservedAtUtc = DateTime.UtcNow
                    })
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Matchmaking hosted service stopped.");
        }
    }
}
