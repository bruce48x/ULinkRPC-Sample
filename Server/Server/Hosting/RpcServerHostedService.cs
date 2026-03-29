using Microsoft.Extensions.Hosting;
using ULinkRPC.Server;
using ULinkRPC.Serializer.MemoryPack;
using ULinkRPC.Transport.WebSocket;

namespace Server.Hosting;

internal sealed class RpcServerHostedService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var builder = RpcServerHostBuilder.Create()
    .UseCommandLine(args)
    .UseSerializer(new MemoryPackRpcSerializer());    
builder.UseAcceptor(async ct => await WsConnectionAcceptor.CreateAsync(builder.ResolvePort(20000), "/ws", stoppingToken));
            await builder.RunAsync();
    }
}
