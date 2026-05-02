#nullable enable

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

        [RpcMethod(4)]
        ValueTask StartMatchmakingAsync(MatchmakingRequest req);

        [RpcMethod(5)]
        ValueTask CancelMatchmakingAsync(CancelMatchmakingRequest req);

        [RpcMethod(6)]
        ValueTask<RealtimeAttachReply> AttachRealtimeAsync(RealtimeAttachRequest req);
        
        [RpcMethod(2)]
        ValueTask SubmitInput(InputMessage req);

        [RpcMethod(3)]
        ValueTask LogoutAsync(LogoutRequest req);
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

        [RpcPush(4)]
        void OnMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus) { }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class LoginRequest
    {
        [MemoryPackOrder(0)]
        public string Account { get; set; } = "";
        [MemoryPackOrder(1)]
        public string Password { get; set; } = "";
        [MemoryPackOrder(2)]
        public bool GuestLogin { get; set; }
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
        [MemoryPackOrder(4)]
        public string Account { get; set; } = "";
        [MemoryPackOrder(5)]
        public string Password { get; set; } = "";
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
    public partial class LogoutRequest
    {
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class MatchmakingRequest
    {
        [MemoryPackOrder(0)]
        public string PlayerId { get; set; } = "";
        [MemoryPackOrder(1)]
        public string Token { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class CancelMatchmakingRequest
    {
        [MemoryPackOrder(0)]
        public string PlayerId { get; set; } = "";
        [MemoryPackOrder(1)]
        public string Token { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class RealtimeAttachRequest
    {
        [MemoryPackOrder(0)]
        public string PlayerId { get; set; } = "";
        [MemoryPackOrder(1)]
        public string Token { get; set; } = "";
        [MemoryPackOrder(2)]
        public string RoomId { get; set; } = "";
        [MemoryPackOrder(3)]
        public string MatchId { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class RealtimeAttachReply
    {
        [MemoryPackOrder(0)]
        public int Code { get; set; }
        [MemoryPackOrder(1)]
        public string Message { get; set; } = "";
        [MemoryPackOrder(2)]
        public string PlayerId { get; set; } = "";
        [MemoryPackOrder(3)]
        public string RoomId { get; set; } = "";
        [MemoryPackOrder(4)]
        public string MatchId { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class MatchmakingStatusUpdate
    {
        [MemoryPackOrder(0)]
        public MatchmakingState State { get; set; }
        [MemoryPackOrder(1)]
        public string Message { get; set; } = "";
        [MemoryPackOrder(2)]
        public string RoomId { get; set; } = "";
        [MemoryPackOrder(3)]
        public int QueuePosition { get; set; }
        [MemoryPackOrder(4)]
        public int QueueSize { get; set; }
        [MemoryPackOrder(5)]
        public int RoomCapacity { get; set; }
        [MemoryPackOrder(6)]
        public int MatchedPlayerCount { get; set; }
        [MemoryPackOrder(7)]
        public RealtimeConnectionInfo? RealtimeConnection { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class RealtimeConnectionInfo
    {
        [MemoryPackOrder(0)]
        public RealtimeTransportKind Transport { get; set; }
        [MemoryPackOrder(1)]
        public string Host { get; set; } = "";
        [MemoryPackOrder(2)]
        public int Port { get; set; }
        [MemoryPackOrder(3)]
        public string Path { get; set; } = "";
        [MemoryPackOrder(4)]
        public string RoomId { get; set; } = "";
        [MemoryPackOrder(5)]
        public string MatchId { get; set; } = "";
        [MemoryPackOrder(6)]
        public string SessionToken { get; set; } = "";
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
        // Seconds remaining in the current round; 0 when no active round.
        [MemoryPackOrder(6)]
        public int RoundRemainingSeconds { get; set; }
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
        [MemoryPackOrder(11)]
        public int ShieldRemainingSeconds { get; set; }
        [MemoryPackOrder(12)]
        public float Mass { get; set; }
        [MemoryPackOrder(13)]
        public float Radius { get; set; }
        [MemoryPackOrder(14)]
        public float MoveSpeed { get; set; }
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
        ScorePoint = 2,
        Shield = 3,
        BonusScore = 4
    }

    public enum MatchmakingState
    {
        Unknown = 0,
        Queued = 1,
        Searching = 2,
        Matched = 3,
        Canceled = 4,
        Failed = 5
    }

    public enum RealtimeTransportKind
    {
        Unknown = 0,
        WebSocket = 1,
        Kcp = 2
    }
}
