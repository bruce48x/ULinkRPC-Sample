using Microsoft.Extensions.Configuration;
using Orleans.Contracts;
using Server.Hosting;
using Shared.Interfaces;

namespace Server.Services;

internal sealed class GatewayNodeIdentity
{
    public GatewayNodeIdentity(IConfiguration configuration, RealtimeRpcServerOptions realtimeOptions)
    {
        InstanceId = configuration["Gateway:NodeId"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(InstanceId))
        {
            InstanceId = $"{Environment.MachineName}-{Environment.ProcessId}";
        }

        RealtimeEndpoint = new GatewayEndpointDescriptor
        {
            InstanceId = InstanceId,
            Transport = RealtimeTransportToString(realtimeOptions.Endpoint.Transport),
            Host = realtimeOptions.Endpoint.Host,
            Port = realtimeOptions.Endpoint.Port,
            Path = realtimeOptions.Endpoint.Path
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

    private static string RealtimeTransportToString(string transport) =>
        string.IsNullOrWhiteSpace(transport) ? "unknown" : transport.ToLowerInvariant();
}
