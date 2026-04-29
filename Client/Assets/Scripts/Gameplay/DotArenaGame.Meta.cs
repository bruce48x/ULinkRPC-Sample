#nullable enable

using System;
using System.Collections.Generic;

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        private void HandleLobbyPresetAction(bool isPrimaryAction)
        {
            if (IsInMultiplayerLobby())
            {
                if (isPrimaryAction)
                {
                    BeginMultiplayerMatchmaking();
                }
                else
                {
                    LogOutToModeSelect();
                }

                return;
            }

            if (!isPrimaryAction)
            {
                var previewPreset = DotArenaSinglePlayerCatalog.PeekPreset(_singlePlayerPlaylistIndex);
                PushEvent($"Next local preset: {DotArenaSinglePlayerCatalog.GetPresetLabel(previewPreset.MapVariant, previewPreset.RuleVariant)}", 4f);
                return;
            }

            var selectedPreset = DotArenaSinglePlayerCatalog.AdvancePresetSelection(ref _singlePlayerPlaylistIndex);
            PushEvent($"Preset switched to {DotArenaSinglePlayerCatalog.GetPresetLabel(selectedPreset.MapVariant, selectedPreset.RuleVariant)}", 4f);
        }

        private bool IsInMultiplayerLobby()
        {
            return _flowState == FrontendFlowState.Entry &&
                   _entryMenuState == EntryMenuState.MultiplayerLobby &&
                   _sessionMode == SessionMode.Multiplayer &&
                   _hasAuthenticatedProfile &&
                   !string.IsNullOrWhiteSpace(_authenticatedPlayerId);
        }

        private void LogOutToModeSelect()
        {
            _settlementSummary = null;
            _flowState = FrontendFlowState.Entry;
            _entryMenuState = EntryMenuState.ModeSelect;
            _sessionMode = SessionMode.None;
            _hasAuthenticatedProfile = false;
            _authenticatedPlayerId = string.Empty;
            _localPlayerId = string.Empty;
            _localWinCount = 0;
            _status = "选择模式";
            _eventMessage = "已退出联机大厅";
            PushEvent("已退出联机大厅");
        }

        private void HandleTaskLobbyAction(bool isPrimaryAction)
        {
            if (_metaState == null)
            {
                return;
            }

            var task = FindReadyTask(isPrimaryAction ? _metaState.DailyTasks : _metaState.NewPlayerTasks)
                ?? FindReadyTask(isPrimaryAction ? _metaState.NewPlayerTasks : _metaState.DailyTasks);
            if (task == null)
            {
                PushEvent("No claimable task right now.");
                return;
            }

            if (DotArenaMetaProgression.TryClaimTaskById(_metaState, task.TaskId))
            {
                PushEvent($"Claimed task: {task.Title}");
            }
        }

        private void HandleShopLobbyAction(bool isPrimaryAction)
        {
            if (_metaState == null)
            {
                return;
            }

            if (isPrimaryAction)
            {
                foreach (var item in DotArenaMetaProgression.GetShopCatalog())
                {
                    if (_metaState.OwnedCosmeticIds.Contains(item.Id))
                    {
                        continue;
                    }

                    if (DotArenaMetaProgression.TryPurchaseAndOptionallyEquip(_metaState, item.Id, false))
                    {
                        PushEvent($"Purchased {item.Name}");
                        return;
                    }
                }

                PushEvent("No affordable cosmetic available.");
                return;
            }

            var nextOwnedCosmeticId = GetNextOwnedCosmeticId();
            if (nextOwnedCosmeticId.Length == 0)
            {
                PushEvent("No owned cosmetic to equip.");
                return;
            }

            DotArenaMetaProgression.Equip(_metaState, nextOwnedCosmeticId);
            PushEvent($"Equipped {nextOwnedCosmeticId}");
        }

        private void HandleSettingsLobbyAction(bool isPrimaryAction)
        {
            if (_metaState == null)
            {
                return;
            }

            if (isPrimaryAction)
            {
                var nextLanguage = string.Equals(_metaState.Settings.Language, "zh-CN", StringComparison.Ordinal)
                    ? "en-US"
                    : "zh-CN";
                if (DotArenaMetaProgression.SetLanguage(_metaState, nextLanguage))
                {
                    PushEvent($"Language set to {nextLanguage}");
                }

                return;
            }

            var fullscreen = DotArenaMetaProgression.ToggleFullscreen(_metaState);
            PushEvent(fullscreen ? "Fullscreen enabled" : "Fullscreen disabled");
        }

        private static DotArenaTaskProgress? FindReadyTask(List<DotArenaTaskProgress> tasks)
        {
            foreach (var task in tasks)
            {
                if (!task.Claimed && task.Progress >= task.Target)
                {
                    return task;
                }
            }

            return null;
        }

        private string GetNextOwnedCosmeticId()
        {
            if (_metaState == null || _metaState.OwnedCosmeticIds.Count == 0)
            {
                return string.Empty;
            }

            var ownedCosmetics = _metaState.OwnedCosmeticIds;
            var currentIndex = ownedCosmetics.IndexOf(_metaState.EquippedCosmeticId);
            if (currentIndex < 0)
            {
                return ownedCosmetics[0];
            }

            return ownedCosmetics[(currentIndex + 1) % ownedCosmetics.Count];
        }

        private void EnsureMetaState(string playerId)
        {
            _metaState = DotArenaMetaProgression.LoadOrCreate(playerId);
        }
    }
}
