using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using ULinkHost.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(configuration =>
    {
        configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();
    })
    .UseULinkHostOrleansSilo((context, silo) =>
    {
        var configuration = context.Configuration;
        var invariant = configuration["Orleans:Invariant"] ?? "Npgsql";
        var connectionString = configuration["Orleans:ConnectionString"]
            ?? throw new InvalidOperationException("Missing configuration: Orleans:ConnectionString");

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
