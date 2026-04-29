#nullable enable

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        public void OnUiSinglePlayerSelected()
        {
            if (IsUiBusy)
            {
                return;
            }

            _singlePlayerStartRequested = true;
        }

        public void OnUiMultiplayerSelected()
        {
            if (IsUiBusy)
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
            if (IsUiBusy)
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
            if (_flowState != FrontendFlowState.Matchmaking || HasPendingUiRequest)
            {
                return;
            }

            _pendingUiRequest = PendingUiRequest.CancelMatchmaking;
            _status = "正在取消匹配";
            _eventMessage = "正在返回联机大厅";
            RefreshSceneUi();
            _ = CancelMatchmakingAsync();
        }

        public void OnUiConnectRequested()
        {
            if (IsUiBusy)
            {
                return;
            }

            _pendingUiRequest = PendingUiRequest.Login;
            _flowState = FrontendFlowState.Entry;
            _entryMenuState = EntryMenuState.MultiplayerAuth;
            _status = $"正在连接 {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}";
            _eventMessage = "正在登录联机账号";
            RefreshSceneUi();
            _ = ConnectAsync();
        }

        public void OnUiRematchRequested()
        {
            if (_flowState != FrontendFlowState.Settlement || IsUiBusy)
            {
                return;
            }

            _rematchRequested = true;
        }

        public void OnUiReturnToLobbyRequested()
        {
            if (_flowState != FrontendFlowState.Settlement || IsUiBusy)
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
            if (_metaState == null || _flowState == FrontendFlowState.Matchmaking || HasPendingUiRequest)
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
