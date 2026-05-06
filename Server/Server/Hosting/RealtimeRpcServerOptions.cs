namespace Server.Hosting;

internal sealed class RealtimeRpcServerOptions
{
    public RealtimeRpcServerOptions(GatewayRpcServerOptions endpoint)
    {
        Endpoint = endpoint;
    }

    public GatewayRpcServerOptions Endpoint { get; }
}
