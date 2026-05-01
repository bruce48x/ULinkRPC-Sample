namespace ULinkHost.Hosting;

public interface IControlPlaneRpcServerConfigurator
{
    void Configure(ULinkHostRpcServerContext context);
}
