using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Server.Hosting;
using Server.Realtime;
using Server.Services;
using ULinkHost.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.AddULinkHostOrleansClient();

builder.Services.AddSingleton<SessionDirectory>();
builder.Services.AddSingleton(_ => new ControlPlaneRpcServerOptions(
    GatewayRpcServerOptions.FromConfiguration(
        builder.Configuration,
        "ControlPlane",
        new GatewayRpcServerOptions { Transport = "websocket", Port = 20000, Path = "/ws" })));
builder.Services.AddSingleton(_ => new RealtimeRpcServerOptions(
    GatewayRpcServerOptions.FromConfiguration(
        builder.Configuration,
        "Realtime",
        new GatewayRpcServerOptions { Transport = "kcp", Port = 20001, Path = "" })));
builder.Services.AddSingleton<GatewayNodeIdentity>();
builder.Services.AddSingleton<MatchmakingMonitor>();
builder.Services.AddSingleton<RoomRuntimeHost>();
builder.Services.AddSingleton<GatewayMatchmakingService>();
builder.Services.AddULinkRpcServer<DefaultControlPlaneRpcServerConfigurator>();
builder.Services.AddULinkRpcServer<DefaultRealtimeRpcServerConfigurator>();
builder.Services.AddHostedService<MatchmakingHostedService>();
builder.Services.AddHostedService<DisconnectedSessionCleanupHostedService>();
builder.Services.AddULinkHostGateway();

var host = builder.Build();
await host.RunAsync();
