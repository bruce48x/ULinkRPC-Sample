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
builder.Services.AddSingleton<GatewayNodeIdentity>();
builder.Services.AddSingleton<MatchmakingMonitor>();
builder.Services.AddSingleton<RoomRuntimeHost>();
builder.Services.AddSingleton<GatewayMatchmakingService>();
builder.Services.AddSingleton<IControlPlaneRpcServerConfigurator, DefaultControlPlaneRpcServerConfigurator>();
builder.Services.AddSingleton<IRealtimeRpcServerConfigurator, DefaultRealtimeRpcServerConfigurator>();
builder.Services.AddHostedService<MatchmakingHostedService>();
builder.Services.AddULinkHostGateway(builder.Configuration);

var host = builder.Build();
await host.RunAsync();
