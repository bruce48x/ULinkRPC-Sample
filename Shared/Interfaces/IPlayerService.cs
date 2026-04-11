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
        ValueTask SubmitInput(InputMessage req);

        [RpcMethod(3)]
        ValueTask LogoutAsync();
    }

    [RpcCallback(typeof(IPlayerService))]
    public interface IPlayerCallback
    {
        [RpcPush(1)]
        void OnWorldState(WorldState worldState);

        [RpcPush(2)]
        void OnPlayerDead(PlayerDead deadEvent);

        [RpcPush(3)]
        void OnMatchEnd(MatchEnd matchEnd);
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class LoginRequest
    {
        [MemoryPackOrder(0)]
        public string Account { get; set; } = "";
        [MemoryPackOrder(1)]
        public string Password { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class LoginReply
    {
        [MemoryPackOrder(0)]
        public int Code { get; set; }
        [MemoryPackOrder(1)]
        public string Token { get; set; } = "";
        [MemoryPackOrder(2)]
        public string PlayerId { get; set; } = "";
        [MemoryPackOrder(3)]
        public int WinCount { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class InputMessage
    {
        [MemoryPackOrder(0)]
        public string PlayerId { get; set; } = "";
        [MemoryPackOrder(1)]
        public float MoveX { get; set; }
        [MemoryPackOrder(2)]
        public float MoveY { get; set; }
        [MemoryPackOrder(3)]
        public bool Dash { get; set; }
        [MemoryPackOrder(4)]
        public int Tick { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class WorldState
    {
        [MemoryPackOrder(0)]
        public int Tick { get; set; }
        [MemoryPackOrder(1)]
        public int RespawnDelaySeconds { get; set; }
        [MemoryPackOrder(2)]
        public List<PlayerState> Players { get; set; } = new();
        [MemoryPackOrder(3)]
        public List<PickupState> Pickups { get; set; } = new();
        [MemoryPackOrder(4)]
        public float ArenaHalfExtentX { get; set; }
        [MemoryPackOrder(5)]
        public float ArenaHalfExtentY { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class PlayerState
    {
        [MemoryPackOrder(0)]
        public string PlayerId { get; set; } = "";
        [MemoryPackOrder(1)]
        public float X { get; set; }
        [MemoryPackOrder(2)]
        public float Y { get; set; }
        [MemoryPackOrder(3)]
        public float Vx { get; set; }
        [MemoryPackOrder(4)]
        public float Vy { get; set; }
        [MemoryPackOrder(5)]
        public PlayerLifeState State { get; set; }
        [MemoryPackOrder(6)]
        public bool Alive { get; set; }
        [MemoryPackOrder(7)]
        public int RespawnRemainingSeconds { get; set; }
        [MemoryPackOrder(8)]
        public int Score { get; set; }
        [MemoryPackOrder(9)]
        public int SpeedBoostRemainingSeconds { get; set; }
        [MemoryPackOrder(10)]
        public int KnockbackBoostRemainingSeconds { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class PickupState
    {
        [MemoryPackOrder(0)]
        public PickupType Type { get; set; }
        [MemoryPackOrder(1)]
        public float X { get; set; }
        [MemoryPackOrder(2)]
        public float Y { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class PlayerDead
    {
        [MemoryPackOrder(0)]
        public string PlayerId { get; set; } = "";
        [MemoryPackOrder(1)]
        public int Tick { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class MatchEnd
    {
        [MemoryPackOrder(0)]
        public string WinnerPlayerId { get; set; } = "";
        [MemoryPackOrder(1)]
        public int Tick { get; set; }
    }

    public enum PlayerLifeState
    {
        Idle = 0,
        Move = 1,
        Dash = 2,
        Stunned = 3,
        Dead = 4
    }

    public enum PickupType
    {
        SpeedBoost = 0,
        KnockbackBoost = 1,
        ScorePoint = 2
    }
}
