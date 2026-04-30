using Microsoft.Extensions.DependencyInjection;

namespace ULinkHost.Runtime;

public static class ULinkHostRuntime
{
    private static IServiceProvider? _services;

    public static IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("ULinkHost services have not been initialized.");

    public static T GetRequiredService<T>() where T : notnull
    {
        return Services.GetRequiredService<T>();
    }

    public static void Initialize(IServiceProvider services)
    {
        _services = services;
    }
}
