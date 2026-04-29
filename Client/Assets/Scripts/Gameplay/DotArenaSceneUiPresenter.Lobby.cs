#nullable enable

using TMPro;
using UnityEngine.UI;

namespace SampleClient.Gameplay
{
    internal sealed partial class DotArenaSceneUiPresenter
    {
        private static string GetLobbyTabTitle(in DotArenaSceneUiSnapshot snapshot, MetaTab tab)
        {
            return tab switch
            {
                MetaTab.Lobby when snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby && snapshot.SessionMode == SessionMode.Multiplayer => "Multiplayer Lobby",
                MetaTab.Lobby => "Profile",
                MetaTab.Tasks => "Tasks",
                MetaTab.Shop => "Shop",
                MetaTab.Records => snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby ? "Match History" : "Records",
                MetaTab.Leaderboard => snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby ? "Lobby Board" : "Leaderboard",
                MetaTab.Settings => "Settings",
                _ => "Lobby"
            };
        }

        private static string GetLobbyTabDetail(in DotArenaSceneUiSnapshot snapshot, MetaTab tab)
        {
            return tab switch
            {
                MetaTab.Lobby => snapshot.MetaProfileDetail,
                MetaTab.Tasks => snapshot.MetaTasksDetail,
                MetaTab.Shop => snapshot.MetaShopDetail,
                MetaTab.Records => snapshot.MetaRecordsDetail,
                MetaTab.Leaderboard => snapshot.MetaLeaderboardDetail,
                MetaTab.Settings => snapshot.MetaSettingsDetail,
                _ => snapshot.MetaProfileDetail
            };
        }

        private static string GetLobbyQuickActionsText(in DotArenaSceneUiSnapshot snapshot, MetaTab tab)
        {
            if (snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby && snapshot.SessionMode == SessionMode.Multiplayer)
            {
                return string.Empty;
            }

            return tab == MetaTab.Lobby ? "Quick Access" : "Sections";
        }

