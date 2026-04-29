#nullable enable

using System;
using System.Collections.Generic;

namespace SampleClient.Gameplay
{
    internal static class DotArenaUiTextComposer
    {
        public static string BuildMenuLoginStatusText(bool hasAuthenticatedProfile, string authenticatedPlayerId, int localWinCount)
        {
            if (!hasAuthenticatedProfile || string.IsNullOrWhiteSpace(authenticatedPlayerId))
            {
                return "未登录";
            }

            return $"已登录: {authenticatedPlayerId}   胜场: {localWinCount}";
        }

        public static string BuildSettlementDetail(SessionMode sessionMode, int localScore, int localWinCount, string winnerPlayerId, bool localPlayerWon, ArenaMapVariant mapVariant, ArenaRuleVariant ruleVariant)
        {
            var modeText = sessionMode == SessionMode.SinglePlayer ? "Single-player" : "Multiplayer";
            var resultText = localPlayerWon ? "Victory" : "Defeat";
            var presetLabel = DotArenaSinglePlayerCatalog.GetPresetLabel(mapVariant, ruleVariant);
            var presetLine = sessionMode == SessionMode.SinglePlayer ? $"\nPreset: {presetLabel}" : string.Empty;
            var followupLine = sessionMode == SessionMode.Multiplayer
                ? "\nNext: Return to Lobby to start another online match."
                : $"\nNext: Return to Mode Select or replay {presetLabel}.";
            return $"Mode: {modeText}{presetLine}\nResult: {resultText}\nWinner: {winnerPlayerId}\nScore: {localScore}\nWins: {localWinCount}{followupLine}";
        }

        public static string BuildSettlementRewardSummary(SessionMode sessionMode, DotArenaRewardSummary? lastRewardSummary)
        {
            if (lastRewardSummary == null)
            {
                return sessionMode == SessionMode.Multiplayer
                    ? "Rewards: pending profile sync."
                    : "Rewards: none recorded yet.";
            }

            return $"Rewards: +{lastRewardSummary.ExperienceGained} XP, +{lastRewardSummary.CurrencyGained} Coins, Level {lastRewardSummary.NewLevel}";
        }

        public static string BuildSettlementTaskSummary(DotArenaMetaState? metaState)
        {
            if (metaState == null)
            {
                return "Tasks: no profile data available.";
            }

            var readySummary = DotArenaMetaProgression.GetClaimableTaskSummary(metaState);
            var readyCount = readySummary.TotalClaimableCount;
            if (readyCount <= 0)
            {
                return "Tasks: no claimable tasks right now.";
            }

            var scopeText = readySummary.DailyClaimableCount > 0 && readySummary.NewPlayerClaimableCount > 0
                ? $"Daily {readySummary.DailyClaimableCount}, New {readySummary.NewPlayerClaimableCount}"
                : readySummary.DailyClaimableCount > 0
                    ? $"Daily {readySummary.DailyClaimableCount}"
                    : $"New {readySummary.NewPlayerClaimableCount}";

            return $"Tasks: {readyCount} claimable now ({scopeText}).";
        }

        public static string BuildSettlementNextStepSummary(SessionMode sessionMode, ArenaMapVariant mapVariant, ArenaRuleVariant ruleVariant)
        {
            return sessionMode == SessionMode.Multiplayer
                ? "Next: Return to Lobby, then Start Match to queue again."
                : $"Next: Return to Mode Select or replay {DotArenaSinglePlayerCatalog.GetPresetLabel(mapVariant, ruleVariant)}.";
        }

        public static string BuildDebugPanelDetail(string status, FrontendFlowState flowState, EntryMenuState entryMenuState, SessionMode sessionMode, string localPlayerId, int lastWorldTick, int viewCount, string localPlayerBuffText, string currentEventMessage, string endpoint, bool isConnected, bool isConnecting)
        {
            var mode = sessionMode switch
            {
                SessionMode.SinglePlayer => "Single-player",
                SessionMode.Multiplayer => "Multiplayer",
                _ => "None"
            };

            return
                $"Status: {status}\n" +
                $"Flow: {flowState} / Entry: {entryMenuState}\n" +
                $"Mode: {mode}\n" +
                $"Player: {localPlayerId}\n" +
                $"Hint: W/A/S/D move, eat pellets, avoid larger cells, P debug\n" +
                $"Tick: {lastWorldTick}\n" +
                $"Views: {viewCount}\n" +
                $"Mass: {localPlayerBuffText}\n" +
                $"Event: {currentEventMessage}\n" +
                $"Endpoint: {endpoint}\n" +
                $"Connected: {isConnected} / Connecting: {isConnecting}";
        }

