using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Server.Services;

namespace Server.Hosting;

internal sealed class MatchmakingHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly GatewayMatchmakingService _gatewayMatchmaking;
    private readonly ILogger<MatchmakingHostedService> _logger;

    public MatchmakingHostedService(GatewayMatchmakingService gatewayMatchmaking, ILogger<MatchmakingHostedService> logger)
    {
        _gatewayMatchmaking = gatewayMatchmaking;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await _gatewayMatchmaking.TryDispatchReadyMatchesAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Matchmaking hosted service stopped.");
        }
    }
}
