using Microsoft.Extensions.DependencyInjection;
using Server.Generated;
using Server.Services;
using ULinkHost.Hosting;
using ULinkRPC.Serializer.MemoryPack;
using ULinkRPC.Transport.Kcp;
using ULinkRPC.Transport.WebSocket;

namespace Server.Hosting;

internal sealed class DefaultRealtimeRpcServerConfigurator : IULinkRpcServerConfigurator
{
    private readonly GatewayRpcServerOptions _options;

    public DefaultRealtimeRpcServerConfigurator(RealtimeRpcServerOptions options)
    {
        _options = options.Endpoint;
    }

    public string Name => "realtime";

    public void Configure(ULinkHostRpcServerContext context)
    {
        var builder = context.Builder;
        builder.UseSerializer(new MemoryPackRpcSerializer());

        PlayerServiceBinder.Bind(
            builder.ServiceRegistry,
            callback => ActivatorUtilities.CreateInstance<PlayerService>(context.Services, callback));

        var port = _options.Port > 0 ? _options.Port : 20001;
        if (string.Equals(_options.Transport, "websocket", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_options.Transport, "ws", StringComparison.OrdinalIgnoreCase))
        {
            var path = string.IsNullOrWhiteSpace(_options.Path) ? "/ws" : _options.Path;
            builder.UseAcceptor(async ct => await WsConnectionAcceptor.CreateAsync(builder.ResolvePort(port), path, ct));
            return;
        }

        if (string.Equals(_options.Transport, "kcp", StringComparison.OrdinalIgnoreCase))
        {
            builder.UseAcceptor(new KcpConnectionAcceptor(
                builder.ResolvePort(port),
                builder.Limits.MaxPendingAcceptedConnections));
            return;
        }

        throw new InvalidOperationException(
            $"Unsupported realtime transport '{_options.Transport}'. Register a custom {nameof(IULinkRpcServerConfigurator)} for this project.");
    }
}
