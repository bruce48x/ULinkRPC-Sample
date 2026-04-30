using Microsoft.Extensions.Configuration;
using Shared.Interfaces;

namespace Server.Services;

internal sealed class GatewayRealtimeOptions
{
    public RealtimeTransportKind Transport { get; init; } = RealtimeTransportKind.Kcp;
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 20001;
    public string Path { get; init; } = "";

    public static GatewayRealtimeOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Realtime");
        var transport = ParseTransport(section["Transport"]);
        var host = section["Host"];
        var path = section["Path"];

        return new GatewayRealtimeOptions
        {
            Transport = transport,
            Host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host,
            Port = ParsePort(section["Port"], transport == RealtimeTransportKind.Kcp ? 20001 : 20000),
            Path = string.IsNullOrWhiteSpace(path)
                ? (transport == RealtimeTransportKind.WebSocket ? "/ws" : string.Empty)
                : path
        };
    }

    private static RealtimeTransportKind ParseTransport(string? rawValue)
    {
        if (string.Equals(rawValue, "kcp", StringComparison.OrdinalIgnoreCase))
        {
            return RealtimeTransportKind.Kcp;
        }

        if (string.Equals(rawValue, "ws", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawValue, "websocket", StringComparison.OrdinalIgnoreCase))
        {
            return RealtimeTransportKind.WebSocket;
        }

        return RealtimeTransportKind.Kcp;
    }

    private static int ParsePort(string? rawValue, int fallback)
    {
        return int.TryParse(rawValue, out var port) && port > 0
            ? port
            : fallback;
    }
}
