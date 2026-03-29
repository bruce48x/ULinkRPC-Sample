using System;
using Orleans;

namespace Server.Orleans;

internal static class ClusterClientRuntime
{
    private static IClusterClient? _clusterClient;

    public static IGrainFactory GrainFactory =>
        _clusterClient ?? throw new InvalidOperationException("Orleans cluster client has not been initialized.");

    public static void Initialize(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient;
    }
}
