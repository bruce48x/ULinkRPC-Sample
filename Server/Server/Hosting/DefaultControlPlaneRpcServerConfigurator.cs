using Microsoft.Extensions.DependencyInjection;
using Server.Generated;
using Server.Services;
using ULinkHost.Hosting;
using ULinkHost.Transport;
using ULinkRPC.Serializer.MemoryPack;
using ULinkRPC.Transport.WebSocket;

namespace Server.Hosting;

internal sealed class DefaultControlPlaneRpcServerConfigurator : IControlPlaneRpcServerConfigurator
{
    private readonly ControlPlaneOptions _options;

    public DefaultControlPlaneRpcServerConfigurator(ControlPlaneOptions options)
    {
        _options = options;
    }

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
