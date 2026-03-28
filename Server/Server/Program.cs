using ULinkRPC.Core;
using ULinkRPC.Server;
using ULinkRPC.Serializer.MemoryPack;
using ULinkRPC.Transport.WebSocket;

var commandLineArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
var builder = RpcServerHostBuilder.Create()
    .UseCommandLine(commandLineArgs)
    .UseSerializer(new MemoryPackRpcSerializer());    
builder.UseAcceptor(async ct => await WsConnectionAcceptor.CreateAsync(builder.ResolvePort(20000), "/ws", ct));

await builder.RunAsync();
