using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Server.Hosting;
using Server.Realtime;
using Server.Runtime;
using Server.Services;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleansClient(client =>
    {
        client.UseLocalhostClustering(
            serviceId: "ULinkRPC-Sample-Server",
            clusterId: "dev");
    });

builder.Services.AddSingleton<SessionDirectory>();
builder.Services.AddSingleton(_ => GatewayRealtimeOptions.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton<RoomRuntimeHost>();
builder.Services.AddSingleton<GatewayMatchmakingService>();
builder.Services.AddHostedService<MatchmakingHostedService>();
builder.Services.AddHostedService<RpcServerHostedService>();
builder.Services.AddHostedService<RealtimeRpcServerHostedService>();

var host = builder.Build();
ServerRuntime.Initialize(host.Services);
await host.RunAsync();
