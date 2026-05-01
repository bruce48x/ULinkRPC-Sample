using ULinkRPC.Server;

namespace ULinkHost.Hosting;

public sealed class ULinkHostRpcServerContext
{
    public ULinkHostRpcServerContext(
        RpcServerHostBuilder builder,
        IServiceProvider services,
        string[] commandLineArgs,
        CancellationToken stoppingToken)
    {
        Builder = builder;
        Services = services;
        CommandLineArgs = commandLineArgs;
        StoppingToken = stoppingToken;
    }

    public RpcServerHostBuilder Builder { get; }

    public IServiceProvider Services { get; }

    public string[] CommandLineArgs { get; }

    public CancellationToken StoppingToken { get; }
}
