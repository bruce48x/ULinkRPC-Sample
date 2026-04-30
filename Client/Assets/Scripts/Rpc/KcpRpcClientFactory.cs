#nullable enable

using System;
using ULinkRPC.Client;
using ULinkRPC.Core;
using ULinkRPC.Serializer.MemoryPack;
using ULinkRPC.Transport.Kcp;

namespace Rpc
{
    public static class KcpRpcClientFactory
    {
        public static RpcClient Create(string host, int port, RpcClient.RpcCallbackBindings callbacks)
        {
            return new RpcClient(
                new RpcClientOptions(
                    new KcpTransport(host, port),
                    new MemoryPackRpcSerializer())
                {
                    KeepAlive = new RpcKeepAliveOptions
                    {
                        Enabled = true,
                        Interval = TimeSpan.FromSeconds(2),
                        Timeout = TimeSpan.FromSeconds(6)
                    }
                },
                callbacks);
        }
    }
}
