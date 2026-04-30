#nullable enable

using System;
using System.Collections.Generic;
using Shared.Interfaces;

namespace SampleClient.Gameplay
{
    internal readonly struct DotArenaConnectionFeedback
    {
        public DotArenaConnectionFeedback(string status, string message)
        {
            Status = status;
            Message = message;
        }

        public string Status { get; }
        public string Message { get; }
    }

    internal readonly struct DotArenaMatchmakingViewState
    {
        public DotArenaMatchmakingViewState(
            FrontendFlowState flowState,
            EntryMenuState entryMenuState,
            string status,
            string eventMessage,
            bool clearPendingCancelRequest)
        {
            FlowState = flowState;
            EntryMenuState = entryMenuState;
            Status = status;
            EventMessage = eventMessage;
            ClearPendingCancelRequest = clearPendingCancelRequest;
        }

        public FrontendFlowState FlowState { get; }
        public EntryMenuState EntryMenuState { get; }
        public string Status { get; }
        public string EventMessage { get; }
        public bool ClearPendingCancelRequest { get; }
    }

    internal static class DotArenaMultiplayerFlow
    {
        public static DotArenaConnectionFeedback BuildConnectionFailure(Exception ex)
        {
            return new DotArenaConnectionFeedback(
                IsServerUnavailableError(ex) ? "服务器暂时不可用" : "登录失败",
                BuildFriendlyConnectionMessage(ex));
        }

        public static DotArenaMatchmakingViewState BuildMatchmakingViewState(MatchmakingStatusUpdate matchmakingStatus, bool cancelRequestPending)
        {
            var statusText = BuildMatchmakingStatusText(matchmakingStatus);
            var detailText = BuildMatchmakingStatusDetail(matchmakingStatus);

            return matchmakingStatus.State switch
            {
                MatchmakingState.Canceled => new DotArenaMatchmakingViewState(
                    FrontendFlowState.Entry,
                    EntryMenuState.MultiplayerLobby,
                    statusText,
                    string.IsNullOrWhiteSpace(detailText) ? "已返回联机大厅" : detailText,
                    clearPendingCancelRequest: true),
                MatchmakingState.Failed => new DotArenaMatchmakingViewState(
                    FrontendFlowState.Entry,
                    EntryMenuState.MultiplayerLobby,
                    statusText,
                    string.IsNullOrWhiteSpace(detailText) ? "请重新开始匹配" : detailText,
                    clearPendingCancelRequest: true),
                MatchmakingState.Matched => new DotArenaMatchmakingViewState(
                    FrontendFlowState.Matchmaking,
                    EntryMenuState.Hidden,
                    statusText,
                    string.IsNullOrWhiteSpace(detailText) ? "房间已就绪，等待世界状态" : detailText,
                    clearPendingCancelRequest: true),
                MatchmakingState.Queued or MatchmakingState.Searching when cancelRequestPending => new DotArenaMatchmakingViewState(
                    FrontendFlowState.Matchmaking,
                    EntryMenuState.Hidden,
                    "正在取消匹配",
                    "等待服务器确认取消",
                    clearPendingCancelRequest: false),
                _ => new DotArenaMatchmakingViewState(
                    FrontendFlowState.Matchmaking,
                    EntryMenuState.Hidden,
                    statusText,
                    string.IsNullOrWhiteSpace(detailText) ? statusText : detailText,
                    clearPendingCancelRequest: false)
            };
        }

        private static string BuildFriendlyConnectionMessage(Exception ex)
        {
            if (IsServerUnavailableError(ex))
            {
                return "服务器正在启动或房间服务暂时异常，请稍后重试。";
            }

            if (IsNetworkConnectError(ex))
            {
                return "无法连接到服务器，请检查网络或确认服务端已启动。";
            }

            return "登录过程中发生错误，请稍后重试。";
        }

        private static bool IsServerUnavailableError(Exception ex)
        {
            return ex is InvalidOperationException invalidOperationException &&
                   invalidOperationException.Message.Contains("RPC failed", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNetworkConnectError(Exception ex)
        {
            if (ex is TimeoutException)
            {
                return true;
            }

            var message = ex.Message;
            return message.Contains("actively refused", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("Unable to connect", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("连接", StringComparison.OrdinalIgnoreCase) && message.Contains("失败", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildMatchmakingStatusText(MatchmakingStatusUpdate matchmakingStatus)
        {
            if (!string.IsNullOrWhiteSpace(matchmakingStatus.Message))
            {
                return matchmakingStatus.Message;
            }

            return matchmakingStatus.State switch
            {
                MatchmakingState.Queued when matchmakingStatus.QueueSize > 0 => $"排队中 {matchmakingStatus.QueuePosition}/{matchmakingStatus.QueueSize}",
                MatchmakingState.Queued => "排队中",
                MatchmakingState.Searching when matchmakingStatus.MatchedPlayerCount > 0 && matchmakingStatus.RoomCapacity > 0 => $"匹配中 {matchmakingStatus.MatchedPlayerCount}/{matchmakingStatus.RoomCapacity}",
                MatchmakingState.Searching => "匹配中",
                MatchmakingState.Matched when !string.IsNullOrWhiteSpace(matchmakingStatus.RoomId) => $"已进入房间 {matchmakingStatus.RoomId}",
                MatchmakingState.Matched => "已进入房间",
                MatchmakingState.Canceled => "已取消匹配",
                MatchmakingState.Failed => "匹配失败",
                _ => "等待匹配"
            };
        }

        private static string BuildMatchmakingStatusDetail(MatchmakingStatusUpdate matchmakingStatus)
        {
            var details = new List<string>();

            if (matchmakingStatus.QueuePosition > 0 && matchmakingStatus.QueueSize > 0)
            {
                details.Add($"Queue {matchmakingStatus.QueuePosition}/{matchmakingStatus.QueueSize}");
            }

            if (matchmakingStatus.MatchedPlayerCount > 0 && matchmakingStatus.RoomCapacity > 0)
            {
                details.Add($"Room {matchmakingStatus.MatchedPlayerCount}/{matchmakingStatus.RoomCapacity}");
            }

            if (!string.IsNullOrWhiteSpace(matchmakingStatus.RoomId))
            {
                details.Add($"Room {matchmakingStatus.RoomId}");
            }

            if (matchmakingStatus.RealtimeConnection is { } realtimeConnection)
            {
                var transport = realtimeConnection.Transport switch
                {
                    RealtimeTransportKind.Kcp => "KCP",
                    RealtimeTransportKind.WebSocket => "WS",
                    _ => "RT"
                };

                if (!string.IsNullOrWhiteSpace(realtimeConnection.Host) && realtimeConnection.Port > 0)
                {
                    details.Add($"{transport} {realtimeConnection.Host}:{realtimeConnection.Port}");
                }
                else
                {
                    details.Add($"{transport} ready");
                }
            }

            if (!string.IsNullOrWhiteSpace(matchmakingStatus.Message))
            {
                details.Add(matchmakingStatus.Message);
            }

            return details.Count == 0 ? string.Empty : string.Join("  |  ", details);
        }
    }
}
