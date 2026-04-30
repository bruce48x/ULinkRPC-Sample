using Microsoft.Extensions.Hosting;
using Server.Services;
using ULinkRPC.Serializer.MemoryPack;
using ULinkRPC.Server;
using ULinkRPC.Transport.Kcp;

namespace Server.Hosting;

internal sealed class RealtimeRpcServerHostedService : BackgroundService
{
    private readonly GatewayRealtimeOptions _realtimeOptions;

    public RealtimeRpcServerHostedService(GatewayRealtimeOptions realtimeOptions)
    {
        _realtimeOptions = realtimeOptions;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var builder = RpcServerHostBuilder.Create()
            .UseCommandLine(args)
            .UseSerializer(new MemoryPackRpcSerializer());

        var port = _realtimeOptions.Port > 0 ? _realtimeOptions.Port : 20001;
        builder.UseAcceptor(new KcpConnectionAcceptor(
            builder.ResolvePort(port),
            builder.Limits.MaxPendingAcceptedConnections));

        await builder.RunAsync();
    }
}