        public static string BuildMatchmakingDetail(SessionMode sessionMode, ArenaMapVariant mapVariant, ArenaRuleVariant ruleVariant, string status, string currentEventMessage)
        {
            return sessionMode == SessionMode.SinglePlayer
                ? $"Preset: {DotArenaSinglePlayerCatalog.GetPresetLabel(mapVariant, ruleVariant)}\nSpawning the local arena and filling the roster with bots."
                : $"{status}\n{currentEventMessage}";
        }

        public static string BuildMetaPlayerSummary(DotArenaMetaState? metaState, bool isInMultiplayerLobby)
        {
            if (metaState == null)
            {
                return "Guest profile";
            }

            return isInMultiplayerLobby
                ? $"{metaState.PlayerId}   Wins {metaState.TotalWins}   Coins {metaState.SoftCurrency}   Online Ready"
                : $"{metaState.PlayerId}   Lv.{metaState.Level}   XP {metaState.Experience}/{GetMetaNextLevelRequirement(metaState.Level)}   Coins {metaState.SoftCurrency}";
        }

        public static string BuildMetaLobbyHighlights(DotArenaMetaState? metaState, bool isInMultiplayerLobby, SinglePlayerMatchPreset previewPreset)
        {
            if (metaState == null)
            {
                return string.Empty;
            }

            var readyTaskCount = DotArenaMetaProgression.GetClaimableTaskCount(metaState);
            var recentSummary = DotArenaMetaProgression.GetRecentMatchSummary(metaState);
            var recentResult = recentSummary.HasRecord
                ? $"{recentSummary.Mode} / {recentSummary.Result}"
                : "No recent result";
            var shopSummary = DotArenaMetaProgression.GetShopAvailabilitySummary(metaState);

            return isInMultiplayerLobby
                ? $"Ready to match now   |   Recent: {recentResult}   |   Claimable tasks: {readyTaskCount}   |   Shop ready: {shopSummary.AffordableAndUnownedCount}"
                : $"Next preset: {DotArenaSinglePlayerCatalog.GetPresetLabel(previewPreset.MapVariant, previewPreset.RuleVariant)}   |   Recent: {recentResult}   |   Claimable tasks: {readyTaskCount}   |   Shop ready: {shopSummary.AffordableAndUnownedCount}";
        }

        public static string BuildMetaProfileDetail(DotArenaMetaState? metaState, bool isInMultiplayerLobby, SinglePlayerMatchPreset previewPreset, DotArenaRewardSummary? lastRewardSummary, string endpoint)
        {
            if (metaState == null)
            {
                return "No profile data loaded.";
            }

            var modeLine = isInMultiplayerLobby
                ? $"Lobby: Multiplayer lobby ready as {metaState.PlayerId}\nEndpoint: {endpoint}\nAction: Start Match to enter online queue"
                : $"Next local preset: {DotArenaSinglePlayerCatalog.GetPresetLabel(previewPreset.MapVariant, previewPreset.RuleVariant)}";

            var lastReward = lastRewardSummary == null
                ? "No recent reward summary."
                : $"Last rewards: +{lastRewardSummary.ExperienceGained} XP, +{lastRewardSummary.CurrencyGained} Coins, Level {lastRewardSummary.NewLevel}";
            return $"Wins: {metaState.TotalWins}\nMatches: {metaState.TotalMatches}\nStreak: {metaState.CurrentLoginStreak}\nEquipped: {metaState.EquippedCosmeticId}\n{modeLine}\n{lastReward}";
        }

        public static string BuildMetaTasksDetail(DotArenaMetaState? metaState)
        {
            if (metaState == null)
            {
                return "No tasks available.";
            }

            var lines = new List<string>();
            var readySummary = DotArenaMetaProgression.GetClaimableTaskSummary(metaState);
            lines.Add($"Ready to claim: {readySummary.TotalClaimableCount} (Daily {readySummary.DailyClaimableCount} / New {readySummary.NewPlayerClaimableCount})");
            foreach (var task in metaState.DailyTasks)
            {
                lines.Add($"Daily: {task.Title} ({task.Progress}/{task.Target}) {(task.Claimed ? "[Claimed]" : string.Empty)}".TrimEnd());
            }

            foreach (var task in metaState.NewPlayerTasks)
            {
                lines.Add($"New: {task.Title} ({task.Progress}/{task.Target}) {(task.Claimed ? "[Claimed]" : string.Empty)}".TrimEnd());
            }

            return lines.Count == 0 ? "No task data." : string.Join("\n", lines);
        }

