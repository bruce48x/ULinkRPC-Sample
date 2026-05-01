using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;

namespace ULinkHost.Hosting;

public static class ULinkHostExtensions
{
    public static IHostApplicationBuilder AddULinkHostOrleansClient(this IHostApplicationBuilder builder)
    {
        builder.UseOrleansClient(client =>
        {
            var configuration = builder.Configuration;
            var clusterId = configuration["Orleans:ClusterId"] ?? "dev";
            var serviceId = configuration["Orleans:ServiceId"] ?? "ULinkRPC-Sample-Server";
            var invariant = configuration["Orleans:Invariant"] ?? "Npgsql";
            var connectionString = configuration["Orleans:ConnectionString"]
                ?? throw new InvalidOperationException("Missing configuration: Orleans:ConnectionString");

            client.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = clusterId;
                options.ServiceId = serviceId;
            });

            client.UseAdoNetClustering(options =>
            {
                options.Invariant = invariant;
                options.ConnectionString = connectionString;
            });
        });

        return builder;
    }

    public static IHostBuilder UseULinkHostOrleansSilo(this IHostBuilder hostBuilder)
    {
        return hostBuilder.UseULinkHostOrleansSilo(configureSilo: null);
    }

    public static IHostBuilder UseULinkHostOrleansSilo(
        this IHostBuilder hostBuilder,
        Action<HostBuilderContext, ISiloBuilder>? configureSilo)
    {
        return hostBuilder.UseOrleans((context, silo) =>
        {
            var configuration = context.Configuration;
            var clusterId = configuration["Orleans:ClusterId"] ?? "dev";
            var serviceId = configuration["Orleans:ServiceId"] ?? "ULinkRPC-Sample-Server";
            var invariant = configuration["Orleans:Invariant"] ?? "Npgsql";
            var connectionString = configuration["Orleans:ConnectionString"]
                ?? throw new InvalidOperationException("Missing configuration: Orleans:ConnectionString");
            var siloPort = ParsePort(configuration["Orleans:SiloPort"], 11111);
            var gatewayPort = ParsePort(configuration["Orleans:GatewayPort"], 30000);

            silo.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = clusterId;
                options.ServiceId = serviceId;
            });

            silo.ConfigureEndpoints(siloPort: siloPort, gatewayPort: gatewayPort);
            silo.UseAdoNetClustering(options =>
            {
                options.Invariant = invariant;
                options.ConnectionString = connectionString;
            });

            configureSilo?.Invoke(context, silo);
        });
    }

    private static int ParsePort(string? rawValue, int fallback)
    {
        return int.TryParse(rawValue, out var port) && port > 0
            ? port
            : fallback;
    }
}
