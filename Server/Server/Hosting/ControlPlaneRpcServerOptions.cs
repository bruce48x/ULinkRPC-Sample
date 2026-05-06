namespace Server.Hosting;

internal sealed class ControlPlaneRpcServerOptions
{
    public ControlPlaneRpcServerOptions(GatewayRpcServerOptions endpoint)
    {
        Endpoint = endpoint;
    }

    public GatewayRpcServerOptions Endpoint { get; }
}
