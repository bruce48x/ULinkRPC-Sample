#nullable enable

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        public void OnUiSinglePlayerSelected()
        {
            if (IsConnecting)
            {
                return;
            }

            _singlePlayerStartRequested = true;
        }

        public void OnUiMultiplayerSelected()
        {
            if (IsConnecting)
            {
                return;
            }

            _entryMenuState = EntryMenuState.MultiplayerAuth;
            _status = "Enter account credentials";
            _eventMessage = "点击匹配开始联机";
            RefreshSceneUi();
        }

        public void OnUiBackToModeSelect()
        {
            if (IsConnecting)
            {
                return;
            }

            _entryMenuState = EntryMenuState.ModeSelect;
            _status = "请选择模式";
            _eventMessage = "请选择单机或联机";
            RefreshSceneUi();
        }

        public void OnUiCancelMatchmakingRequested()
        {
            if (_flowState != FrontendFlowState.Matchmaking)
            {
                return;
            }

            _ = CancelMatchmakingAsync();
        }

        public void OnUiConnectRequested()
        {
            if (IsConnecting)
            {
                return;
            }

            _ = ConnectAsync(enterMultiplayerLobbyAfterLogin: true);
        }

        public void OnUiRematchRequested()
        {
            if (_flowState != FrontendFlowState.Settlement || IsConnecting)
            {
                return;
            }

            _rematchRequested = true;
        }

        public void OnUiReturnToLobbyRequested()
        {
            if (_flowState != FrontendFlowState.Settlement || IsConnecting)
            {
                return;
            }

            _returnToLobbyRequested = true;
        }

        public void OnUiAccountChanged(string value)
        {
            _account = value;
        }

        public void OnUiPasswordChanged(string value)
        {
            _password = value;
        }

        private void OnUiLobbyActionRequested(MetaTab tab, bool isPrimaryAction)
        {
            if (_metaState == null || _flowState == FrontendFlowState.Matchmaking)
            {
                return;
            }

            switch (tab)
            {
                case MetaTab.Lobby:
                    HandleLobbyPresetAction(isPrimaryAction);
                    break;
                case MetaTab.Tasks:
                    HandleTaskLobbyAction(isPrimaryAction);
                    break;
                case MetaTab.Shop:
                    HandleShopLobbyAction(isPrimaryAction);
                    break;
                case MetaTab.Settings:
                    HandleSettingsLobbyAction(isPrimaryAction);
                    break;
            }
        }
    }
}
