using Microsoft.Extensions.Hosting;
using Orleans;

var host = Host.CreateDefaultBuilder(args)
    .UseOrleans(silo =>
    {
        silo.UseLocalhostClustering(
            serviceId: "ULinkRPC-Sample-Server",
            clusterId: "dev");
        silo.AddMemoryGrainStorage("users");
    })
    .Build();

await host.RunAsync();
