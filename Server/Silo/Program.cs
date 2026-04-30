using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(configuration =>
    {
        configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();
    })
    .UseOrleans((context, silo) =>
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
        silo.AddAdoNetGrainStorage("users", options =>
        {
            options.Invariant = invariant;
            options.ConnectionString = connectionString;
        });
        silo.AddAdoNetGrainStorage("sessions", options =>
        {
            options.Invariant = invariant;
            options.ConnectionString = connectionString;
        });
        silo.AddAdoNetGrainStorage("matchmaking", options =>
        {
            options.Invariant = invariant;
            options.ConnectionString = connectionString;
        });
        silo.AddAdoNetGrainStorage("rooms", options =>
        {
            options.Invariant = invariant;
            options.ConnectionString = connectionString;
        });
    })
    .Build();

await host.RunAsync();

static int ParsePort(string? rawValue, int fallback)
{
    return int.TryParse(rawValue, out var port) && port > 0
        ? port
        : fallback;
}
