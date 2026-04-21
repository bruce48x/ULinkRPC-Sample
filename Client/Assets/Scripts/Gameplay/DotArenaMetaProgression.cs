#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SampleClient.Gameplay
{
    [Serializable]
    internal sealed class DotArenaMetaState
    {
        public string PlayerId = "Guest";
        public int Level = 1;
        public int Experience;
        public int SoftCurrency;
        public int TotalWins;
        public int TotalMatches;
        public int CurrentLoginStreak;
        public string LastLoginDateIso = string.Empty;
        public bool ClaimedFirstWinToday;
        public string LastFirstWinClaimDateIso = string.Empty;
        public string EquippedCosmeticId = "skin_default";
        public List<string> OwnedCosmeticIds = new() { "skin_default" };
        public List<DotArenaTaskProgress> DailyTasks = new();
        public List<DotArenaTaskProgress> NewPlayerTasks = new();
        public List<DotArenaMatchRecord> MatchHistory = new();
        public DotArenaSettings Settings = new();
    }

    [Serializable]
    internal sealed class DotArenaTaskProgress
    {
        public string TaskId = string.Empty;
        public string Title = string.Empty;
        public int Target;
        public int Progress;
        public int RewardCurrency;
        public int RewardExperience;
        public bool Claimed;
        public bool IsDaily;
    }

    [Serializable]
    internal sealed class DotArenaMatchRecord
    {
        public string Mode = string.Empty;
        public string Result = string.Empty;
        public int Score;
        public string WinnerPlayerId = string.Empty;
        public string PlayedAtUtcIso = string.Empty;
    }

    [Serializable]
    internal sealed class DotArenaSettings
    {
        public float MasterVolume = 1f;
        public float MusicVolume = 0.8f;
        public float SfxVolume = 0.9f;
        public bool Fullscreen;
        public string Language = "zh-CN";
    }

    internal sealed class DotArenaRewardSummary
    {
        public int ExperienceGained { get; set; }
        public int CurrencyGained { get; set; }
        public bool ClaimedFirstWinReward { get; set; }
        public int NewLevel { get; set; }
    }

    internal sealed class DotArenaShopItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Price { get; set; }
        public bool IsStarterOffer { get; set; }
    }

    internal sealed class DotArenaTaskReadySummary
    {
        public int DailyClaimableCount { get; set; }
        public int NewPlayerClaimableCount { get; set; }
        public int TotalClaimableCount { get; set; }
    }

    internal sealed class DotArenaRecentMatchSummary
    {
        public bool HasRecord { get; set; }
        public string Mode { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public int Score { get; set; }
        public string WinnerPlayerId { get; set; } = string.Empty;
        public string PlayedAtUtcIso { get; set; } = string.Empty;
    }

    internal sealed class DotArenaRecentMatchTrendSummary
    {
        public bool HasHistory { get; set; }
        public int SampleCount { get; set; }
        public int WinCount { get; set; }
        public int LossCount { get; set; }
        public float WinRate { get; set; }
        public int AverageScore { get; set; }
        public int BestScore { get; set; }
        public int CurrentStreak { get; set; }
        public string CurrentStreakType { get; set; } = string.Empty;
        public string TrendLabel { get; set; } = string.Empty;
        public string FormStrip { get; set; } = string.Empty;
    }

    internal sealed class DotArenaLeaderboardEntrySummary
    {
        public int Position { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Wins { get; set; }
        public int Matches { get; set; }
        public string Note { get; set; } = string.Empty;
        public bool IsLocalPlayer { get; set; }
    }

    internal sealed class DotArenaLeaderboardSummary
    {
        public bool HasProfile { get; set; }
        public string Title { get; set; } = string.Empty;
        public string PlayerLine { get; set; } = string.Empty;
        public string RankLine { get; set; } = string.Empty;
        public string TrendLine { get; set; } = string.Empty;
        public string FormLine { get; set; } = string.Empty;
        public List<DotArenaLeaderboardEntrySummary> Entries { get; set; } = new();
    }

    internal sealed class DotArenaShopAvailabilitySummary
    {
        public int TotalCatalogCount { get; set; }
        public int OwnedCount { get; set; }
        public int AffordableCount { get; set; }
        public int AffordableAndUnownedCount { get; set; }
        public DotArenaShopItem? CheapestAffordableItem { get; set; }
        public DotArenaShopItem? CheapestAffordableUnownedItem { get; set; }
    }

    internal static class DotArenaMetaProgression
    {
        private static readonly DotArenaShopItem[] ShopCatalog =
        {
            new() { Id = "skin_default", Name = "Default Skin", Price = 0 },
            new() { Id = "skin_crimson", Name = "Crimson Skin", Price = 120 },
            new() { Id = "skin_glacier", Name = "Glacier Skin", Price = 180 },
            new() { Id = "skin_sunburst", Name = "Sunburst Skin", Price = 240, IsStarterOffer = true }
        };

        private static string LegacyStatePath => Path.Combine(Application.persistentDataPath, "dotarena_meta.json");

        public static DotArenaMetaState LoadOrCreate(string playerId)
        {
            var resolvedPlayerId = ResolvePlayerId(playerId);
            var state = TryLoadState(GetStatePath(resolvedPlayerId)) ?? TryLoadLegacyState(resolvedPlayerId) ?? new DotArenaMetaState();
            NormalizeState(state, resolvedPlayerId);
            HandleLogin(state);
            Save(state);
            return state;
        }

        public static void Save(DotArenaMetaState state)
        {
            try
            {
                if (state == null)
                {
                    return;
                }

                NormalizeState(state, state.PlayerId);
                var path = GetStatePath(state.PlayerId);
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, JsonUtility.ToJson(state, true));
            }
            catch
            {
            }
        }

        public static bool TryClaimTaskById(DotArenaMetaState state, string taskId) => ClaimTask(state, taskId);

        public static bool TryPurchaseAndOptionallyEquip(DotArenaMetaState state, string itemId, bool equipAfterPurchase = true)
        {
            if (state == null)
            {
                return false;
            }

            NormalizeState(state, state.PlayerId);
            var item = FindItem(itemId);
            if (item == null)
            {
                return false;
            }

            var owned = state.OwnedCosmeticIds != null && state.OwnedCosmeticIds.Contains(item.Id);
            if (!owned)
            {
                if (state.SoftCurrency < item.Price)
                {
                    return false;
                }

                state.SoftCurrency -= item.Price;
                state.OwnedCosmeticIds ??= new List<string>();
                state.OwnedCosmeticIds.Add(item.Id);
            }

            if (equipAfterPurchase)
            {
                state.EquippedCosmeticId = item.Id;
            }

            Save(state);
            return true;
        }

        public static bool SetLanguage(DotArenaMetaState state, string language)
        {
            if (state == null)
            {
                return false;
            }

            NormalizeState(state, state.PlayerId);

            var normalized = NormalizeLanguage(language);
            if (string.Equals(state.Settings.Language, normalized, StringComparison.Ordinal))
            {
                return true;
            }

            state.Settings.Language = normalized;
            Save(state);
            return true;
        }

        public static float AdjustMasterVolume(DotArenaMetaState state, float delta)
        {
            if (state == null)
            {
                return 0f;
            }

            NormalizeState(state, state.PlayerId);
            state.Settings.MasterVolume = Mathf.Clamp01(state.Settings.MasterVolume + delta);
            Save(state);
            return state.Settings.MasterVolume;
        }

        public static float AdjustMusicVolume(DotArenaMetaState state, float delta)
        {
            if (state == null)
            {
                return 0f;
            }

            NormalizeState(state, state.PlayerId);
            state.Settings.MusicVolume = Mathf.Clamp01(state.Settings.MusicVolume + delta);
            Save(state);
            return state.Settings.MusicVolume;
        }

        public static float AdjustSfxVolume(DotArenaMetaState state, float delta)
        {
            if (state == null)
            {
                return 0f;
            }

            NormalizeState(state, state.PlayerId);
            state.Settings.SfxVolume = Mathf.Clamp01(state.Settings.SfxVolume + delta);
            Save(state);
            return state.Settings.SfxVolume;
        }

        public static bool ToggleFullscreen(DotArenaMetaState state)
        {
            if (state == null)
            {
                return false;
            }

            NormalizeState(state, state.PlayerId);
            state.Settings.Fullscreen = !state.Settings.Fullscreen;
            Save(state);
            return state.Settings.Fullscreen;
        }

        public static IReadOnlyList<DotArenaShopItem> GetShopCatalog() => ShopCatalog;

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

        public static bool TryPurchase(DotArenaMetaState state, string itemId)
        {
            return TryPurchaseAndOptionallyEquip(state, itemId, false);
        }

        public static void Equip(DotArenaMetaState state, string itemId)
        {
            if (state == null)
            {
                return;
            }

            NormalizeState(state, state.PlayerId);
            if (state.OwnedCosmeticIds == null || !state.OwnedCosmeticIds.Contains(itemId))
            {
                return;
            }

            state.EquippedCosmeticId = itemId;
            Save(state);
        }

        public static DotArenaRewardSummary ApplyMatchResult(
            DotArenaMetaState state,
            SessionMode mode,
            string winnerPlayerId,
            string localPlayerId,
            int score)
        {
            if (state == null)
            {
                return new DotArenaRewardSummary();
            }

            NormalizeState(state, state.PlayerId);
            var won = string.Equals(winnerPlayerId, localPlayerId, StringComparison.Ordinal);
            state.TotalMatches += 1;
            if (won)
            {
                state.TotalWins += 1;
            }

            var exp = 20 + Math.Max(0, score * 5) + (won ? 30 : 0);
            var currency = 15 + Math.Max(0, score * 2) + (won ? 20 : 0);
            var claimedFirstWin = false;
            var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
            if (won && !string.Equals(state.LastFirstWinClaimDateIso, today, StringComparison.Ordinal))
            {
                state.LastFirstWinClaimDateIso = today;
                currency += 50;
                exp += 40;
                claimedFirstWin = true;
            }

            state.Experience += exp;
            state.SoftCurrency += currency;
            while (state.Experience >= GetExperienceForNextLevel(state.Level))
            {
                state.Experience -= GetExperienceForNextLevel(state.Level);
                state.Level += 1;
            }

            state.MatchHistory.Insert(0, new DotArenaMatchRecord
            {
                Mode = mode == SessionMode.SinglePlayer ? "Single-player" : "Multiplayer",
                Result = won ? "Win" : "Loss",
                Score = score,
                WinnerPlayerId = winnerPlayerId,
                PlayedAtUtcIso = DateTime.UtcNow.ToString("O")
            });

            if (state.MatchHistory.Count > 20)
            {
                state.MatchHistory.RemoveRange(20, state.MatchHistory.Count - 20);
            }

            IncrementTask(state, "daily_play_matches", 1);
            IncrementTask(state, "daily_collect_score", Math.Max(1, score));
            if (won)
            {
                IncrementTask(state, "daily_get_win", 1);
                IncrementTask(state, "newbie_first_win", 1);
            }
            IncrementTask(state, "newbie_play_match", 1);
            IncrementTask(state, "newbie_score_points", Math.Max(1, score));

            Save(state);
            return new DotArenaRewardSummary
            {
                ExperienceGained = exp,
                CurrencyGained = currency,
                ClaimedFirstWinReward = claimedFirstWin,
                NewLevel = state.Level
            };
        }

        private static void HandleLogin(DotArenaMetaState state)
        {
            var today = DateTime.UtcNow.Date;
            var todayIso = today.ToString("yyyy-MM-dd");
            if (string.Equals(state.LastLoginDateIso, todayIso, StringComparison.Ordinal))
            {
                return;
            }

            if (DateTime.TryParse(state.LastLoginDateIso, out var lastLogin) && lastLogin.Date == today.AddDays(-1))
            {
                state.CurrentLoginStreak += 1;
            }
            else
            {
                state.CurrentLoginStreak = 1;
            }

            state.LastLoginDateIso = todayIso;
            state.SoftCurrency += 10 * state.CurrentLoginStreak;
        }

        private static void EnsureTaskLists(DotArenaMetaState state)
        {
            if (state.DailyTasks.Count == 0)
            {
                state.DailyTasks.Add(new DotArenaTaskProgress
                {
                    TaskId = "daily_play_matches",
                    Title = "Play 3 matches",
                    Target = 3,
                    RewardCurrency = 60,
                    RewardExperience = 30,
                    IsDaily = true
                });
                state.DailyTasks.Add(new DotArenaTaskProgress
                {
                    TaskId = "daily_get_win",
                    Title = "Win 1 match",
                    Target = 1,
                    RewardCurrency = 80,
                    RewardExperience = 40,
                    IsDaily = true
                });
                state.DailyTasks.Add(new DotArenaTaskProgress
                {
                    TaskId = "daily_collect_score",
                    Title = "Earn 10 score",
                    Target = 10,
                    RewardCurrency = 70,
                    RewardExperience = 35,
                    IsDaily = true
                });
            }

            if (state.NewPlayerTasks.Count == 0)
            {
                state.NewPlayerTasks.Add(new DotArenaTaskProgress
                {
                    TaskId = "newbie_play_match",
                    Title = "Play your first match",
                    Target = 1,
                    RewardCurrency = 50,
                    RewardExperience = 40
                });
                state.NewPlayerTasks.Add(new DotArenaTaskProgress
                {
                    TaskId = "newbie_first_win",
                    Title = "Get your first win",
                    Target = 1,
                    RewardCurrency = 100,
                    RewardExperience = 60
                });
                state.NewPlayerTasks.Add(new DotArenaTaskProgress
                {
                    TaskId = "newbie_score_points",
                    Title = "Earn 15 total score",
                    Target = 15,
                    RewardCurrency = 80,
                    RewardExperience = 50
                });
            }
        }

        private static void IncrementTask(DotArenaMetaState state, string taskId, int value)
        {
            foreach (var task in state.DailyTasks)
            {
                if (task.TaskId == taskId && !task.Claimed)
                {
                    task.Progress = Math.Min(task.Target, task.Progress + value);
                }
            }

            foreach (var task in state.NewPlayerTasks)
            {
                if (task.TaskId == taskId && !task.Claimed)
                {
                    task.Progress = Math.Min(task.Target, task.Progress + value);
                }
            }
        }

        public static bool ClaimTask(DotArenaMetaState state, string taskId)
        {
            if (state == null)
            {
                return false;
            }

            NormalizeState(state, state.PlayerId);
            foreach (var task in state.DailyTasks)
            {
                if (TryClaim(state, task, taskId))
                {
                    Save(state);
                    return true;
                }
            }

            foreach (var task in state.NewPlayerTasks)
            {
                if (TryClaim(state, task, taskId))
                {
                    Save(state);
                    return true;
                }
            }

            return false;
        }

        private static bool TryClaim(DotArenaMetaState state, DotArenaTaskProgress task, string taskId)
        {
            if (task.TaskId != taskId || task.Claimed || task.Progress < task.Target)
            {
                return false;
            }

            task.Claimed = true;
            state.SoftCurrency += task.RewardCurrency;
            state.Experience += task.RewardExperience;
            while (state.Experience >= GetExperienceForNextLevel(state.Level))
            {
                state.Experience -= GetExperienceForNextLevel(state.Level);
                state.Level += 1;
            }

            return true;
        }

        private static DotArenaMetaState? TryLoadState(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                return JsonUtility.FromJson<DotArenaMetaState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }

        private static DotArenaMetaState? TryLoadLegacyState(string playerId)
        {
            var legacy = TryLoadState(LegacyStatePath);
            if (legacy == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(legacy.PlayerId) && !string.Equals(ResolvePlayerId(legacy.PlayerId), playerId, StringComparison.Ordinal))
            {
                return null;
            }

            return legacy;
        }

        private static void NormalizeState(DotArenaMetaState state, string playerId)
        {
            state.PlayerId = ResolvePlayerId(string.IsNullOrWhiteSpace(playerId) ? state.PlayerId : playerId);
            state.Settings ??= new DotArenaSettings();
            state.Settings.Language = NormalizeLanguage(state.Settings.Language);
            state.Settings.MasterVolume = Mathf.Clamp01(state.Settings.MasterVolume);
            state.Settings.MusicVolume = Mathf.Clamp01(state.Settings.MusicVolume);
            state.Settings.SfxVolume = Mathf.Clamp01(state.Settings.SfxVolume);
            state.OwnedCosmeticIds ??= new List<string>();
            state.DailyTasks ??= new List<DotArenaTaskProgress>();
            state.NewPlayerTasks ??= new List<DotArenaTaskProgress>();
            state.MatchHistory ??= new List<DotArenaMatchRecord>();
            if (!state.OwnedCosmeticIds.Contains("skin_default"))
            {
                state.OwnedCosmeticIds.Insert(0, "skin_default");
            }

            if (string.IsNullOrWhiteSpace(state.EquippedCosmeticId) || !state.OwnedCosmeticIds.Contains(state.EquippedCosmeticId))
            {
                state.EquippedCosmeticId = "skin_default";
            }

            EnsureTaskLists(state);
        }

        private static string ResolvePlayerId(string playerId)
        {
            return string.IsNullOrWhiteSpace(playerId) ? "Guest" : playerId.Trim();
        }

        private static string NormalizeLanguage(string language)
        {
            return string.IsNullOrWhiteSpace(language) ? "zh-CN" : language.Trim();
        }

        private static string GetStatePath(string playerId)
        {
            return Path.Combine(Application.persistentDataPath, $"dotarena_meta_{SanitizePathSegment(playerId)}.json");
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Guest";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var buffer = value.Trim().ToCharArray();
            for (var i = 0; i < buffer.Length; i++)
            {
                var ch = buffer[i];
                if (Array.IndexOf(invalidChars, ch) >= 0 || char.IsControl(ch) || char.IsWhiteSpace(ch))
                {
                    buffer[i] = '_';
                }
            }

            var sanitized = new string(buffer).Trim('_');
            return string.IsNullOrWhiteSpace(sanitized) ? "Guest" : sanitized;
        }

        private static DotArenaShopItem? FindItem(string itemId)
        {
            foreach (var item in ShopCatalog)
            {
                if (item.Id == itemId)
                {
                    return item;
                }
            }

            return null;
        }

        private static int GetExperienceForNextLevel(int level) => 100 + ((Math.Max(1, level) - 1) * 25);
    }
}
