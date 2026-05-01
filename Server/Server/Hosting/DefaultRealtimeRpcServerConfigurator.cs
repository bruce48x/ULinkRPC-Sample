using Microsoft.Extensions.DependencyInjection;
using Server.Generated;
using Server.Services;
using ULinkHost.Hosting;
using ULinkHost.Transport;
using ULinkRPC.Serializer.MemoryPack;
using ULinkRPC.Transport.Kcp;
using ULinkRPC.Transport.WebSocket;

namespace Server.Hosting;

internal sealed class DefaultRealtimeRpcServerConfigurator : IRealtimeRpcServerConfigurator
{
    private readonly RealtimeServerOptions _options;

    public DefaultRealtimeRpcServerConfigurator(RealtimeServerOptions options)
    {
        _options = options;
    }

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
            $"Unsupported realtime transport '{_options.Transport}'. Register a custom {nameof(IRealtimeRpcServerConfigurator)} for this project.");
    }
}
