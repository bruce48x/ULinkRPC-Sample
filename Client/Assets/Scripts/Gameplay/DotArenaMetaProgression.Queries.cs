#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SampleClient.Gameplay
{
    internal static partial class DotArenaMetaProgression
    {
        public static DotArenaTaskReadySummary GetClaimableTaskSummary(DotArenaMetaState? state)
        {
            if (state == null)
            {
                return new DotArenaTaskReadySummary();
            }

            NormalizeState(state, state.PlayerId);

            var dailyClaimable = 0;
            foreach (var task in state.DailyTasks)
            {
                if (!task.Claimed && task.Progress >= task.Target)
                {
                    dailyClaimable += 1;
                }
            }

            var newPlayerClaimable = 0;
            foreach (var task in state.NewPlayerTasks)
            {
                if (!task.Claimed && task.Progress >= task.Target)
                {
                    newPlayerClaimable += 1;
                }
            }

            return new DotArenaTaskReadySummary
            {
                DailyClaimableCount = dailyClaimable,
                NewPlayerClaimableCount = newPlayerClaimable,
                TotalClaimableCount = dailyClaimable + newPlayerClaimable
            };
        }

        public static int GetClaimableTaskCount(DotArenaMetaState? state)
        {
            return GetClaimableTaskSummary(state).TotalClaimableCount;
        }

        public static bool HasClaimableTasks(DotArenaMetaState? state)
        {
            return GetClaimableTaskCount(state) > 0;
        }

        public static bool TryGetRecentMatchSummary(DotArenaMetaState? state, out DotArenaRecentMatchSummary summary)
        {
            summary = GetRecentMatchSummary(state);
            return summary.HasRecord;
        }

        public static DotArenaRecentMatchSummary GetRecentMatchSummary(DotArenaMetaState? state)
        {
            if (state == null)
            {
                return new DotArenaRecentMatchSummary();
            }

            NormalizeState(state, state.PlayerId);
            if (state.MatchHistory.Count == 0)
            {
                return new DotArenaRecentMatchSummary();
            }

            var record = state.MatchHistory[0];
            return new DotArenaRecentMatchSummary
            {
                HasRecord = true,
                Mode = record.Mode,
                Result = record.Result,
                Score = record.Score,
                WinnerPlayerId = record.WinnerPlayerId,
                PlayedAtUtcIso = record.PlayedAtUtcIso
            };
        }

        public static DotArenaRecentMatchTrendSummary GetRecentMatchTrendSummary(DotArenaMetaState? state, int sampleCount = 5)
        {
            if (state == null)
            {
                return new DotArenaRecentMatchTrendSummary();
            }

            NormalizeState(state, state.PlayerId);
            if (state.MatchHistory.Count == 0)
            {
                return new DotArenaRecentMatchTrendSummary();
            }

            sampleCount = Math.Clamp(sampleCount, 1, 10);
            var historyCount = Math.Min(sampleCount, state.MatchHistory.Count);
            var wins = 0;
            var losses = 0;
            var scoreSum = 0;
            var bestScore = int.MinValue;
            var form = new List<string>(historyCount);

            for (var i = 0; i < historyCount; i++)
            {
                var record = state.MatchHistory[i];
                var won = string.Equals(record.Result, "Win", StringComparison.OrdinalIgnoreCase);
                form.Add(won ? "W" : "L");
                if (won)
                {
                    wins += 1;
                }
                else
                {
                    losses += 1;
                }

                scoreSum += record.Score;
                if (record.Score > bestScore)
                {
                    bestScore = record.Score;
                }
            }

            var currentStreak = 0;
            var currentStreakType = string.Empty;
            var first = state.MatchHistory[0];
            var leadingWon = string.Equals(first.Result, "Win", StringComparison.OrdinalIgnoreCase);
            for (var i = 0; i < historyCount; i++)
            {
                var record = state.MatchHistory[i];
                var won = string.Equals(record.Result, "Win", StringComparison.OrdinalIgnoreCase);
                if (i == 0)
                {
                    currentStreakType = won ? "Win" : "Loss";
                }

                if (won != leadingWon)
                {
                    break;
                }

                currentStreak += 1;
            }

            var trendLabel = wins > losses
                ? "Hot"
                : losses > wins
                    ? "Cold"
                    : "Balanced";

            return new DotArenaRecentMatchTrendSummary
            {
                HasHistory = true,
                SampleCount = historyCount,
                WinCount = wins,
                LossCount = losses,
                WinRate = historyCount == 0 ? 0f : wins / (float)historyCount,
                AverageScore = historyCount == 0 ? 0 : Mathf.RoundToInt(scoreSum / (float)historyCount),
                BestScore = bestScore < 0 ? 0 : bestScore,
                CurrentStreak = currentStreak,
                CurrentStreakType = currentStreakType,
                TrendLabel = trendLabel,
                FormStrip = string.Join("-", form)
            };
        }

        public static DotArenaLeaderboardSummary GetLeaderboardSummary(DotArenaMetaState? state)
        {
            if (state == null)
            {
                return new DotArenaLeaderboardSummary();
            }

            NormalizeState(state, state.PlayerId);

            var trend = GetRecentMatchTrendSummary(state, 5);
            var totalMatches = Math.Max(0, state.TotalMatches);
            var winRate = totalMatches == 0 ? 0f : state.TotalWins / (float)totalMatches;
            var projectedRank = Math.Max(1, 4 - Math.Min(3, state.TotalWins / 2));

            var entries = new List<DotArenaLeaderboardEntrySummary>
            {
                new()
                {
                    Position = 1,
                    Name = state.PlayerId,
                    Wins = state.TotalWins,
                    Matches = state.TotalMatches,
                    Note = "Local player",
                    IsLocalPlayer = true
                },
                new()
                {
                    Position = 2,
                    Name = "Queue Rival",
                    Wins = Math.Max(0, state.TotalWins - 1),
                    Matches = Math.Max(1, state.TotalMatches - 1),
                    Note = "Matchmaking benchmark"
                },
                new()
                {
                    Position = 3,
                    Name = "Arena Veteran",
                    Wins = Math.Max(0, state.TotalWins / 2),
                    Matches = Math.Max(1, state.TotalMatches - 2),
                    Note = "Baseline leaderboard"
                }
            };

            entries.Sort((left, right) => right.Wins.CompareTo(left.Wins));
            for (var i = 0; i < entries.Count; i++)
            {
                entries[i].Position = i + 1;
            }

            return new DotArenaLeaderboardSummary
            {
                HasProfile = true,
                Title = "Local Leaderboard",
                PlayerLine = $"Player: {state.PlayerId} | Wins: {state.TotalWins} | Matches: {state.TotalMatches} | Win rate: {winRate:P0}",
                RankLine = $"Projected rank: #{projectedRank} of {entries.Count} | Trend: {trend.TrendLabel}",
                TrendLine = trend.HasHistory
                    ? $"Recent form: {trend.FormStrip} | {trend.CurrentStreakType} streak: {trend.CurrentStreak} | Avg score: {trend.AverageScore}"
                    : "Recent form: No history",
                FormLine = trend.HasHistory
                    ? $"Last {trend.SampleCount}: {trend.WinCount}W / {trend.LossCount}L | Best score: {trend.BestScore}"
                    : "Last 0: no matches yet",
                Entries = entries
            };
        }

        public static DotArenaShopAvailabilitySummary GetShopAvailabilitySummary(DotArenaMetaState? state)
        {
            if (state == null)
            {
                return new DotArenaShopAvailabilitySummary();
            }

            NormalizeState(state, state.PlayerId);

            var owned = 0;
            var affordable = 0;
            var affordableAndUnowned = 0;
            DotArenaShopItem? cheapestAffordable = null;
            DotArenaShopItem? cheapestAffordableUnowned = null;

            foreach (var item in ShopCatalog)
            {
                var isOwned = state.OwnedCosmeticIds != null && state.OwnedCosmeticIds.Contains(item.Id);
                if (isOwned)
                {
                    owned += 1;
                }

                if (state.SoftCurrency < item.Price)
                {
                    continue;
                }

                affordable += 1;
                if (!isOwned)
                {
                    affordableAndUnowned += 1;
                }

                if (cheapestAffordable == null || item.Price < cheapestAffordable.Price)
                {
                    cheapestAffordable = item;
                }

                if (!isOwned && (cheapestAffordableUnowned == null || item.Price < cheapestAffordableUnowned.Price))
                {
                    cheapestAffordableUnowned = item;
                }
            }

            return new DotArenaShopAvailabilitySummary
            {
                TotalCatalogCount = ShopCatalog.Length,
                OwnedCount = owned,
                AffordableCount = affordable,
                AffordableAndUnownedCount = affordableAndUnowned,
                CheapestAffordableItem = cheapestAffordable,
                CheapestAffordableUnownedItem = cheapestAffordableUnowned
            };
        }

        public static int GetPurchasableShopItemCount(DotArenaMetaState? state)
        {
            return GetShopAvailabilitySummary(state).AffordableAndUnownedCount;
        }

        public static bool TryGetCheapestPurchasableShopItem(DotArenaMetaState? state, out DotArenaShopItem? item)
        {
            var summary = GetShopAvailabilitySummary(state);
            item = summary.CheapestAffordableUnownedItem;
            return item != null;
        }
    }
}
