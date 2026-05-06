using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Contracts.Sessions;
using Orleans.Contracts.Users;
using Server.Services;

namespace Server.Hosting;

internal sealed class DisconnectedSessionCleanupHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReconnectGracePeriod = TimeSpan.FromSeconds(60);

    private readonly SessionDirectory _sessionDirectory;
    private readonly GatewayMatchmakingService _gatewayMatchmaking;
    private readonly IClusterClient _clusterClient;
    private readonly ILogger<DisconnectedSessionCleanupHostedService> _logger;

    public DisconnectedSessionCleanupHostedService(
        SessionDirectory sessionDirectory,
        GatewayMatchmakingService gatewayMatchmaking,
        IClusterClient clusterClient,
        ILogger<DisconnectedSessionCleanupHostedService> logger)
    {
        _sessionDirectory = sessionDirectory;
        _gatewayMatchmaking = gatewayMatchmaking;
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
                await CleanupExpiredSessionsAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Disconnected session cleanup hosted service stopped.");
        }
    }

    private async Task CleanupExpiredSessionsAsync(CancellationToken cancellationToken)
    {
        var expired = _sessionDirectory.GetExpiredControlDisconnects(DateTime.UtcNow, ReconnectGracePeriod);
        foreach (var registration in expired)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _gatewayMatchmaking
                    .ReleasePlayerAsync(registration.PlayerId, "Reconnect grace period expired")
                    .ConfigureAwait(false);
                await _clusterClient.GetGrain<IPlayerSessionGrain>(registration.PlayerId)
                    .MarkDisconnectedAsync(new PlayerSessionDisconnectRequest
                    {
                        UserId = registration.PlayerId,
                        ConnectionId = registration.ConnectionId,
                        DisconnectedAtUtc = DateTime.UtcNow,
                        Reason = "Reconnect grace period expired"
                    })
                    .ConfigureAwait(false);
                await _clusterClient.GetGrain<IUserGrain>(registration.PlayerId)
                    .SetOnlineAsync(false)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean up expired disconnected session for player {PlayerId}.", registration.PlayerId);
                continue;
            }

            _sessionDirectory.Remove(registration.PlayerId);
        }
    }
}
