using ULinkRPC.Server;

namespace ULinkHost.Hosting;

public sealed class ULinkHostRpcServerContext
{
    public ULinkHostRpcServerContext(
        string serverName,
        RpcServerHostBuilder builder,
        IServiceProvider services,
        string[] commandLineArgs,
        CancellationToken stoppingToken)
    {
        ServerName = serverName;
        Builder = builder;
        Services = services;
        CommandLineArgs = commandLineArgs;
        StoppingToken = stoppingToken;
    }

    public string ServerName { get; }

    public RpcServerHostBuilder Builder { get; }

    public IServiceProvider Services { get; }

    public string[] CommandLineArgs { get; }

    public CancellationToken StoppingToken { get; }
}
