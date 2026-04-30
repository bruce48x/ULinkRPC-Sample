using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ULinkHost.Hosting;

public static class ULinkHostGatewayExtensions
{
    public static IServiceCollection AddULinkHostGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(_ => Transport.ControlPlaneOptions.FromConfiguration(configuration));
        services.AddSingleton(_ => Transport.RealtimeServerOptions.FromConfiguration(configuration));
        services.AddHostedService<ControlPlaneRpcServerHostedService>();
        services.AddHostedService<RealtimeRpcServerHostedService>();
        return services;
    }
}
