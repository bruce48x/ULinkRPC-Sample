using Microsoft.Extensions.Hosting;
using Orleans;

var host = Host.CreateDefaultBuilder(args)
    .UseOrleans(silo =>
    {
        silo.UseLocalhostClustering(
            serviceId: "ULinkRPC-Sample-Server",
            clusterId: "dev");
        silo.AddMemoryGrainStorage("users");
        silo.AddMemoryGrainStorage("sessions");
        silo.AddMemoryGrainStorage("matchmaking");
        silo.AddMemoryGrainStorage("rooms");
    })
    .Build();

await host.RunAsync();
