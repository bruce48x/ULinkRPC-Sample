using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ULinkHost.Hosting;

public static class ULinkHostGatewayExtensions
{
    public static IServiceCollection AddULinkHostGateway(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ULinkRpcServersHostedService>());
        return services;
    }

    [Obsolete("Register project-specific options directly and call AddULinkHostGateway().")]
    public static IServiceCollection AddULinkHostGateway(this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddULinkHostGateway();
    }

    public static IServiceCollection AddULinkRpcServer<TConfigurator>(this IServiceCollection services)
        where TConfigurator : class, IULinkRpcServerConfigurator
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IULinkRpcServerConfigurator, TConfigurator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ULinkRpcServersHostedService>());
        return services;
    }
}
