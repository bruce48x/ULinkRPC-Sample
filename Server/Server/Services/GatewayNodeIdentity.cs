using Microsoft.Extensions.Configuration;
using Orleans.Contracts;
using Shared.Interfaces;

namespace Server.Services;

internal sealed class GatewayNodeIdentity
{
    public GatewayNodeIdentity(IConfiguration configuration, GatewayRealtimeOptions realtimeOptions)
    {
        InstanceId = configuration["Gateway:NodeId"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(InstanceId))
        {
            InstanceId = $"{Environment.MachineName}-{Environment.ProcessId}";
        }

        RealtimeEndpoint = new GatewayEndpointDescriptor
        {
            InstanceId = InstanceId,
            Transport = RealtimeTransportToString(realtimeOptions.Transport),
            Host = realtimeOptions.Host,
            Port = realtimeOptions.Port,
            Path = realtimeOptions.Path
        };
    }

    public string InstanceId { get; }

    public GatewayEndpointDescriptor RealtimeEndpoint { get; }

    public bool IsRuntimeOwner(GatewayEndpointDescriptor? gateway)
    {
        return gateway is not null
            && !string.IsNullOrWhiteSpace(gateway.InstanceId)
            && string.Equals(gateway.InstanceId, InstanceId, StringComparison.Ordinal);
    }

    private static string RealtimeTransportToString(RealtimeTransportKind transport)
    {
        return transport switch
        {
            RealtimeTransportKind.Kcp => "kcp",
            RealtimeTransportKind.WebSocket => "websocket",
            _ => "unknown"
        };
    }
}
