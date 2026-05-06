using Microsoft.Extensions.DependencyInjection;
using Server.Generated;
using Server.Services;
using ULinkHost.Hosting;
using ULinkRPC.Serializer.MemoryPack;
using ULinkRPC.Transport.WebSocket;

namespace Server.Hosting;

internal sealed class DefaultControlPlaneRpcServerConfigurator : IULinkRpcServerConfigurator
{
    private readonly GatewayRpcServerOptions _options;

    public DefaultControlPlaneRpcServerConfigurator(ControlPlaneRpcServerOptions options)
    {
        _options = options.Endpoint;
    }

    public string Name => "control";

    public void Configure(ULinkHostRpcServerContext context)
    {
        var builder = context.Builder;
        var path = string.IsNullOrWhiteSpace(_options.Path) ? "/ws" : _options.Path;

        builder
            .UseSerializer(new MemoryPackRpcSerializer())
            .UseAcceptor(async ct => await WsConnectionAcceptor.CreateAsync(builder.ResolvePort(_options.Port), path, ct));

        PlayerServiceBinder.Bind(
            builder.ServiceRegistry,
            callback => ActivatorUtilities.CreateInstance<PlayerService>(context.Services, callback));
    }
}
