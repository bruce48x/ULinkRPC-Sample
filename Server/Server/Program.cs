using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Server.Hosting;
using Server.Orleans;
using Server.Services;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleansClient(client =>
    {
        client.UseLocalhostClustering(
            serviceId: "ULinkRPC-Sample-Server",
            clusterId: "dev");
    });

builder.Services.AddSingleton<GameArenaRuntime>();
builder.Services.Configure<GameArenaOptions>(builder.Configuration.GetSection("GameArena"));
builder.Services.AddSingleton(static sp =>
{
    var options = sp.GetRequiredService<IOptions<GameArenaOptions>>();
    return options.Value;
});
builder.Services.AddHostedService<GameArenaHostedService>();
builder.Services.AddHostedService<RpcServerHostedService>();

var host = builder.Build();
ClusterClientRuntime.Initialize(host.Services.GetRequiredService<IClusterClient>());
GameArenaRuntimeRegistry.Initialize(host.Services.GetRequiredService<GameArenaRuntime>());
await host.RunAsync();