using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ULinkHost.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(configuration =>
    {
        configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();
    })
    .UseULinkHostOrleansSilo()
    .Build();

await host.RunAsync();
