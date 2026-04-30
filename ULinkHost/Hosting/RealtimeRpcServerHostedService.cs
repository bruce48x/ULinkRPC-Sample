using Microsoft.Extensions.Hosting;
using ULinkRPC.Server;

namespace ULinkHost.Hosting;

internal sealed class RealtimeRpcServerHostedService : BackgroundService
{
    private readonly IRealtimeRpcServerConfigurator _configurator;

    public RealtimeRpcServerHostedService(IRealtimeRpcServerConfigurator configurator)
    {
        _configurator = configurator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var builder = RpcServerHostBuilder.Create()
            .UseCommandLine(args);
        _configurator.Configure(builder);

        await builder.RunAsync();
    }
}
