using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Server.Hosting;
using Server.Realtime;
using Server.Runtime;
using Server.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

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

builder.Services.AddSingleton<SessionDirectory>();
builder.Services.AddSingleton(_ => GatewayRealtimeOptions.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton<GatewayNodeIdentity>();
builder.Services.AddSingleton<MatchmakingMonitor>();
builder.Services.AddSingleton<RoomRuntimeHost>();
builder.Services.AddSingleton<GatewayMatchmakingService>();
builder.Services.AddHostedService<MatchmakingHostedService>();
builder.Services.AddHostedService<RpcServerHostedService>();
builder.Services.AddHostedService<RealtimeRpcServerHostedService>();

var host = builder.Build();
ServerRuntime.Initialize(host.Services);
await host.RunAsync();
