using Microsoft.Extensions.Configuration;

namespace ULinkHost.Transport;

public sealed class ControlPlaneOptions
{
    public int Port { get; init; } = 20000;
    public string Path { get; init; } = "/ws";

    public static ControlPlaneOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("ControlPlane");
        var path = section["Path"];

        return new ControlPlaneOptions
        {
            Port = ParsePort(section["Port"], 20000),
            Path = string.IsNullOrWhiteSpace(path) ? "/ws" : path
        };
    }

    private static int ParsePort(string? rawValue, int fallback)
    {
        return int.TryParse(rawValue, out var port) && port > 0
            ? port
            : fallback;
    }
}