        public static string BuildMetaShopDetail(DotArenaMetaState? metaState)
        {
            if (metaState == null)
            {
                return "No shop data.";
            }

            var lines = new List<string>();
            var availability = DotArenaMetaProgression.GetShopAvailabilitySummary(metaState);
            var cheapest = availability.CheapestAffordableUnownedItem?.Name ?? "None";
            lines.Add($"Affordable now: {availability.AffordableAndUnownedCount}");
            lines.Add($"Cheapest next item: {cheapest}");
            foreach (var item in DotArenaMetaProgression.GetShopCatalog())
            {
                var state = metaState.OwnedCosmeticIds.Contains(item.Id)
                    ? (metaState.EquippedCosmeticId == item.Id ? "Equipped" : "Owned")
                    : $"{item.Price} Coins";
                lines.Add($"{item.Name}: {state}");
            }

            return string.Join("\n", lines);
        }

        public static string BuildMetaRecordsDetail(DotArenaMetaState? metaState)
        {
            if (metaState == null || metaState.MatchHistory.Count == 0)
            {
                return "No recent matches.";
            }

            var recent = DotArenaMetaProgression.GetRecentMatchSummary(metaState);
            var trend = DotArenaMetaProgression.GetRecentMatchTrendSummary(metaState, 5);
            var lines = new List<string>
            {
                "Recent Match",
                $"Mode: {recent.Mode}",
                $"Result: {recent.Result}",
                $"Score: {recent.Score}",
                $"Winner: {recent.WinnerPlayerId}",
                string.IsNullOrWhiteSpace(recent.PlayedAtUtcIso) ? "Played: Unknown" : $"Played: {recent.PlayedAtUtcIso[..Math.Min(10, recent.PlayedAtUtcIso.Length)]}",
                string.Empty,
                "Trend Summary",
                $"Window: Last {trend.SampleCount}",
                $"Form: {trend.FormStrip}",
                $"Wins / Losses: {trend.WinCount} / {trend.LossCount}",
                $"Trend: {trend.TrendLabel}",
                $"Streak: {trend.CurrentStreakType} {trend.CurrentStreak}",
                $"Avg Score: {trend.AverageScore}",
                $"Best Score: {trend.BestScore}",
                string.Empty,
                "Recent History"
            };

            var count = Math.Min(5, metaState.MatchHistory.Count);
            for (var i = 0; i < count; i++)
            {
                var record = metaState.MatchHistory[i];
                var date = record.PlayedAtUtcIso.Length >= 10 ? record.PlayedAtUtcIso[..10] : record.PlayedAtUtcIso;
                lines.Add($"{date}  {record.Mode}  {record.Result}  Score {record.Score}");
            }

            return string.Join("\n", lines);
        }

        public static string BuildMetaLeaderboardDetail(DotArenaMetaState? metaState)
        {
            if (metaState == null)
            {
                return "No leaderboard data.";
            }

            var summary = DotArenaMetaProgression.GetLeaderboardSummary(metaState);
            var lines = new List<string>
            {
                "Leaderboard Summary",
                summary.PlayerLine,
                summary.RankLine,
                summary.TrendLine,
                summary.FormLine,
                string.Empty,
                summary.Title
            };

            foreach (var entry in summary.Entries)
            {
                var marker = entry.IsLocalPlayer ? " [You]" : string.Empty;
                lines.Add($"{entry.Position}. {entry.Name} - {entry.Wins} wins / {entry.Matches} matches{marker}");
            }

            return string.Join("\n", lines);
        }

        public static string BuildMetaSettingsDetail(DotArenaMetaState? metaState)
        {
            if (metaState == null)
            {
                return "No settings loaded.";
            }

            return $"Master Volume: {metaState.Settings.MasterVolume:0.0}\nMusic Volume: {metaState.Settings.MusicVolume:0.0}\nSfx Volume: {metaState.Settings.SfxVolume:0.0}\nLanguage: {metaState.Settings.Language}\nFullscreen: {metaState.Settings.Fullscreen}";
        }

        public static string BuildMetaFooterHint(bool isInMultiplayerLobby)
        {
            return isInMultiplayerLobby
                ? "Start Match enters queue. Log Out returns to mode select."
                : "Use the top tabs to switch sections. Start from Lobby or Tasks.";
        }

        public static string GetRematchButtonLabel(SessionMode sessionMode)
        {
            return sessionMode == SessionMode.SinglePlayer ? "Play Again" : "Queue Again";
        }

        public static int GetMetaNextLevelRequirement(int level)
        {
            return 100 + ((Math.Max(1, level) - 1) * 25);
        }
    }
}
