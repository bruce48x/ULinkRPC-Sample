namespace ULinkHost.Hosting;

public interface IULinkRpcServerConfigurator
{
    string Name { get; }

    void Configure(ULinkHostRpcServerContext context);
}
