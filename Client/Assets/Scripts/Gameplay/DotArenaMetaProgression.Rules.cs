#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SampleClient.Gameplay
{
    internal static partial class DotArenaMetaProgression
    {
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
        private static int GetExperienceForNextLevel(int level) => 100 + ((Math.Max(1, level) - 1) * 25);
    }
}