        private void BindQuickActionButton(Button? button, int index)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                var targetTab = GetLobbyQuickActionTarget(_selectedLobbyTab, index);
                if (targetTab.HasValue)
                {
                    _selectedLobbyTab = targetTab.Value;
                }
            });
        }

        private void RefreshLobbyQuickActionButtons(in DotArenaSceneUiSnapshot snapshot, MetaTab tab)
        {
            if (snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby && snapshot.SessionMode == SessionMode.Multiplayer)
            {
                HideLobbyQuickActionButton(_lobbyQuickActionButton1);
                HideLobbyQuickActionButton(_lobbyQuickActionButton2);
                HideLobbyQuickActionButton(_lobbyQuickActionButton3);
                HideLobbyQuickActionButton(_lobbyQuickActionButton4);
                return;
            }

            RefreshLobbyQuickActionButton(_lobbyQuickActionButton1, _lobbyQuickActionButton1Text, snapshot, tab, 0);
            RefreshLobbyQuickActionButton(_lobbyQuickActionButton2, _lobbyQuickActionButton2Text, snapshot, tab, 1);
            RefreshLobbyQuickActionButton(_lobbyQuickActionButton3, _lobbyQuickActionButton3Text, snapshot, tab, 2);
            RefreshLobbyQuickActionButton(_lobbyQuickActionButton4, _lobbyQuickActionButton4Text, snapshot, tab, 3);
        }

        private static void HideLobbyQuickActionButton(Button? button)
        {
            if (button != null)
            {
                button.gameObject.SetActive(false);
            }
        }

        private void RefreshLobbyQuickActionButton(Button? button, TMP_Text? label, in DotArenaSceneUiSnapshot snapshot, MetaTab tab, int index)
        {
            if (button == null)
            {
                return;
            }

            var text = GetLobbyQuickActionHint(snapshot, tab, index);
            var hasAction = !string.IsNullOrWhiteSpace(text);
            button.gameObject.SetActive(hasAction);
            if (hasAction)
            {
                SetText(label, text);
            }
        }

        private static bool HasLobbyPrimaryAction(in DotArenaSceneUiSnapshot snapshot, MetaTab tab)
        {
            return tab is MetaTab.Lobby or MetaTab.Tasks or MetaTab.Shop or MetaTab.Settings;
        }

        private static bool HasLobbySecondaryAction(in DotArenaSceneUiSnapshot snapshot, MetaTab tab)
        {
            return tab is MetaTab.Lobby or MetaTab.Tasks or MetaTab.Shop or MetaTab.Settings;
        }

        private static string GetLobbyPrimaryActionLabel(in DotArenaSceneUiSnapshot snapshot, MetaTab tab)
        {
            return tab switch
            {
                MetaTab.Lobby when snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby && snapshot.SessionMode == SessionMode.Multiplayer => "开始匹配",
                MetaTab.Lobby => "Cycle Preset",
                MetaTab.Tasks => "Claim Ready",
                MetaTab.Shop => "Buy Cheapest",
                MetaTab.Settings => "Toggle Lang",
                _ => string.Empty
            };
        }

        private static string GetLobbySecondaryActionLabel(in DotArenaSceneUiSnapshot snapshot, MetaTab tab)
        {
            return tab switch
            {
                MetaTab.Lobby when snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby && snapshot.SessionMode == SessionMode.Multiplayer => "退出登录",
                MetaTab.Lobby => "Preview",
                MetaTab.Tasks => "Claim Next",
                MetaTab.Shop => "Equip Next",
                MetaTab.Settings => "Fullscreen",
                _ => string.Empty
            };
        }

        private static string GetLobbyQuickActionHint(in DotArenaSceneUiSnapshot snapshot, MetaTab tab, int index)
        {
            return (snapshot.EntryMenuState, snapshot.SessionMode, tab, index) switch
            {
                (EntryMenuState.MultiplayerLobby, SessionMode.Multiplayer, MetaTab.Lobby, 0) => "Match History",
                (EntryMenuState.MultiplayerLobby, SessionMode.Multiplayer, MetaTab.Lobby, 1) => "Lobby Board",
                (EntryMenuState.MultiplayerLobby, SessionMode.Multiplayer, MetaTab.Lobby, 2) => "Tasks",
                (EntryMenuState.MultiplayerLobby, SessionMode.Multiplayer, MetaTab.Lobby, 3) => "Shop",
                (EntryMenuState.MultiplayerLobby, SessionMode.Multiplayer, _, 0) => "Profile",
                (EntryMenuState.MultiplayerLobby, SessionMode.Multiplayer, _, 1) => "Shop",
                (EntryMenuState.MultiplayerLobby, SessionMode.Multiplayer, _, 2) => "Records",
                (EntryMenuState.MultiplayerLobby, SessionMode.Multiplayer, _, 3) => "Board",
                (_, SessionMode.SinglePlayer, MetaTab.Lobby, 0) => "Tasks",
                (_, SessionMode.SinglePlayer, MetaTab.Lobby, 1) => "Shop",
                (_, SessionMode.SinglePlayer, MetaTab.Lobby, 2) => "Board",
                (_, SessionMode.SinglePlayer, MetaTab.Lobby, 3) => "Settings",
                (_, SessionMode.SinglePlayer, MetaTab.Tasks, 0) => "Shop",
                (_, SessionMode.SinglePlayer, MetaTab.Tasks, 1) => "Settings",
                (_, SessionMode.SinglePlayer, MetaTab.Shop, 0) => "Tasks",
                (_, SessionMode.SinglePlayer, MetaTab.Shop, 1) => "Board",
                (_, SessionMode.SinglePlayer, MetaTab.Settings, 0) => "Tasks",
                (_, SessionMode.SinglePlayer, MetaTab.Settings, 1) => "Shop",
                (_, SessionMode.SinglePlayer, _, 0) => "Tasks",
                (_, SessionMode.SinglePlayer, _, 1) => "Shop",
                (_, _, MetaTab.Tasks, 0) => "Shop",
                (_, _, MetaTab.Tasks, 1) => "Profile",
                (_, _, MetaTab.Shop, 0) => "Tasks",
                (_, _, MetaTab.Shop, 1) => "Settings",
                (_, _, MetaTab.Records, 0) => "Profile",
                (_, _, MetaTab.Records, 1) => "Shop",
                (_, _, MetaTab.Leaderboard, 0) => "Profile",
                (_, _, MetaTab.Leaderboard, 1) => "Records",
                (_, _, MetaTab.Settings, 0) => "Tasks",
                (_, _, MetaTab.Settings, 1) => "Shop",
                (_, _, _, 0) => "Tasks",
                (_, _, _, 1) => "Shop",
                _ => string.Empty
            };
        }

        private MetaTab? GetLobbyQuickActionTarget(MetaTab currentTab, int index)
        {
            return (currentTab, index) switch
            {
                (MetaTab.Lobby, 0) => MetaTab.Records,
                (MetaTab.Lobby, 1) => MetaTab.Leaderboard,
                (MetaTab.Lobby, 2) => MetaTab.Tasks,
                (MetaTab.Lobby, 3) => MetaTab.Shop,
                (MetaTab.Tasks, 0) => MetaTab.Shop,
                (MetaTab.Tasks, 1) => MetaTab.Settings,
                (MetaTab.Tasks, 2) => MetaTab.Records,
                (MetaTab.Tasks, 3) => MetaTab.Leaderboard,
                (MetaTab.Shop, 0) => MetaTab.Tasks,
                (MetaTab.Shop, 1) => MetaTab.Leaderboard,
                (MetaTab.Shop, 2) => MetaTab.Records,
                (MetaTab.Shop, 3) => MetaTab.Settings,
                (MetaTab.Records, 0) => MetaTab.Lobby,
                (MetaTab.Records, 1) => MetaTab.Shop,
                (MetaTab.Records, 2) => MetaTab.Tasks,
                (MetaTab.Records, 3) => MetaTab.Settings,
                (MetaTab.Leaderboard, 0) => MetaTab.Lobby,
                (MetaTab.Leaderboard, 1) => MetaTab.Records,
                (MetaTab.Leaderboard, 2) => MetaTab.Tasks,
                (MetaTab.Leaderboard, 3) => MetaTab.Shop,
                (MetaTab.Settings, 0) => MetaTab.Tasks,
                (MetaTab.Settings, 1) => MetaTab.Shop,
                (MetaTab.Settings, 2) => MetaTab.Records,
                (MetaTab.Settings, 3) => MetaTab.Leaderboard,
                _ => null
            };
        }
    }
}
