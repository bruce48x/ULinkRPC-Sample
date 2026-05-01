using Microsoft.Extensions.Hosting;
using ULinkRPC.Server;

namespace ULinkHost.Hosting;

internal sealed class RealtimeRpcServerHostedService : BackgroundService
{
    private readonly IRealtimeRpcServerConfigurator _configurator;
    private readonly IServiceProvider _services;

    public RealtimeRpcServerHostedService(
        IRealtimeRpcServerConfigurator configurator,
        IServiceProvider services)
    {
        _configurator = configurator;
        _services = services;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var builder = RpcServerHostBuilder.Create()
            .UseCommandLine(args);
        _configurator.Configure(new ULinkHostRpcServerContext(builder, _services, args, stoppingToken));

        await builder.RunAsync(stoppingToken);
    }
}
