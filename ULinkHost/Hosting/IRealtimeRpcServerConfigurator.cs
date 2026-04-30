using ULinkRPC.Server;

namespace ULinkHost.Hosting;

public interface IRealtimeRpcServerConfigurator
{
    void Configure(RpcServerHostBuilder builder);
}
