using Microsoft.Extensions.DependencyInjection;

namespace Server.Runtime;

internal static class ServerRuntime
{
    private static IServiceProvider? _services;

    public static IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("Server services have not been initialized.");

    public static T GetRequiredService<T>() where T : notnull
    {
        return Services.GetRequiredService<T>();
    }

    public static void Initialize(IServiceProvider services)
    {
        _services = services;
    }
}
