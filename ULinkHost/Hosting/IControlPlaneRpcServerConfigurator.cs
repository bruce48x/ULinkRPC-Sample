using ULinkRPC.Server;

namespace ULinkHost.Hosting;

public interface IControlPlaneRpcServerConfigurator
{
    void Configure(RpcServerHostBuilder builder);
}
