using Microsoft.Extensions.Configuration;

namespace ULinkHost.Transport;

public sealed class RealtimeServerOptions
{
    public string Transport { get; init; } = "kcp";
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 20001;
    public string Path { get; init; } = "";

    public static RealtimeServerOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Realtime");
        var host = section["Host"];
        var path = section["Path"];
        var transport = NormalizeTransport(section["Transport"]);

        return new RealtimeServerOptions
        {
            Transport = transport,
            Host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host,
            Port = ParsePort(section["Port"], string.Equals(transport, "kcp", StringComparison.OrdinalIgnoreCase) ? 20001 : 20000),
            Path = string.IsNullOrWhiteSpace(path)
                ? (string.Equals(transport, "websocket", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(transport, "ws", StringComparison.OrdinalIgnoreCase)
                    ? "/ws"
                    : string.Empty)
                : path
        };
    }

    private static string NormalizeTransport(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return "kcp";
        }

        return rawValue.Trim().ToLowerInvariant();
    }

    private static int ParsePort(string? rawValue, int fallback)
    {
        return int.TryParse(rawValue, out var port) && port > 0
            ? port
            : fallback;
    }
}
