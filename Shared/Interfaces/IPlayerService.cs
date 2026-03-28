using System.Collections.Generic;
using System.Threading.Tasks;
using MemoryPack;
using ULinkRPC.Core;
using UnityEngine;

namespace Shared.Interfaces
{
    [RpcService(1, Callback = typeof(IPlayerCallback))]
    public interface IPlayerService
    {
        [RpcMethod(1)]
        ValueTask<LoginReply> LoginAsync(LoginRequest req);
        
        [RpcMethod(2)]
        ValueTask Move(MoveRequest req);
    }

    [RpcCallback(typeof(IPlayerService))]
    public interface IPlayerCallback
    {
        [RpcPush(1)]
        void OnMove(PlayerPositions playerPositions);
    }

    [MemoryPackable]
    public partial class LoginRequest
    {
        public string Account { get; set; } = "";
        public string Password { get; set; } = "";
    }

    [MemoryPackable]
    public partial class LoginReply
    {
        public int Code { get; set; }
        public string Token { get; set; } = "";
    }

    [MemoryPackable]
    public partial class PlayerPosition
    {
        public string PlayerId { get; set; } = "";
        public Vector2 Position { get; set; }
    }
    
    
    [MemoryPackable]
    public partial class MoveRequest
    {
        public string PlayerId { get; set; } = "";
        public int Direction { get; set; }
    }

    [MemoryPackable]
    public partial class PlayerPositions
    {
        public List<PlayerPosition> playerPositions { get; set; }
    }
}