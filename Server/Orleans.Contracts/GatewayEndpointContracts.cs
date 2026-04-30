namespace Orleans.Contracts;

[GenerateSerializer]
public sealed class GatewayEndpointDescriptor
{
    [Id(0)]
    public string InstanceId { get; set; } = "";

    [Id(1)]
    public string Transport { get; set; } = "";

    [Id(2)]
    public string Host { get; set; } = "";

    [Id(3)]
    public int Port { get; set; }

    [Id(4)]
    public string Path { get; set; } = "";
}
