#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Shared.Gameplay;
using Shared.Interfaces;
using TMPro;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;
using Object = UnityEngine.Object;

namespace SampleClient.Gameplay
{
    public static class DotArenaBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureGame()
        {
            if (Object.FindObjectOfType<DotArenaGame>() != null) return;

            var bootstrap = new GameObject(nameof(DotArenaGame));
            bootstrap.AddComponent<DotArenaGame>();
        }
    }

    public sealed class DotArenaGame : MonoBehaviour, IPlayerCallback
    {
        private static readonly SinglePlayerMatchPreset[] SinglePlayerPlaylist =
        {
            new(ArenaMapVariant.ClassicSquare, ArenaRuleVariant.ClassicElimination),
            new(ArenaMapVariant.NarrowBridge, ArenaRuleVariant.ScoreRush),
            new(ArenaMapVariant.FinalRing, ArenaRuleVariant.ArenaCollapse)
        };

        [SerializeField] private string _host = "127.0.0.1";
        [SerializeField] private int _port = 20000;
        [SerializeField] private string _path = "/ws";
        [SerializeField] private string _account = "a";
        [SerializeField] private string _password = "b";

        private readonly CancellationTokenSource _cts = new();
        private readonly DotArenaCallbackInbox _callbackInbox = new();
        private readonly DotArenaSceneUiPresenter _sceneUiPresenter = new();
        private readonly Dictionary<string, DotView> _views = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PlayerRenderState> _renderStates = new(StringComparer.Ordinal);
        private readonly List<PickupView> _pickupViews = new();
        private readonly Dictionary<string, PlayerOverlayView> _playerOverlayViews = new(StringComparer.Ordinal);

        private DotArenaNetworkSession? _networkSession;
        private DotArenaWorldSynchronizer? _worldSynchronizer;
        private ArenaSimulation? _localMatch;
        private string _localPlayerId = string.Empty;
        private bool _singlePlayerStartRequested;
        private bool _rematchRequested;
        private bool _returnToLobbyRequested;
        private EntryMenuState _entryMenuState = EntryMenuState.ModeSelect;
        private SessionMode _sessionMode = SessionMode.None;
        private FrontendFlowState _flowState = FrontendFlowState.Entry;
        private int _inputTick;
        private bool _dashQueued;
        private float _nextInputAt;
        private float _singlePlayerTickAccumulator;

        private int _localWinCount;
        private bool _hasAuthenticatedProfile;
        private string _authenticatedPlayerId = string.Empty;

        private Sprite _pixelSprite = null!;
        private Sprite _playerSprite = null!;
        private Sprite _playerOutlineSprite = null!;
        private Shader? _jellyShader;
        private Shader? _pickupAbsorbShader;
        private string _status = "\u8fde\u63a5\u4e2d...";
        private string _eventMessage = "\u7b49\u5f85\u73a9\u5bb6\u52a0\u5165";
        private float _eventMessageUntil;
        private int _lastWorldTick = -1;
        private int _lastLoggedPlayerCount = -1;
        private bool _shutdownStarted;
        private bool _ignoreDisconnectCallback;
        private string _lastLoggedInputVector = string.Empty;
        private bool _showDebugPanel;
        private int _lastRoundRemainingSeconds;
        private MatchSettlementSummary? _settlementSummary;
        private DotArenaMetaState? _metaState;
        private DotArenaRewardSummary? _lastRewardSummary;
        private MetaTab _selectedMetaTab;
        private SpriteRenderer? _safeZoneRenderer;
        private SpriteRenderer? _topBorderRenderer;
        private SpriteRenderer? _bottomBorderRenderer;
        private SpriteRenderer? _leftBorderRenderer;
        private SpriteRenderer? _rightBorderRenderer;
        private Vector2 _currentArenaHalfExtents = GameplayConfig.ArenaHalfExtents;
        private int _singlePlayerPlaylistIndex = -1;
        private ArenaMapVariant _currentArenaMapVariant = ArenaMapVariant.ClassicSquare;
        private ArenaRuleVariant _currentArenaRuleVariant = ArenaRuleVariant.ClassicElimination;
#if UNITY_EDITOR
        private Vector2 _editorMoveOverride;
        private bool _editorDashOverride;
        private bool _hasEditorInputOverride;
#endif

        private bool HasActiveSession => _flowState is FrontendFlowState.Matchmaking or FrontendFlowState.InMatch;

        private DotArenaNetworkSession NetworkSession => _networkSession ??= new DotArenaNetworkSession(OnDisconnected);

        private bool IsConnected => NetworkSession.IsConnected;

        private bool IsConnecting => NetworkSession.IsConnecting;

        private DotArenaWorldSynchronizer WorldSynchronizer => _worldSynchronizer ??= new DotArenaWorldSynchronizer(
            _views,
            _renderStates,
            _pickupViews,
            _playerOverlayViews,
            CreateView,
            EnsurePlayerOverlay,
            CreatePickupView,
            Destroy,
            UpdateArenaVisuals,
            message => PushEvent(message),
            (message, duration) => PushEvent(message, duration),
            message => _eventMessage = message,
            () => _metaState?.EquippedCosmeticId);

        private void Start()
        {
            ApplyLaunchOverrides();
            ConfigureWindow();
            InitializeConnectionMode();
            EnsureMetaState("Guest");
            ConfigureCamera();
            BuildArena();
            BindSceneUi();
            RefreshSceneUi();
        }

        private void Update()
        {
            CaptureInputIntent();
            ProcessMenuRequests();
            HandleInput();
            TickLocalMatch();
            ApplyPendingCallbacks();
            UpdateViews();
            RefreshSceneUi();
        }

        private void OnDestroy()
        {
            BeginShutdown();
            _cts.Dispose();
        }

        private void OnApplicationQuit()
        {
            BeginShutdown();
        }

        private void OnGUI()
        {
            if (_sceneUiPresenter.HasSceneUi)
            {
                return;
            }

            if (!HasActiveSession)
            {
                if (_flowState == FrontendFlowState.Settlement)
                {
                    DrawSettlementMenu();
                    DrawMetaDashboard();
                    return;
                }

                DrawEntryMenu();
                DrawMetaDashboard();
                return;
            }

            var hudSnapshot = new DotArenaImmediateHudSnapshot
            {
                Status = _status,
                LocalPlayerId = _localPlayerId,
                Account = _account,
                LocalPlayerScoreText = GetLocalPlayerScoreText(),
                LocalWinCount = _localWinCount,
                LastWorldTick = _lastWorldTick,
                LocalPlayerBuffText = GetLocalPlayerBuffText(),
                SessionMode = _sessionMode,
                Host = _host,
                Port = _port,
                Path = _path,
                EventMessage = GetCurrentEventMessage(),
                PlayerVisualDiameter = PlayerVisualDiameter
            };

            DotArenaImmediateHudRenderer.DrawSessionHud(hudSnapshot, _views, _renderStates);
        }

        private void DrawEntryMenu()
        {
            const float width = 360f;
            const float height = 240f;
            var boxRect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            var contentRect = new Rect(boxRect.x + 20f, boxRect.y + 20f, width - 40f, height - 40f);

            var previousColor = GUI.color;
            GUI.color = new Color(0.04f, 0.06f, 0.08f, 0.94f);
            GUI.Box(boxRect, GUIContent.none);
            GUI.color = previousColor;

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                wordWrap = true,
                normal = { textColor = new Color(0.86f, 0.91f, 0.96f, 1f) }
            };

            GUI.Label(new Rect(contentRect.x, contentRect.y, contentRect.width, 30f), "Dot Arena", titleStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 34f, contentRect.width, 36f), _status, bodyStyle);

            switch (_entryMenuState)
            {
                case EntryMenuState.ModeSelect:
                    DrawModeSelect(contentRect, bodyStyle);
                    break;
                case EntryMenuState.MultiplayerAuth:
                    DrawMultiplayerAuthDialog(contentRect, bodyStyle);
                    break;
                case EntryMenuState.MultiplayerLobby:
                    GUI.Label(new Rect(contentRect.x, contentRect.y + 84f, contentRect.width, 54f),
                        $"联机大厅已就绪。\n{GetMenuLoginStatusText()}", bodyStyle);
                    break;
            }
        }

        private void DrawSettlementMenu()
        {
            var summary = _settlementSummary;
            if (summary == null)
            {
                DrawEntryMenu();
                return;
            }

            const float width = 420f;
            const float height = 372f;
            var boxRect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            var contentRect = new Rect(boxRect.x + 24f, boxRect.y + 24f, width - 48f, height - 48f);

            var previousColor = GUI.color;
            GUI.color = new Color(0.04f, 0.06f, 0.08f, 0.96f);
            GUI.Box(boxRect, GUIContent.none);
            GUI.color = previousColor;

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                fontSize = 13,
                wordWrap = true,
                normal = { textColor = new Color(0.86f, 0.91f, 0.96f, 1f) }
            };

            GUI.Label(new Rect(contentRect.x, contentRect.y, contentRect.width, 34f), summary.Title, titleStyle);
            var detailText = string.Join("\n\n", new[]
            {
                summary.Detail,
                summary.RewardSummary,
                summary.TaskSummary,
                summary.NextStepSummary
            });
            GUI.Label(new Rect(contentRect.x, contentRect.y + 42f, contentRect.width, 180f), detailText, bodyStyle);

            if (GUI.Button(new Rect(contentRect.x + 20f, contentRect.y + 238f, contentRect.width - 40f, 34f), GetRematchButtonLabel(summary.SessionMode)))
            {
                _rematchRequested = true;
            }

            if (GUI.Button(new Rect(contentRect.x + 20f, contentRect.y + 280f, contentRect.width - 40f, 34f), "Return to Lobby"))
            {
                _returnToLobbyRequested = true;
            }
        }

        private void DrawMetaDashboard()
        {
            var meta = _metaState;
            if (meta == null || _flowState == FrontendFlowState.Matchmaking)
            {
                return;
            }

            const float width = 420f;
            const float height = 500f;
            var boxRect = new Rect(Screen.width - width - 24f, 24f, width, height);
            GUI.Box(boxRect, GUIContent.none);

            var buttonWidth = (width - 36f) / 3f;
            var tabs = new[]
            {
                ("Lobby", MetaTab.Lobby),
                ("Tasks", MetaTab.Tasks),
                ("Shop", MetaTab.Shop),
                ("Records", MetaTab.Records),
                ("Board", MetaTab.Leaderboard),
                ("Settings", MetaTab.Settings)
            };

            for (var i = 0; i < tabs.Length; i++)
            {
                var row = i / 3;
                var col = i % 3;
                var rect = new Rect(boxRect.x + 12f + (col * buttonWidth), boxRect.y + 12f + (row * 32f), buttonWidth - 6f, 26f);
                if (GUI.Button(rect, tabs[i].Item1))
                {
                    _selectedMetaTab = tabs[i].Item2;
                }
            }

            GUI.Label(new Rect(boxRect.x + 16f, boxRect.y + 84f, width - 32f, 24f),
                $"Player: {meta.PlayerId}  Lv.{meta.Level}  XP: {meta.Experience}/{GetMetaNextLevelRequirement(meta.Level)}  Coins: {meta.SoftCurrency}");

            var contentY = boxRect.y + 112f;
            switch (_selectedMetaTab)
            {
                case MetaTab.Lobby:
                    DrawLobbyMeta(meta, boxRect, contentY);
                    break;
                case MetaTab.Tasks:
                    DrawTasksMeta(meta, boxRect, contentY);
                    break;
                case MetaTab.Shop:
                    DrawShopMeta(meta, boxRect, contentY);
                    break;
                case MetaTab.Records:
                    DrawRecordsMeta(meta, boxRect, contentY);
                    break;
                case MetaTab.Leaderboard:
                    DrawLeaderboardMeta(meta, boxRect, contentY);
                    break;
                case MetaTab.Settings:
                    DrawSettingsMeta(meta, boxRect, contentY);
                    break;
            }
        }

        private void DrawLobbyMeta(DotArenaMetaState meta, Rect boxRect, float contentY)
        {
            GUI.Label(new Rect(boxRect.x + 16f, contentY, boxRect.width - 32f, 20f),
                $"Wins: {meta.TotalWins}  Matches: {meta.TotalMatches}  Streak: {meta.CurrentLoginStreak}");
            GUI.Label(new Rect(boxRect.x + 16f, contentY + 28f, boxRect.width - 32f, 20f),
                $"Equipped Skin: {meta.EquippedCosmeticId}");
            if (_lastRewardSummary != null)
            {
                GUI.Label(new Rect(boxRect.x + 16f, contentY + 56f, boxRect.width - 32f, 54f),
                    $"Last Rewards: +{_lastRewardSummary.ExperienceGained} XP, +{_lastRewardSummary.CurrencyGained} Coins, Level {_lastRewardSummary.NewLevel}");
            }
        }

        private void DrawTasksMeta(DotArenaMetaState meta, Rect boxRect, float contentY)
        {
            var y = contentY;
            GUI.Label(new Rect(boxRect.x + 16f, y, boxRect.width - 32f, 20f), "Daily Tasks");
            y += 24f;
            foreach (var task in meta.DailyTasks)
            {
                DrawTaskRow(meta, task, boxRect.x + 16f, ref y, boxRect.width - 32f);
            }

            y += 12f;
            GUI.Label(new Rect(boxRect.x + 16f, y, boxRect.width - 32f, 20f), "New Player Tasks");
            y += 24f;
            foreach (var task in meta.NewPlayerTasks)
            {
                DrawTaskRow(meta, task, boxRect.x + 16f, ref y, boxRect.width - 32f);
            }
        }

        private void DrawTaskRow(DotArenaMetaState meta, DotArenaTaskProgress task, float x, ref float y, float width)
        {
            GUI.Label(new Rect(x, y, width - 110f, 20f), $"{task.Title} ({task.Progress}/{task.Target})");
            if (task.Progress >= task.Target && !task.Claimed)
            {
                if (GUI.Button(new Rect(x + width - 100f, y - 2f, 100f, 24f), "Claim"))
                {
                    DotArenaMetaProgression.TryClaimTaskById(meta, task.TaskId);
                }
            }
            else
            {
                GUI.Label(new Rect(x + width - 100f, y, 100f, 20f), task.Claimed ? "Claimed" : "In Progress");
            }

            y += 24f;
        }

        private void DrawShopMeta(DotArenaMetaState meta, Rect boxRect, float contentY)
        {
            var y = contentY;
            foreach (var item in DotArenaMetaProgression.GetShopCatalog())
            {
                GUI.Label(new Rect(boxRect.x + 16f, y, 180f, 20f), $"{item.Name} ({item.Price})");
                if (meta.OwnedCosmeticIds.Contains(item.Id))
                {
                    if (GUI.Button(new Rect(boxRect.x + 220f, y - 2f, 80f, 24f), "Equip"))
                    {
                        DotArenaMetaProgression.Equip(meta, item.Id);
                    }
                }
                else if (GUI.Button(new Rect(boxRect.x + 220f, y - 2f, 80f, 24f), "Buy"))
                {
                    DotArenaMetaProgression.TryPurchaseAndOptionallyEquip(meta, item.Id, false);
                }

                y += 28f;
            }
        }

        private void DrawRecordsMeta(DotArenaMetaState meta, Rect boxRect, float contentY)
        {
            var y = contentY;
            foreach (var record in meta.MatchHistory)
            {
                GUI.Label(new Rect(boxRect.x + 16f, y, boxRect.width - 32f, 20f),
                    $"{record.PlayedAtUtcIso[..Math.Min(10, record.PlayedAtUtcIso.Length)]}  {record.Mode}  {record.Result}  Score {record.Score}");
                y += 22f;
                if (y > boxRect.y + boxRect.height - 24f)
                {
                    break;
                }
            }
        }

        private void DrawLeaderboardMeta(DotArenaMetaState meta, Rect boxRect, float contentY)
        {
            GUI.Label(new Rect(boxRect.x + 16f, contentY, boxRect.width - 32f, 20f), "Local Leaderboard");
            GUI.Label(new Rect(boxRect.x + 16f, contentY + 28f, boxRect.width - 32f, 20f), $"1. {meta.PlayerId} - {meta.TotalWins} wins");
            GUI.Label(new Rect(boxRect.x + 16f, contentY + 52f, boxRect.width - 32f, 20f), $"2. AI League - {Math.Max(0, meta.TotalWins - 1)} wins");
            GUI.Label(new Rect(boxRect.x + 16f, contentY + 76f, boxRect.width - 32f, 20f), $"3. Challenger - {Math.Max(0, meta.TotalWins / 2)} wins");
        }

        private void DrawSettingsMeta(DotArenaMetaState meta, Rect boxRect, float contentY)
        {
            GUI.Label(new Rect(boxRect.x + 16f, contentY, 140f, 20f), $"Master Volume: {meta.Settings.MasterVolume:0.0}");
            if (GUI.Button(new Rect(boxRect.x + 180f, contentY - 2f, 36f, 24f), "-"))
            {
                DotArenaMetaProgression.AdjustMasterVolume(meta, -0.1f);
            }
            if (GUI.Button(new Rect(boxRect.x + 220f, contentY - 2f, 36f, 24f), "+"))
            {
                DotArenaMetaProgression.AdjustMasterVolume(meta, 0.1f);
            }

            GUI.Label(new Rect(boxRect.x + 16f, contentY + 32f, 160f, 20f), $"Language: {meta.Settings.Language}");
            if (GUI.Button(new Rect(boxRect.x + 180f, contentY + 30f, 80f, 24f), "Toggle"))
            {
                DotArenaMetaProgression.SetLanguage(meta, meta.Settings.Language == "zh-CN" ? "en-US" : "zh-CN");
            }
        }

        private void DrawModeSelect(Rect contentRect, GUIStyle bodyStyle)
        {
            GUI.Label(new Rect(contentRect.x, contentRect.y + 70f, contentRect.width, 36f),
                $"选择一种游玩方式。单机会直接进入并自动补足 AI 到 4 人。\n{GetMenuLoginStatusText()}", bodyStyle);

            var previousEnabled = GUI.enabled;
            GUI.enabled = !IsConnecting;
            if (GUI.Button(new Rect(contentRect.x + 30f, contentRect.y + 126f, contentRect.width - 60f, 34f), "单机"))
            {
                _singlePlayerStartRequested = true;
            }

            if (GUI.Button(new Rect(contentRect.x + 30f, contentRect.y + 170f, contentRect.width - 60f, 34f), "联机"))
            {
                _entryMenuState = EntryMenuState.MultiplayerAuth;
                _status = "请输入账号密码";
                _eventMessage = "点击匹配后发起联机";
            }

            GUI.enabled = previousEnabled;
        }

        private void DrawMultiplayerAuthDialog(Rect contentRect, GUIStyle bodyStyle)
        {
            const float labelWidth = 48f;
            const float fieldHeight = 24f;
            var fieldWidth = contentRect.width - labelWidth - 26f;
            var accountY = contentRect.y + 74f;
            var passwordY = contentRect.y + 110f;
            var buttonY = contentRect.y + 154f;

            GUI.Label(new Rect(contentRect.x, contentRect.y + 42f, contentRect.width, 24f), "联机匹配", bodyStyle);
            GUI.Label(new Rect(contentRect.x + 8f, accountY + 3f, labelWidth, 20f), "账号", bodyStyle);
            GUI.Label(new Rect(contentRect.x + 8f, passwordY + 3f, labelWidth, 20f), "密码", bodyStyle);

            var previousEnabled = GUI.enabled;
            GUI.enabled = !IsConnecting;
            _account = GUI.TextField(new Rect(contentRect.x + labelWidth + 14f, accountY, fieldWidth, fieldHeight), _account);
            _password = GUI.PasswordField(new Rect(contentRect.x + labelWidth + 14f, passwordY, fieldWidth, fieldHeight), _password, '*');

            if (GUI.Button(new Rect(contentRect.x + 8f, buttonY, 120f, 28f), IsConnecting ? "匹配中..." : "匹配"))
            {
                _ = ConnectAsync(enterMultiplayerLobbyAfterLogin: true);
            }

            if (GUI.Button(new Rect(contentRect.x + contentRect.width - 128f, buttonY, 120f, 28f), "返回"))
            {
                _entryMenuState = EntryMenuState.ModeSelect;
                _status = "选择模式";
                _eventMessage = "请选择单机或联机";
            }

            GUI.enabled = previousEnabled;
        }

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
            _eventMessage = "\u70b9\u51fb\u5339\u914d\u5f00\u59cb\u8054\u673a";
            RefreshSceneUi();
        }

        public void OnUiBackToModeSelect()
        {
            if (IsConnecting)
            {
                return;
            }

            _entryMenuState = EntryMenuState.ModeSelect;
            _status = "\u8bf7\u9009\u62e9\u6a21\u5f0f";
            _eventMessage = "\u8bf7\u9009\u62e9\u5355\u673a\u6216\u8054\u673a";
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

        private void BindSceneUi()
        {
            _sceneUiPresenter.Bind(
                transform,
                OnUiSinglePlayerSelected,
                OnUiMultiplayerSelected,
                OnUiConnectRequested,
                OnUiBackToModeSelect,
                OnUiCancelMatchmakingRequested,
                OnUiAccountChanged,
                OnUiPasswordChanged,
                OnUiLobbyActionRequested,
                OnUiRematchRequested,
                OnUiReturnToLobbyRequested);
        }

        private void RefreshSceneUi()
        {
            var settlementSummary = _settlementSummary;
            _sceneUiPresenter.Refresh(new DotArenaSceneUiSnapshot
            {
                HasSession = HasActiveSession,
                FlowState = _flowState,
                EntryMenuState = _entryMenuState,
                SessionMode = _sessionMode,
                Status = _status,
                LocalPlayerId = _localPlayerId,
                Account = _account,
                Password = _password,
                LocalPlayerScoreText = GetLocalPlayerScoreText(),
                LocalWinCount = _localWinCount,
                LastWorldTick = _lastWorldTick,
                ViewCount = _views.Count,
                LocalPlayerBuffText = GetLocalPlayerBuffText(),
                DebugPanelVisible = _showDebugPanel,
                DebugPanelDetail = BuildDebugPanelDetail(),
                Host = _host,
                Port = _port,
                Path = _path,
                CurrentEventMessage = GetCurrentEventMessage(),
                LastRoundRemainingSeconds = _lastRoundRemainingSeconds,
                MenuLoginStatusText = GetMenuLoginStatusText(),
                IsConnecting = IsConnecting,
                SettlementTitle = settlementSummary?.Title ?? string.Empty,
                SettlementDetail = settlementSummary?.Detail ?? string.Empty,
                SettlementRewardSummary = settlementSummary?.RewardSummary ?? string.Empty,
                SettlementTaskSummary = settlementSummary?.TaskSummary ?? string.Empty,
                SettlementNextStepSummary = settlementSummary?.NextStepSummary ?? string.Empty,
                SettlementPrimaryActionText = settlementSummary == null
                    ? string.Empty
                    : GetRematchButtonLabel(settlementSummary.SessionMode),
                MatchmakingTitle = _sessionMode == SessionMode.SinglePlayer ? "Preparing Local Match" : "Matchmaking",
                MatchmakingDetail = BuildMatchmakingDetail(),
                MetaPlayerSummary = BuildMetaPlayerSummary(),
                MetaLobbyHighlights = BuildMetaLobbyHighlights(),
                MetaProfileDetail = BuildMetaProfileDetail(),
                MetaTasksDetail = BuildMetaTasksDetail(),
                MetaShopDetail = BuildMetaShopDetail(),
                MetaRecordsDetail = BuildMetaRecordsDetail(),
                MetaLeaderboardDetail = BuildMetaLeaderboardDetail(),
                MetaSettingsDetail = BuildMetaSettingsDetail(),
                MetaFooterHint = BuildMetaFooterHint()
            });
        }

        public void OnWorldState(WorldState worldState)
        {
            _callbackInbox.EnqueueWorldState(worldState);
        }

        public void OnPlayerDead(PlayerDead deadEvent)
        {
            _callbackInbox.EnqueuePlayerDead(deadEvent);
        }

        public void OnMatchEnd(MatchEnd matchEnd)
        {
            _callbackInbox.EnqueueMatchEnd(matchEnd);
        }

        private async Task ConnectAsync(bool enterMultiplayerLobbyAfterLogin)
        {
            if (IsConnecting || IsConnected || _sessionMode == SessionMode.SinglePlayer) return;

            _flowState = FrontendFlowState.Matchmaking;
            _entryMenuState = enterMultiplayerLobbyAfterLogin ? EntryMenuState.MultiplayerAuth : EntryMenuState.Hidden;
            _status = $"\u6b63\u5728\u8fde\u63a5 {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}";

            try
            {
                var reply = await NetworkSession.ConnectAndLoginAsync(_host, _port, _path, _account, _password, this, _cts.Token);

                if (reply.Code != 0)
                {
                    _status = $"Login failed, code={reply.Code}";
                    return;
                }

                _localPlayerId = string.IsNullOrWhiteSpace(reply.PlayerId) ? _account : reply.PlayerId;
                EnsureMetaState(_localPlayerId);
                _localWinCount = Math.Max(0, reply.WinCount);
                _hasAuthenticatedProfile = true;
                _authenticatedPlayerId = _localPlayerId;
                _sessionMode = SessionMode.Multiplayer;
                _flowState = enterMultiplayerLobbyAfterLogin ? FrontendFlowState.Entry : FrontendFlowState.Matchmaking;
                _entryMenuState = enterMultiplayerLobbyAfterLogin ? EntryMenuState.MultiplayerLobby : EntryMenuState.Hidden;
                _status = enterMultiplayerLobbyAfterLogin ? $"联机大厅: {_localPlayerId}" : $"Matchmaking: {_localPlayerId}";
                Debug.Log($"[DotArena] Connected as {_localPlayerId} -> {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}");
                PushEvent(enterMultiplayerLobbyAfterLogin ? "登录成功，可在联机大厅开始匹配" : "等待其他玩家加入");
            }
            catch (OperationCanceledException)
            {
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = enterMultiplayerLobbyAfterLogin ? EntryMenuState.MultiplayerAuth : EntryMenuState.MultiplayerLobby;
                _status = "Connection canceled";
            }
            catch (Exception ex)
            {
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = enterMultiplayerLobbyAfterLogin ? EntryMenuState.MultiplayerAuth : EntryMenuState.MultiplayerLobby;
                _status = $"Connect failed: {ex.Message}";
                Debug.LogError($"[DotArena] Connect failed: {ex}");
                await DisposeConnectionAsync();
                if (!enterMultiplayerLobbyAfterLogin && _hasAuthenticatedProfile && !string.IsNullOrWhiteSpace(_authenticatedPlayerId))
                {
                    _sessionMode = SessionMode.Multiplayer;
                    _localPlayerId = _authenticatedPlayerId;
                    _eventMessage = "联机匹配失败，已返回联机大厅";
                }
            }
        }

        private void CaptureInputIntent()
        {
            if (DotArenaInputUtility.IsKeyDown(KeyCode.P))
            {
                _showDebugPanel = !_showDebugPanel;
            }
        }

        private void ApplyPendingCallbacks()
        {
            var pending = _callbackInbox.Drain();

            if (pending.WorldState != null)
            {
                ApplyWorldState(pending.WorldState);
            }

            foreach (var deadEvent in pending.Deaths)
            {
                HandleDeadEvent(deadEvent);
            }

            if (pending.MatchEnd != null)
            {
                HandleMatchEnd(pending.MatchEnd);
            }
        }

        private void ApplyWorldState(WorldState worldState)
        {
            WorldSynchronizer.ApplyWorldState(
                worldState,
                _localPlayerId,
                ref _lastWorldTick,
                ref _lastRoundRemainingSeconds,
                ref _lastLoggedPlayerCount,
                ref _currentArenaHalfExtents);

            if (_sessionMode != SessionMode.None &&
                _flowState != FrontendFlowState.Settlement &&
                worldState.Players.Count > 0)
            {
                _flowState = FrontendFlowState.InMatch;
                _status = _sessionMode == SessionMode.SinglePlayer
                    ? $"Single-player Match: {_localPlayerId}"
                    : $"In Match: {_localPlayerId}";
            }
        }

        private void HandleDeadEvent(PlayerDead deadEvent)
        {
            if (_renderStates.TryGetValue(deadEvent.PlayerId, out var renderState))
            {
                renderState.Alive = false;
                renderState.State = PlayerLifeState.Dead;
            }

            if (_views.TryGetValue(deadEvent.PlayerId, out var view))
            {
                var radius = renderState?.Radius ?? GameplayConfig.PlayerVisualRadius;
                view.ApplyPresentation(DotArenaPresentation.ResolvePlayerColor(deadEvent.PlayerId), PlayerLifeState.Dead, false, radius);
            }

            PushEvent(deadEvent.PlayerId == _localPlayerId
                ? "你被吞噬了"
                : $"{deadEvent.PlayerId} 被吞噬");
        }

        private void HandleMatchEnd(MatchEnd matchEnd)
        {
            if (_sessionMode == SessionMode.Multiplayer &&
                string.Equals(matchEnd.WinnerPlayerId, _localPlayerId, StringComparison.Ordinal))
            {
                _localWinCount += 1;
            }

            PushEvent(matchEnd.WinnerPlayerId == _localPlayerId
                ? "本局胜利"
                : $"胜者: {matchEnd.WinnerPlayerId}");

            _ = ReturnToMainMenuAfterMatchAsync(
                _sessionMode == SessionMode.Multiplayer,
                matchEnd.WinnerPlayerId,
                string.Equals(matchEnd.WinnerPlayerId, _localPlayerId, StringComparison.Ordinal));
        }

        private void TickLocalMatch()
        {
            if (_sessionMode != SessionMode.SinglePlayer || _localMatch == null)
            {
                return;
            }

            _singlePlayerTickAccumulator += Mathf.Min(Time.deltaTime, SinglePlayerTickSeconds * MaxSinglePlayerCatchUpTicks);

            var catchUpTicks = 0;
            while (_singlePlayerTickAccumulator >= SinglePlayerTickSeconds && catchUpTicks < MaxSinglePlayerCatchUpTicks)
            {
                _singlePlayerTickAccumulator -= SinglePlayerTickSeconds;
                catchUpTicks++;

                var step = _localMatch.Tick(SinglePlayerTickSeconds);
                ApplyWorldState(step.WorldState);

                foreach (var deadEvent in step.Deaths)
                {
                    HandleDeadEvent(deadEvent);
                }

                if (step.MatchEnd != null)
                {
                    HandleMatchEnd(step.MatchEnd);
                    _singlePlayerTickAccumulator = 0f;
                    break;
                }
            }

            if (catchUpTicks == MaxSinglePlayerCatchUpTicks && _singlePlayerTickAccumulator > SinglePlayerTickSeconds)
            {
                _singlePlayerTickAccumulator = 0f;
            }
        }

        private void UpdateViews()
        {
            foreach (var entry in _views)
            {
                if (!_renderStates.TryGetValue(entry.Key, out var renderState))
                {
                    continue;
                }

                var elapsed = Mathf.Clamp01((Time.time - renderState.ReceivedAt) / InterpolationDurationSeconds);
                var smoothed = elapsed * elapsed * (3f - (2f * elapsed));
                var position = Vector2.Lerp(renderState.PreviousPosition, renderState.TargetPosition, smoothed);

                entry.Value.SetPosition(position);
            }

            UpdateCameraFollow();
            UpdatePlayerOverlayViews();

            var pickupScale = GameplayConfig.PickupCollisionRadius * 2f;
            foreach (var pickupView in _pickupViews)
            {
                var pulse = 1f + (Mathf.Sin(Time.time * PickupPulseFrequency) * PickupPulseAmplitude);
                pickupView.UpdateVisual(Time.time, pickupScale * pulse, PickupAbsorbDurationSeconds);
            }

            foreach (var dotView in _views.Values)
            {
                dotView.UpdateJelly(Time.time);
            }
        }

        private void UpdateCameraFollow()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            var targetPosition = Vector2.zero;
            if (_views.TryGetValue(_localPlayerId, out var localView))
            {
                targetPosition = localView.GetPosition();
            }

            var halfVisibleHeight = Mathf.Max(0f, camera.orthographicSize - ArenaVisualPadding);
            var halfVisibleWidth = halfVisibleHeight * camera.aspect;
            var limitX = Mathf.Max(0f, CurrentArenaHalfWidth - halfVisibleWidth);
            var limitY = Mathf.Max(0f, CurrentArenaHalfHeight - halfVisibleHeight);
            var desired = new Vector3(
                Mathf.Clamp(targetPosition.x, -limitX, limitX),
                Mathf.Clamp(targetPosition.y, -limitY, limitY),
                -10f);
            var t = 1f - Mathf.Exp(-CameraFollowSharpness * Time.deltaTime);
            camera.transform.position = Vector3.Lerp(camera.transform.position, desired, t);
        }

        private void HandleInput()
        {
            if (!HasActiveSession || Time.time < _nextInputAt)
            {
                return;
            }

            _nextInputAt = Time.time + InputSendIntervalSeconds;

            var move = ReadMoveVector();
            var dash = _dashQueued;
            _dashQueued = false;
            var inputSummary = $"{move.x:0.00},{move.y:0.00}|dash={dash}";
            if (!string.Equals(_lastLoggedInputVector, inputSummary, StringComparison.Ordinal))
            {
                _lastLoggedInputVector = inputSummary;
                Debug.Log($"[DotArena] HandleInput mode={_sessionMode} move={inputSummary} localMatch={_localMatch != null}");
            }

            if (_sessionMode == SessionMode.SinglePlayer && _localMatch != null)
            {
                _localMatch.SubmitInput(new InputMessage
                {
                    PlayerId = _localPlayerId,
                    MoveX = move.x,
                    MoveY = move.y,
                    Dash = dash,
                    Tick = ++_inputTick
                });
                return;
            }

            if (!IsConnected)
            {
                return;
            }

            _ = SendInputAsync(move, dash);
        }

        private async Task SendInputAsync(Vector2 move, bool dash)
        {
            try
            {
                await NetworkSession.SubmitInputAsync(new InputMessage
                {
                    PlayerId = _localPlayerId,
                    MoveX = move.x,
                    MoveY = move.y,
                    Dash = dash,
                    Tick = ++_inputTick
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _status = $"Input failed: {ex.Message}";
            }
        }

        private void OnDisconnected(Exception? ex)
        {
            if (_ignoreDisconnectCallback)
            {
                return;
            }

            if (_sessionMode == SessionMode.SinglePlayer)
            {
                Debug.LogWarning($"[DotArena] Ignored remote disconnect while running single-player: {ex?.Message ?? "Disconnected"}");
                return;
            }

            ResetSessionPresentation();
            _localPlayerId = string.Empty;
            _sessionMode = SessionMode.None;
            _flowState = FrontendFlowState.Entry;
            _localMatch = null;
            _entryMenuState = EntryMenuState.ModeSelect;
            _hasAuthenticatedProfile = false;
            _authenticatedPlayerId = string.Empty;
            _settlementSummary = null;
            _localWinCount = 0;
            _status = ex == null ? "Disconnected" : $"Disconnected: {ex.Message}";
            Debug.LogWarning($"[DotArena] {_status}");
        }

        private Task ReturnToMainMenuAfterMatchAsync(bool preserveLoginState)
        {
            return ReturnToMainMenuAfterMatchAsync(
                preserveLoginState,
                _localPlayerId,
                true);
        }

        private async Task ReturnToMainMenuAfterMatchAsync(bool preserveLoginState, string winnerPlayerId, bool localPlayerWon)
        {
            var sessionMode = _sessionMode;
            var localScore = GetLocalPlayerScoreValue();
            var authenticatedPlayerId = _authenticatedPlayerId;
            var localWinCount = _localWinCount;

            if (_sessionMode == SessionMode.Multiplayer)
            {
                await DisposeConnectionAsync().ConfigureAwait(false);
            }
            else
            {
                ResetSessionPresentation();
                _sessionMode = SessionMode.None;
                _localMatch = null;
                _localPlayerId = string.Empty;
            }

            _flowState = FrontendFlowState.Settlement;
            _entryMenuState = EntryMenuState.Hidden;
            _status = "Match finished";
            _eventMessage = "Review results, then rematch or return to the lobby.";

            if (preserveLoginState)
            {
                _hasAuthenticatedProfile = true;
                _authenticatedPlayerId = authenticatedPlayerId;
                _localWinCount = localWinCount;
                _sessionMode = SessionMode.Multiplayer;
                _localPlayerId = authenticatedPlayerId;
            }
            else
            {
                _hasAuthenticatedProfile = false;
                _authenticatedPlayerId = string.Empty;
                _localWinCount = 0;
                _sessionMode = SessionMode.None;
                _localPlayerId = string.Empty;
            }

            _settlementSummary = new MatchSettlementSummary
            {
                Title = preserveLoginState ? "Multiplayer Results" : "Single-player Results",
                Detail = BuildSettlementDetail(sessionMode, localScore, _localWinCount, winnerPlayerId, localPlayerWon),
                RewardSummary = BuildSettlementRewardSummary(sessionMode),
                TaskSummary = BuildSettlementTaskSummary(sessionMode),
                NextStepSummary = BuildSettlementNextStepSummary(sessionMode),
                WinnerPlayerId = winnerPlayerId,
                LocalPlayerScore = localScore,
                LocalWinCount = _localWinCount,
                LocalPlayerWon = localPlayerWon,
                SessionMode = sessionMode
            };

            if (_metaState != null)
            {
                _lastRewardSummary = DotArenaMetaProgression.ApplyMatchResult(
                    _metaState,
                    sessionMode,
                    winnerPlayerId,
                    preserveLoginState ? authenticatedPlayerId : "Player",
                    localScore);
                if (_settlementSummary != null)
                {
                    _settlementSummary.RewardSummary = BuildSettlementRewardSummary(sessionMode);
                    _settlementSummary.TaskSummary = BuildSettlementTaskSummary(sessionMode);
                }
            }
            else
            {
                _lastRewardSummary = null;
            }
        }

        private void BeginShutdown()
        {
            if (_shutdownStarted)
            {
                return;
            }

            _shutdownStarted = true;
            _cts.Cancel();
            _ = DisposeConnectionAsync();
        }

        private async Task DisposeConnectionAsync(bool clearSessionState = true)
        {
            _ignoreDisconnectCallback = true;
            try
            {
                await NetworkSession.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _ignoreDisconnectCallback = false;
            }

            if (clearSessionState)
            {
                _sessionMode = SessionMode.None;
                _localPlayerId = string.Empty;
                _localMatch = null;
            }
        }

        private async Task CancelMatchmakingAsync()
        {
            if (_flowState != FrontendFlowState.Matchmaking)
            {
                return;
            }

            var preserveLoginState = _sessionMode == SessionMode.Multiplayer && _hasAuthenticatedProfile;
            var authenticatedPlayerId = _authenticatedPlayerId;
            var localWinCount = _localWinCount;

            if (_sessionMode == SessionMode.Multiplayer)
            {
                await DisposeConnectionAsync().ConfigureAwait(false);
            }
            else
            {
                ResetSessionPresentation();
                _sessionMode = SessionMode.None;
                _localMatch = null;
                _localPlayerId = string.Empty;
            }

            _flowState = FrontendFlowState.Entry;
            if (preserveLoginState)
            {
                _hasAuthenticatedProfile = true;
                _authenticatedPlayerId = authenticatedPlayerId;
                _localWinCount = localWinCount;
                _sessionMode = SessionMode.Multiplayer;
                _localPlayerId = authenticatedPlayerId;
                _entryMenuState = EntryMenuState.MultiplayerLobby;
                _status = $"联机大厅: {authenticatedPlayerId}";
                _eventMessage = "已返回联机大厅";
            }
            else
            {
                _hasAuthenticatedProfile = false;
                _authenticatedPlayerId = string.Empty;
                _localWinCount = 0;
                _sessionMode = SessionMode.None;
                _localPlayerId = string.Empty;
                _entryMenuState = EntryMenuState.ModeSelect;
                _status = "Choose a mode";
                _eventMessage = "Select single-player or multiplayer.";
            }
        }

        private void ProcessMenuRequests()
        {
            if (IsConnecting)
            {
                return;
            }

            if (_flowState == FrontendFlowState.Settlement)
            {
                if (_returnToLobbyRequested)
                {
                    _returnToLobbyRequested = false;
                    _rematchRequested = false;
                    ReturnToEntryMenuFromSettlement();
                }

                if (_rematchRequested)
                {
                    _rematchRequested = false;
                    var sessionMode = _settlementSummary?.SessionMode ?? SessionMode.SinglePlayer;
                    _settlementSummary = null;
                    _flowState = FrontendFlowState.Entry;

                    if (sessionMode == SessionMode.SinglePlayer)
                    {
                        BeginSinglePlayerMatch();
                    }
                    else
                    {
                        BeginMultiplayerMatchmaking();
                    }
                }

                return;
            }

            if (HasActiveSession)
            {
                return;
            }

            if (!_singlePlayerStartRequested)
            {
                return;
            }

            _singlePlayerStartRequested = false;
            BeginSinglePlayerMatch();
        }

        private void BeginMultiplayerMatchmaking()
        {
            if (!_hasAuthenticatedProfile || string.IsNullOrWhiteSpace(_authenticatedPlayerId))
            {
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerAuth;
                _status = "请先登录联机账号";
                _eventMessage = "输入账号密码后进入联机大厅";
                return;
            }

            _sessionMode = SessionMode.Multiplayer;
            _localPlayerId = _authenticatedPlayerId;
            _flowState = FrontendFlowState.Matchmaking;
            _entryMenuState = EntryMenuState.Hidden;
            _status = $"Matchmaking: {_localPlayerId}";
            _eventMessage = "等待其他玩家加入";
            _settlementSummary = null;

            if (IsConnected)
            {
                return;
            }

            _ = ConnectAsync(enterMultiplayerLobbyAfterLogin: false);
        }

        private void ConfigureCamera()
        {
            var cameraObject = GameObject.FindWithTag("MainCamera");
            var mainCamera = cameraObject != null ? cameraObject.GetComponent<Camera>() : null;

            if (mainCamera == null)
            {
                cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                mainCamera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            mainCamera.orthographic = true;
            mainCamera.orthographicSize = FollowCameraSize;
            mainCamera.backgroundColor = BackgroundColor;
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.transform.position = new Vector3(0f, 0f, -10f);
            mainCamera.transform.rotation = Quaternion.identity;
        }

        private void ConfigureWindow()
        {
#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
            Screen.SetResolution(WindowWidth, WindowHeight, FullScreenMode.Windowed);
#endif
        }

        private void InitializeConnectionMode()
        {
            _flowState = FrontendFlowState.Entry;
            _entryMenuState = EntryMenuState.ModeSelect;
            _status = "选择模式";
            _eventMessage = "请选择单机或联机";
        }

        private string GetMenuLoginStatusText()
        {
            if (!_hasAuthenticatedProfile || string.IsNullOrWhiteSpace(_authenticatedPlayerId))
            {
                return "未登录";
            }

            return $"已登录: {_authenticatedPlayerId}   胜场: {_localWinCount}";
        }

        private void ApplyLaunchOverrides()
        {
            var launchArguments = Rpc.RpcLaunchArguments.ReadCurrentProcess();
            launchArguments.ApplyTo(ref _host, ref _port, ref _path);
            launchArguments.ApplyCredentials(ref _account, ref _password);

            if (launchArguments.HasOverrides)
            {
                Debug.Log($"[LaunchArgs] DotArenaGame host={_host}, port={_port}, path={_path}, account={_account}");
            }
        }

        private void BeginSinglePlayerMatch()
        {
            var preset = GetNextSinglePlayerPreset();
            _settlementSummary = null;
            ResetSessionPresentation();
            _ = DisposeConnectionAsync(clearSessionState: false);
            _sessionMode = SessionMode.SinglePlayer;
            _flowState = FrontendFlowState.Matchmaking;
            _localPlayerId = "Player";
            EnsureMetaState(_localPlayerId);
            _currentArenaMapVariant = preset.MapVariant;
            _currentArenaRuleVariant = preset.RuleVariant;
            _localMatch = new ArenaSimulation(CreateSinglePlayerOptions(preset));
            _localMatch.UpsertPlayer(new ArenaPlayerRegistration
            {
                PlayerId = _localPlayerId,
                Score = 1
            });
            _localWinCount = 0;
            _entryMenuState = EntryMenuState.Hidden;
            _status = $"Single-player | {GetArenaRuleVariantName(_currentArenaRuleVariant)}";
            _eventMessage = $"Loading {GetArenaMapVariantName(_currentArenaMapVariant)}";
            _lastWorldTick = -1;
            _inputTick = 0;
            _singlePlayerTickAccumulator = 0f;
            Debug.Log("[DotArena] BeginSinglePlayerMatch");
            ApplyWorldState(_localMatch.CreateWorldState());
            PushEvent($"Preset: {GetSinglePlayerPresetLabel(_currentArenaMapVariant, _currentArenaRuleVariant)}", 4f);
            _status = $"Single-player: {_localPlayerId}";
        }

        private void BuildArena()
        {
            _pixelSprite = DotArenaSpriteFactory.CreatePixelSprite();
            _playerSprite = DotArenaSpriteFactory.CreateCircleSprite();
            _playerOutlineSprite = DotArenaSpriteFactory.CreateCircleOutlineSprite();
            _jellyShader = Shader.Find(JellyShaderName);

            var existingRoot = transform.Find("ArenaRoot");
            if (existingRoot != null)
            {
                Destroy(existingRoot.gameObject);
            }

            var arenaRoot = new GameObject("ArenaRoot");
            arenaRoot.transform.SetParent(transform, false);

            CreateRect(arenaRoot.transform, "DangerZone", Vector2.zero,
                new Vector2((ArenaHalfWidth + 1f) * 2f, (ArenaHalfHeight + 1f) * 2f), DangerColor, -30);

            CreateRect(arenaRoot.transform, "Board", Vector2.zero, new Vector2(ArenaHalfWidth * 2f, ArenaHalfHeight * 2f),
                BoardColor, -20);
            _safeZoneRenderer = CreateRect(arenaRoot.transform, "SafeZone", Vector2.zero,
                new Vector2(ArenaHalfWidth * 2f, ArenaHalfHeight * 2f), SafeZoneColor, -15);

            const float gridStep = 2f;
            for (var x = -ArenaHalfWidth; x <= ArenaHalfWidth + 0.01f; x += gridStep)
            {
                CreateRect(arenaRoot.transform, $"Vertical-{Mathf.RoundToInt(x)}", new Vector2(x, 0f),
                    new Vector2(0.05f, ArenaHalfHeight * 2f), GridColor, -10);
            }

            for (var y = -ArenaHalfHeight; y <= ArenaHalfHeight + 0.01f; y += gridStep)
            {
                CreateRect(arenaRoot.transform, $"Horizontal-{Mathf.RoundToInt(y)}", new Vector2(0f, y),
                    new Vector2(ArenaHalfWidth * 2f, 0.05f), GridColor, -10);
            }

            _topBorderRenderer = CreateRect(arenaRoot.transform, "TopBorder", new Vector2(0f, ArenaHalfHeight),
                new Vector2(ArenaHalfWidth * 2f + 0.18f, 0.18f), BorderColor, -5);
            _bottomBorderRenderer = CreateRect(arenaRoot.transform, "BottomBorder", new Vector2(0f, -ArenaHalfHeight),
                new Vector2(ArenaHalfWidth * 2f + 0.18f, 0.18f), BorderColor, -5);
            _leftBorderRenderer = CreateRect(arenaRoot.transform, "LeftBorder", new Vector2(-ArenaHalfWidth, 0f),
                new Vector2(0.18f, ArenaHalfHeight * 2f + 0.18f), BorderColor, -5);
            _rightBorderRenderer = CreateRect(arenaRoot.transform, "RightBorder", new Vector2(ArenaHalfWidth, 0f),
                new Vector2(0.18f, ArenaHalfHeight * 2f + 0.18f), BorderColor, -5);
            UpdateArenaVisuals();
        }

        private void ResetSessionPresentation()
        {
            Debug.Log($"[DotArena] ResetSessionPresentation views={_views.Count}, pickups={_pickupViews.Count}, mode={_sessionMode}");
            foreach (var view in _views.Values)
            {
                Destroy(view.Root);
            }

            foreach (var pickupView in _pickupViews)
            {
                Destroy(pickupView.Root);
            }

            foreach (var overlay in _playerOverlayViews.Values)
            {
                Destroy(overlay.Root);
            }

            _views.Clear();
            _pickupViews.Clear();
            _playerOverlayViews.Clear();
            _renderStates.Clear();
            _callbackInbox.Clear();
            _localWinCount = _sessionMode == SessionMode.Multiplayer ? _localWinCount : 0;
            _lastWorldTick = -1;
            _lastLoggedPlayerCount = -1;
            _dashQueued = false;
            _nextInputAt = 0f;
            _singlePlayerTickAccumulator = 0f;
            _currentArenaHalfExtents = GameplayConfig.ArenaHalfExtents;
            UpdateArenaVisuals();
        }

        private void ReturnToEntryMenuFromSettlement()
        {
            var preserveLoginState = _settlementSummary?.SessionMode == SessionMode.Multiplayer;
            var authenticatedPlayerId = _authenticatedPlayerId;
            var localWinCount = _localWinCount;

            _settlementSummary = null;
            _flowState = FrontendFlowState.Entry;

            if (preserveLoginState)
            {
                _hasAuthenticatedProfile = true;
                _authenticatedPlayerId = authenticatedPlayerId;
                _localWinCount = localWinCount;
                _sessionMode = SessionMode.Multiplayer;
                _localPlayerId = authenticatedPlayerId;
                _entryMenuState = EntryMenuState.MultiplayerLobby;
                _status = $"联机大厅: {authenticatedPlayerId}";
                _eventMessage = "已返回联机大厅";
                return;
            }

            _hasAuthenticatedProfile = false;
            _authenticatedPlayerId = string.Empty;
            _localWinCount = 0;
            _sessionMode = SessionMode.None;
            _localPlayerId = string.Empty;
            _entryMenuState = EntryMenuState.ModeSelect;
            _status = "Choose a mode";
            _eventMessage = "Select single-player or multiplayer.";
        }

        private Vector2 ReadMoveVector()
        {
#if UNITY_EDITOR
            if (_hasEditorInputOverride)
            {
                return _editorMoveOverride.sqrMagnitude > 1f ? _editorMoveOverride.normalized : _editorMoveOverride;
            }
#endif

            var x = 0f;
            var y = 0f;

            if (DotArenaInputUtility.IsKeyPressed(KeyCode.A)) x -= 1f;
            if (DotArenaInputUtility.IsKeyPressed(KeyCode.D)) x += 1f;
            if (DotArenaInputUtility.IsKeyPressed(KeyCode.S)) y -= 1f;
            if (DotArenaInputUtility.IsKeyPressed(KeyCode.W)) y += 1f;

            var move = new Vector2(x, y);
            return move.sqrMagnitude > 1f ? move.normalized : move;
        }

#if UNITY_EDITOR
        private bool ConsumeEditorDashOverride()
        {
            if (!_editorDashOverride)
            {
                return false;
            }

            _editorDashOverride = false;
            return true;
        }
#endif

        private DotView CreateView(string playerId)
        {
            var viewRoot = new GameObject(playerId);
            viewRoot.transform.SetParent(transform, false);
            Debug.Log($"[DotArena] CreateView root={viewRoot.name} parent={viewRoot.transform.parent?.name}");

            var renderer = viewRoot.AddComponent<SpriteRenderer>();
            renderer.sprite = _playerSprite;
            var cosmeticId = playerId == _localPlayerId ? _metaState?.EquippedCosmeticId : null;
            renderer.color = DotArenaPresentation.ResolvePlayerColor(playerId, cosmeticId);
            renderer.sortingOrder = PlayerSortingOrder;
            renderer.material = CreateJellyMaterial(UnityEngine.Random.Range(0f, Mathf.PI * 2f), 3.6f, 0.06f);

            var outlineObject = new GameObject("Outline");
            outlineObject.transform.SetParent(viewRoot.transform, false);
            outlineObject.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            var outlineRenderer = outlineObject.AddComponent<SpriteRenderer>();
            outlineRenderer.sprite = _playerOutlineSprite;
            outlineRenderer.color = PlayerOutlineColor;
            outlineRenderer.sortingOrder = PlayerSortingOrder - 1;
            outlineRenderer.material = CreateJellyMaterial(UnityEngine.Random.Range(0f, Mathf.PI * 2f), 4.2f, 0.08f);

            var nameBackdrop = new GameObject("NameBackdrop");
            nameBackdrop.transform.SetParent(viewRoot.transform, false);
            nameBackdrop.transform.localPosition = new Vector3(0f, PlayerNameOffsetY, PlayerTextDepth + 0.01f);
            nameBackdrop.transform.localScale = new Vector3(PlayerNameBackdropWidth, PlayerNameBackdropHeight, 1f);
            var nameBackdropRenderer = nameBackdrop.AddComponent<SpriteRenderer>();
            nameBackdropRenderer.sprite = _pixelSprite;
            nameBackdropRenderer.color = PlayerTextBackdropColor;
            nameBackdropRenderer.sortingOrder = PlayerTextBackdropSortingOrder;

            var nameLabel = new GameObject("NameLabel");
            nameLabel.transform.SetParent(viewRoot.transform, false);
            nameLabel.transform.localPosition = new Vector3(0f, PlayerNameOffsetY, PlayerTextDepth);
            nameLabel.transform.localScale = Vector3.one * PlayerNameScale;

            var nameText = nameLabel.AddComponent<TextMesh>();
            nameText.text = playerId;
            nameText.fontSize = 48;
            nameText.characterSize = PlayerTextCharacterSize;
            nameText.anchor = TextAnchor.MiddleCenter;
            nameText.alignment = TextAlignment.Center;
            nameText.fontStyle = FontStyle.Bold;
            nameText.color = new Color(0.98f, 0.99f, 1f, 1f);
            DotArenaSpriteFactory.ConfigureTextRenderer(nameText.GetComponent<MeshRenderer>(), PlayerTextSortingOrder);

            var scoreBackdrop = new GameObject("ScoreBackdrop");
            scoreBackdrop.transform.SetParent(viewRoot.transform, false);
            scoreBackdrop.transform.localPosition = new Vector3(0f, PlayerScoreOffsetY, PlayerTextDepth + 0.01f);
            scoreBackdrop.transform.localScale = new Vector3(PlayerScoreBackdropWidth, PlayerScoreBackdropHeight, 1f);
            var scoreBackdropRenderer = scoreBackdrop.AddComponent<SpriteRenderer>();
            scoreBackdropRenderer.sprite = _pixelSprite;
            scoreBackdropRenderer.color = PlayerTextBackdropColor;
            scoreBackdropRenderer.sortingOrder = PlayerTextBackdropSortingOrder;

            var scoreLabel = new GameObject("ScoreLabel");
            scoreLabel.transform.SetParent(viewRoot.transform, false);
            scoreLabel.transform.localPosition = new Vector3(0f, PlayerScoreOffsetY, PlayerTextDepth);
            scoreLabel.transform.localScale = Vector3.one * PlayerScoreScale;

            var scoreText = scoreLabel.AddComponent<TextMesh>();
            scoreText.text = "0";
            scoreText.fontSize = 44;
            scoreText.characterSize = PlayerTextCharacterSize;
            scoreText.anchor = TextAnchor.MiddleCenter;
            scoreText.alignment = TextAlignment.Center;
            scoreText.fontStyle = FontStyle.Bold;
            scoreText.color = new Color(1f, 0.97f, 0.72f, 1f);
            DotArenaSpriteFactory.ConfigureTextRenderer(scoreText.GetComponent<MeshRenderer>(), PlayerTextSortingOrder);

            var view = new DotView(viewRoot, renderer, outlineRenderer, nameText, scoreText);
            view.SetIdentity(playerId, 0);
            view.ApplyPresentation(DotArenaPresentation.ResolvePlayerColor(playerId, cosmeticId), PlayerLifeState.Idle, true, GameplayConfig.PlayerVisualRadius);
            return view;
        }

        private SpriteRenderer CreateRect(Transform parent, string objectName, Vector2 position, Vector2 size, Color color,
            int sortingOrder)
        {
            var rectangle = new GameObject(objectName);
            rectangle.transform.SetParent(parent, false);
            rectangle.transform.localPosition = new Vector3(position.x, position.y, 0f);
            rectangle.transform.localScale = new Vector3(size.x, size.y, 1f);

            var renderer = rectangle.AddComponent<SpriteRenderer>();
            renderer.sprite = _pixelSprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private PickupView CreatePickupView(PickupType pickupType)
        {
            var pickupRoot = new GameObject($"{pickupType}Pickup");
            pickupRoot.transform.SetParent(transform, false);

            var pickupColor = DotArenaPresentation.GetPickupColor(pickupType);
            var renderer = pickupRoot.AddComponent<SpriteRenderer>();
            renderer.sprite = _playerSprite;
            renderer.color = pickupColor;
            renderer.sortingOrder = PickupSortingOrder;
            renderer.material = CreatePickupAbsorbMaterial(pickupColor);

            var glow = new GameObject("Glow");
            glow.transform.SetParent(pickupRoot.transform, false);
            glow.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            glow.transform.localScale = Vector3.one * 1.24f;

            var glowRenderer = glow.AddComponent<SpriteRenderer>();
            glowRenderer.sprite = _playerOutlineSprite;
            glowRenderer.color = Color.Lerp(pickupColor, Color.white, 0.35f);
            glowRenderer.sortingOrder = PickupSortingOrder - 1;

            var label = new GameObject("Label");
            label.transform.SetParent(pickupRoot.transform, false);
            label.transform.localPosition = new Vector3(0f, 0f, PlayerTextDepth);
            label.transform.localScale = Vector3.one * PickupLabelScale;

            var labelText = label.AddComponent<TextMesh>();
            labelText.text = DotArenaPresentation.GetPickupDisplayName(pickupType);
            labelText.fontSize = 64;
            labelText.characterSize = 0.12f;
            labelText.anchor = TextAnchor.MiddleCenter;
            labelText.alignment = TextAlignment.Center;
            labelText.fontStyle = FontStyle.Bold;
            labelText.color = DotArenaPresentation.GetPickupLabelColor(pickupType);
            DotArenaSpriteFactory.ConfigureTextRenderer(labelText.GetComponent<MeshRenderer>(), PickupLabelSortingOrder);

            pickupRoot.SetActive(false);
            return new PickupView(pickupRoot, renderer, glowRenderer, labelText);
        }

        private Material CreatePickupAbsorbMaterial(Color baseColor)
        {
            if (_pickupAbsorbShader == null)
            {
                _pickupAbsorbShader = Shader.Find(PickupAbsorbShaderName);
            }

            var shader = _pickupAbsorbShader != null ? _pickupAbsorbShader : Shader.Find("Sprites/Default");
            var material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            if (_pickupAbsorbShader != null)
            {
                material.SetFloat("_Dissolve", 0f);
                if (material.HasProperty("_EdgeColor"))
                {
                    material.SetColor("_EdgeColor", Color.Lerp(baseColor, Color.white, 0.55f));
                }
            }

            return material;
        }

        private Material CreateJellyMaterial(float phase, float wobbleSpeed, float wobbleAmount)
        {
            if (_jellyShader == null)
            {
                _jellyShader = Shader.Find(JellyShaderName);
            }

            var shader = _jellyShader != null ? _jellyShader : Shader.Find("Sprites/Default");
            var material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            if (_jellyShader != null)
            {
                material.SetFloat("_Phase", phase);
                material.SetFloat("_WobbleSpeed", wobbleSpeed);
                material.SetFloat("_WobbleAmount", wobbleAmount);
            }

            return material;
        }

        private string GetLocalPlayerScoreText()
        {
            if (_localPlayerId.Length == 0)
            {
                return "0";
            }

            return _renderStates.TryGetValue(_localPlayerId, out var renderState)
                ? $"{DotArenaPresentation.FormatScore(renderState.Score)} / {DotArenaPresentation.FormatMass(renderState.Mass)}"
                : "0";
        }

        private int GetLocalPlayerScoreValue()
        {
            if (_localPlayerId.Length == 0)
            {
                return 0;
            }

            return _renderStates.TryGetValue(_localPlayerId, out var renderState)
                ? renderState.Score
                : 0;
        }

        private string GetLocalPlayerBuffText()
        {
            if (_localPlayerId.Length == 0 || !_renderStates.TryGetValue(_localPlayerId, out var renderState))
            {
                return "mass 0";
            }

            return $"mass {DotArenaPresentation.FormatMass(renderState.Mass)} / speed {renderState.MoveSpeed:0.0}";
        }

        private string GetCurrentEventMessage()
        {
            if (_eventMessageUntil > 0f && Time.time > _eventMessageUntil)
            {
                _eventMessageUntil = 0f;
                if (_views.Count < 2)
                {
                    _eventMessage = "等待玩家加入";
                }
                else
                {
                    _eventMessage = "对局进行中";
                }
            }

            return _eventMessage;
        }

        private void PushEvent(string message, float durationSeconds = 3f)
        {
            _eventMessage = message;
            _eventMessageUntil = Time.time + durationSeconds;
        }

        private SinglePlayerMatchPreset GetNextSinglePlayerPreset()
        {
            if (_singlePlayerPlaylistIndex < 0)
            {
                _singlePlayerPlaylistIndex = 0;
            }

            var preset = SinglePlayerPlaylist[_singlePlayerPlaylistIndex];
            _singlePlayerPlaylistIndex = (_singlePlayerPlaylistIndex + 1) % SinglePlayerPlaylist.Length;
            return preset;
        }

        private SinglePlayerMatchPreset GetPreviewSinglePlayerPreset()
        {
            if (_singlePlayerPlaylistIndex < 0)
            {
                _singlePlayerPlaylistIndex = 0;
            }

            return SinglePlayerPlaylist[_singlePlayerPlaylistIndex];
        }

        private SinglePlayerMatchPreset AdvanceSinglePlayerPresetSelection()
        {
            if (_singlePlayerPlaylistIndex < 0)
            {
                _singlePlayerPlaylistIndex = 0;
            }
            else
            {
                _singlePlayerPlaylistIndex = (_singlePlayerPlaylistIndex + 1) % SinglePlayerPlaylist.Length;
            }

            return SinglePlayerPlaylist[_singlePlayerPlaylistIndex];
        }

        private ArenaSimulationOptions CreateSinglePlayerOptions(SinglePlayerMatchPreset preset)
        {
            var options = new ArenaSimulationOptions
            {
                Arena = CreateArenaConfig(preset.MapVariant),
                RespawnDelaySeconds = 5f,
                FoodTargetCount = 96,
                InitialMass = 24f,
                RespawnMass = 24f,
                EnabledPickupTypes = new[]
                {
                    PickupType.SpeedBoost,
                    PickupType.KnockbackBoost,
                    PickupType.ScorePoint,
                    PickupType.Shield,
                    PickupType.BonusScore
                }
            };

            switch (preset.RuleVariant)
            {
                case ArenaRuleVariant.ScoreRush:
                    options.MaxRoundSeconds = 85f;
                    options.RespawnDelaySeconds = 3f;
                    options.FoodTargetCount = 132;
                    options.FoodMassGain = 1.45f;
                    options.BaseMoveSpeed = 9.4f;
                    options.ShrinkStartDelaySeconds = 26f;
                    options.ShrinkDurationSeconds = 28f;
                    break;
                case ArenaRuleVariant.ArenaCollapse:
                    options.MaxRoundSeconds = 70f;
                    options.RespawnDelaySeconds = 8f;
                    options.FoodTargetCount = 88;
                    options.BaseMoveSpeed = 8.4f;
                    options.EatMassRatio = 1.12f;
                    options.ShrinkStartDelaySeconds = 8f;
                    options.ShrinkDurationSeconds = 20f;
                    options.FinalArenaHalfExtents = new Vector2(8f, 8f);
                    break;
            }

            return options;
        }

        private static ArenaConfig CreateArenaConfig(ArenaMapVariant mapVariant)
        {
            return mapVariant switch
            {
                ArenaMapVariant.NarrowBridge => new ArenaConfig
                {
                    ArenaHalfExtents = new Vector2(52f, 18f),
                    RespawnInset = 4f,
                    PlayerCollisionRadius = GameplayConfig.PlayerCollisionRadius,
                    PlayerVisualRadius = GameplayConfig.PlayerVisualRadius,
                    PickupCollisionRadius = GameplayConfig.PickupCollisionRadius,
                    PickupSpawnInset = 3.5f
                },
                ArenaMapVariant.FinalRing => new ArenaConfig
                {
                    ArenaHalfExtents = new Vector2(34f, 34f),
                    RespawnInset = 4f,
                    PlayerCollisionRadius = GameplayConfig.PlayerCollisionRadius,
                    PlayerVisualRadius = GameplayConfig.PlayerVisualRadius,
                    PickupCollisionRadius = GameplayConfig.PickupCollisionRadius,
                    PickupSpawnInset = 3f
                },
                _ => new ArenaConfig
                {
                    ArenaHalfExtents = GameplayConfig.ArenaHalfExtents,
                    RespawnInset = GameplayConfig.RespawnInset,
                    PlayerCollisionRadius = GameplayConfig.PlayerCollisionRadius,
                    PlayerVisualRadius = GameplayConfig.PlayerVisualRadius,
                    PickupCollisionRadius = GameplayConfig.PickupCollisionRadius,
                    PickupSpawnInset = GameplayConfig.PickupSpawnInset
                }
            };
        }

        private static string GetSinglePlayerPresetLabel(ArenaMapVariant mapVariant, ArenaRuleVariant ruleVariant)
        {
            return $"{GetArenaMapVariantName(mapVariant)} / {GetArenaRuleVariantName(ruleVariant)}";
        }

        private static string GetArenaMapVariantName(ArenaMapVariant mapVariant)
        {
            return mapVariant switch
            {
                ArenaMapVariant.NarrowBridge => "Narrow Bridge",
                ArenaMapVariant.FinalRing => "Final Ring",
                _ => "Classic Square"
            };
        }

        private static string GetArenaRuleVariantName(ArenaRuleVariant ruleVariant)
        {
            return ruleVariant switch
            {
                ArenaRuleVariant.ScoreRush => "Score Rush",
                ArenaRuleVariant.ArenaCollapse => "Arena Collapse",
                _ => "Classic Elimination"
            };
        }

        private string BuildSettlementDetail(SessionMode sessionMode, int localScore, int localWinCount, string winnerPlayerId, bool localPlayerWon)
        {
            var modeText = sessionMode == SessionMode.SinglePlayer ? "Single-player" : "Multiplayer";
            var resultText = localPlayerWon ? "Victory" : "Defeat";
            var presetLine = sessionMode == SessionMode.SinglePlayer
                ? $"\nPreset: {GetSinglePlayerPresetLabel(_currentArenaMapVariant, _currentArenaRuleVariant)}"
                : string.Empty;
            var followupLine = sessionMode == SessionMode.Multiplayer
                ? "\nNext: Return to Lobby to start another online match."
                : $"\nNext: Return to Mode Select or replay {GetSinglePlayerPresetLabel(_currentArenaMapVariant, _currentArenaRuleVariant)}.";
            return $"Mode: {modeText}{presetLine}\nResult: {resultText}\nWinner: {winnerPlayerId}\nScore: {localScore}\nWins: {localWinCount}{followupLine}";
        }

        private string BuildSettlementRewardSummary(SessionMode sessionMode)
        {
            if (_lastRewardSummary == null)
            {
                return sessionMode == SessionMode.Multiplayer
                    ? "Rewards: pending profile sync."
                    : "Rewards: none recorded yet.";
            }

            return $"Rewards: +{_lastRewardSummary.ExperienceGained} XP, +{_lastRewardSummary.CurrencyGained} Coins, Level {_lastRewardSummary.NewLevel}";
        }

        private string BuildSettlementTaskSummary(SessionMode sessionMode)
        {
            var meta = _metaState;
            if (meta == null)
            {
                return "Tasks: no profile data available.";
            }

            var readySummary = DotArenaMetaProgression.GetClaimableTaskSummary(meta);
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

        private string BuildSettlementNextStepSummary(SessionMode sessionMode)
        {
            return sessionMode == SessionMode.Multiplayer
                ? "Next: Return to Lobby, then Start Match to queue again."
                : $"Next: Return to Mode Select or replay {GetSinglePlayerPresetLabel(_currentArenaMapVariant, _currentArenaRuleVariant)}.";
        }

        private string BuildDebugPanelDetail()
        {
            var endpoint = Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path);
            var mode = _sessionMode switch
            {
                SessionMode.SinglePlayer => "Single-player",
                SessionMode.Multiplayer => "Multiplayer",
                _ => "None"
            };

            return
                $"Status: {_status}\n" +
                $"Flow: {_flowState} / Entry: {_entryMenuState}\n" +
                $"Mode: {mode}\n" +
                $"Player: {_localPlayerId}\n" +
                $"Hint: W/A/S/D move, eat pellets, avoid larger cells, P debug\n" +
                $"Tick: {_lastWorldTick}\n" +
                $"Views: {_views.Count}\n" +
                $"Mass: {GetLocalPlayerBuffText()}\n" +
                $"Event: {GetCurrentEventMessage()}\n" +
                $"Endpoint: {endpoint}\n" +
                $"Connected: {IsConnected} / Connecting: {IsConnecting}";
        }

        private string BuildMatchmakingDetail()
        {
            if (_sessionMode == SessionMode.SinglePlayer)
            {
                return $"Preset: {GetSinglePlayerPresetLabel(_currentArenaMapVariant, _currentArenaRuleVariant)}\nSpawning the local arena and filling the roster with bots.";
            }

            return $"{_status}\n{GetCurrentEventMessage()}";
        }

        private string BuildMetaPlayerSummary()
        {
            var meta = _metaState;
            if (meta == null)
            {
                return "Guest profile";
            }

            if (IsInMultiplayerLobby())
            {
                return $"{meta.PlayerId}   Wins {meta.TotalWins}   Coins {meta.SoftCurrency}   Online Ready";
            }

            return $"{meta.PlayerId}   Lv.{meta.Level}   XP {meta.Experience}/{GetMetaNextLevelRequirement(meta.Level)}   Coins {meta.SoftCurrency}";
        }

        private string BuildMetaLobbyHighlights()
        {
            var meta = _metaState;
            if (meta == null)
            {
                return string.Empty;
            }

            var readyTaskCount = DotArenaMetaProgression.GetClaimableTaskCount(meta);
            var recentSummary = DotArenaMetaProgression.GetRecentMatchSummary(meta);
            var recentResult = recentSummary.HasRecord
                ? $"{recentSummary.Mode} / {recentSummary.Result}"
                : "No recent result";
            var shopSummary = DotArenaMetaProgression.GetShopAvailabilitySummary(meta);

            if (IsInMultiplayerLobby())
            {
                return $"Ready to match now   |   Recent: {recentResult}   |   Claimable tasks: {readyTaskCount}   |   Shop ready: {shopSummary.AffordableAndUnownedCount}";
            }

            var previewPreset = GetPreviewSinglePlayerPreset();
            return $"Next preset: {GetSinglePlayerPresetLabel(previewPreset.MapVariant, previewPreset.RuleVariant)}   |   Recent: {recentResult}   |   Claimable tasks: {readyTaskCount}   |   Shop ready: {shopSummary.AffordableAndUnownedCount}";
        }

        private string BuildMetaProfileDetail()
        {
            var meta = _metaState;
            if (meta == null)
            {
                return "No profile data loaded.";
            }

            var previewPreset = GetPreviewSinglePlayerPreset();
            var modeLine = IsInMultiplayerLobby()
                ? $"Lobby: Multiplayer lobby ready as {meta.PlayerId}\nEndpoint: {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}\nAction: Start Match to enter online queue"
                : $"Next local preset: {GetSinglePlayerPresetLabel(previewPreset.MapVariant, previewPreset.RuleVariant)}";

            var lastReward = _lastRewardSummary == null
                ? "No recent reward summary."
                : $"Last rewards: +{_lastRewardSummary.ExperienceGained} XP, +{_lastRewardSummary.CurrencyGained} Coins, Level {_lastRewardSummary.NewLevel}";
            return $"Wins: {meta.TotalWins}\nMatches: {meta.TotalMatches}\nStreak: {meta.CurrentLoginStreak}\nEquipped: {meta.EquippedCosmeticId}\n{modeLine}\n{lastReward}";
        }

        private string BuildMetaTasksDetail()
        {
            var meta = _metaState;
            if (meta == null)
            {
                return "No tasks available.";
            }

            var lines = new List<string>();
            var readySummary = DotArenaMetaProgression.GetClaimableTaskSummary(meta);
            lines.Add($"Ready to claim: {readySummary.TotalClaimableCount} (Daily {readySummary.DailyClaimableCount} / New {readySummary.NewPlayerClaimableCount})");
            foreach (var task in meta.DailyTasks)
            {
                lines.Add($"Daily: {task.Title} ({task.Progress}/{task.Target}) {(task.Claimed ? "[Claimed]" : string.Empty)}".TrimEnd());
            }

            foreach (var task in meta.NewPlayerTasks)
            {
                lines.Add($"New: {task.Title} ({task.Progress}/{task.Target}) {(task.Claimed ? "[Claimed]" : string.Empty)}".TrimEnd());
            }

            return lines.Count == 0 ? "No task data." : string.Join("\n", lines);
        }

        private string BuildMetaShopDetail()
        {
            var meta = _metaState;
            if (meta == null)
            {
                return "No shop data.";
            }

            var lines = new List<string>();
            var availability = DotArenaMetaProgression.GetShopAvailabilitySummary(meta);
            var cheapest = availability.CheapestAffordableUnownedItem?.Name ?? "None";
            lines.Add($"Affordable now: {availability.AffordableAndUnownedCount}");
            lines.Add($"Cheapest next item: {cheapest}");
            foreach (var item in DotArenaMetaProgression.GetShopCatalog())
            {
                var state = meta.OwnedCosmeticIds.Contains(item.Id)
                    ? (meta.EquippedCosmeticId == item.Id ? "Equipped" : "Owned")
                    : $"{item.Price} Coins";
                lines.Add($"{item.Name}: {state}");
            }

            return string.Join("\n", lines);
        }

        private string BuildMetaRecordsDetail()
        {
            var meta = _metaState;
            if (meta == null || meta.MatchHistory.Count == 0)
            {
                return "No recent matches.";
            }

            var recent = DotArenaMetaProgression.GetRecentMatchSummary(meta);
            var trend = DotArenaMetaProgression.GetRecentMatchTrendSummary(meta, 5);
            var lines = new List<string>();
            if (IsInMultiplayerLobby())
            {
                lines.Add("Recent Match");
            }
            else
            {
                lines.Add("Recent Match");
            }

            lines.Add($"Mode: {recent.Mode}");
            lines.Add($"Result: {recent.Result}");
            lines.Add($"Score: {recent.Score}");
            lines.Add($"Winner: {recent.WinnerPlayerId}");
            lines.Add(string.IsNullOrWhiteSpace(recent.PlayedAtUtcIso) ? "Played: Unknown" : $"Played: {recent.PlayedAtUtcIso[..Math.Min(10, recent.PlayedAtUtcIso.Length)]}");
            lines.Add(string.Empty);
            lines.Add("Trend Summary");
            lines.Add($"Window: Last {trend.SampleCount}");
            lines.Add($"Form: {trend.FormStrip}");
            lines.Add($"Wins / Losses: {trend.WinCount} / {trend.LossCount}");
            lines.Add($"Trend: {trend.TrendLabel}");
            lines.Add($"Streak: {trend.CurrentStreakType} {trend.CurrentStreak}");
            lines.Add($"Avg Score: {trend.AverageScore}");
            lines.Add($"Best Score: {trend.BestScore}");
            lines.Add(string.Empty);
            lines.Add("Recent History");
            var count = Math.Min(5, meta.MatchHistory.Count);
            for (var i = 0; i < count; i++)
            {
                var record = meta.MatchHistory[i];
                var date = record.PlayedAtUtcIso.Length >= 10 ? record.PlayedAtUtcIso[..10] : record.PlayedAtUtcIso;
                lines.Add($"{date}  {record.Mode}  {record.Result}  Score {record.Score}");
            }

            return string.Join("\n", lines);
        }

        private string BuildMetaLeaderboardDetail()
        {
            var meta = _metaState;
            if (meta == null)
            {
                return "No leaderboard data.";
            }

            var summary = DotArenaMetaProgression.GetLeaderboardSummary(meta);
            if (IsInMultiplayerLobby())
            {
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

            var localLines = new List<string>
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
                localLines.Add($"{entry.Position}. {entry.Name} - {entry.Wins} wins / {entry.Matches} matches{marker}");
            }

            return string.Join("\n", localLines);
        }

        private string BuildMetaSettingsDetail()
        {
            var meta = _metaState;
            if (meta == null)
            {
                return "No settings loaded.";
            }

            return $"Master Volume: {meta.Settings.MasterVolume:0.0}\nMusic Volume: {meta.Settings.MusicVolume:0.0}\nSfx Volume: {meta.Settings.SfxVolume:0.0}\nLanguage: {meta.Settings.Language}\nFullscreen: {meta.Settings.Fullscreen}";
        }

        private string BuildMetaFooterHint()
        {
            return IsInMultiplayerLobby()
                ? "Start Match enters queue. Log Out returns to mode select."
                : "Use the top tabs to switch sections. Start from Lobby or Tasks.";
        }

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
                var previewPreset = GetPreviewSinglePlayerPreset();
                PushEvent($"Next local preset: {GetSinglePlayerPresetLabel(previewPreset.MapVariant, previewPreset.RuleVariant)}", 4f);
                return;
            }

            var selectedPreset = AdvanceSinglePlayerPresetSelection();
            PushEvent($"Preset switched to {GetSinglePlayerPresetLabel(selectedPreset.MapVariant, selectedPreset.RuleVariant)}", 4f);
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

        private static string GetRematchButtonLabel(SessionMode sessionMode)
        {
            return sessionMode == SessionMode.SinglePlayer ? "Play Again" : "Queue Again";
        }

        private void EnsureMetaState(string playerId)
        {
            _metaState = DotArenaMetaProgression.LoadOrCreate(playerId);
        }

        private static int GetMetaNextLevelRequirement(int level)
        {
            return 100 + ((Math.Max(1, level) - 1) * 25);
        }


        private static float ArenaHalfWidth => GameplayConfig.ArenaHalfExtents.x;

        private static float ArenaHalfHeight => GameplayConfig.ArenaHalfExtents.y;

        private float CurrentArenaHalfWidth => _currentArenaHalfExtents.x;

        private float CurrentArenaHalfHeight => _currentArenaHalfExtents.y;

        private void UpdateArenaVisuals()
        {
            if (_safeZoneRenderer != null)
            {
                _safeZoneRenderer.transform.localScale = new Vector3(CurrentArenaHalfWidth * 2f, CurrentArenaHalfHeight * 2f, 1f);
            }

            UpdateBorderRenderer(_topBorderRenderer, new Vector2(0f, CurrentArenaHalfHeight),
                new Vector2(CurrentArenaHalfWidth * 2f + 0.18f, 0.18f));
            UpdateBorderRenderer(_bottomBorderRenderer, new Vector2(0f, -CurrentArenaHalfHeight),
                new Vector2(CurrentArenaHalfWidth * 2f + 0.18f, 0.18f));
            UpdateBorderRenderer(_leftBorderRenderer, new Vector2(-CurrentArenaHalfWidth, 0f),
                new Vector2(0.18f, CurrentArenaHalfHeight * 2f + 0.18f));
            UpdateBorderRenderer(_rightBorderRenderer, new Vector2(CurrentArenaHalfWidth, 0f),
                new Vector2(0.18f, CurrentArenaHalfHeight * 2f + 0.18f));
        }

        private static void UpdateBorderRenderer(SpriteRenderer? renderer, Vector2 position, Vector2 size)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.transform.localPosition = new Vector3(position.x, position.y, 0f);
            renderer.transform.localScale = new Vector3(size.x, size.y, 1f);
        }

        private static float PlayerVisualDiameter => GameplayConfig.PlayerVisualRadius * 2f;

        private void EnsurePlayerOverlay(string playerId)
        {
            var overlayLayer = _sceneUiPresenter.OverlayLayer;
            if (overlayLayer == null || _playerOverlayViews.ContainsKey(playerId))
            {
                return;
            }

            var root = new GameObject($"{playerId}Overlay", typeof(RectTransform));
            root.transform.SetParent(overlayLayer, false);

            var rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = new Vector2(140f, 40f);

            var nameObject = new GameObject("NameText", typeof(RectTransform), typeof(TextMeshProUGUI));
            nameObject.transform.SetParent(root.transform, false);
            var nameRect = (RectTransform)nameObject.transform;
            nameRect.anchorMin = new Vector2(0.5f, 0.5f);
            nameRect.anchorMax = new Vector2(0.5f, 0.5f);
            nameRect.pivot = new Vector2(0.5f, 0.5f);
            nameRect.anchoredPosition = new Vector2(0f, -10f);
            nameRect.sizeDelta = new Vector2(140f, 20f);

            var nameText = nameObject.GetComponent<TextMeshProUGUI>();
            nameText.font = ResolveOverlayFontAsset();
            nameText.fontSize = 16;
            nameText.fontStyle = FontStyles.Bold;
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.enableWordWrapping = false;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            nameText.color = UiPrimaryTextColor;

            var scoreObject = new GameObject("ScoreText", typeof(RectTransform), typeof(TextMeshProUGUI));
            scoreObject.transform.SetParent(root.transform, false);
            var scoreRect = (RectTransform)scoreObject.transform;
            scoreRect.anchorMin = new Vector2(0.5f, 0.5f);
            scoreRect.anchorMax = new Vector2(0.5f, 0.5f);
            scoreRect.pivot = new Vector2(0.5f, 0.5f);
            scoreRect.anchoredPosition = new Vector2(0f, 8f);
            scoreRect.sizeDelta = new Vector2(140f, 18f);

            var scoreText = scoreObject.GetComponent<TextMeshProUGUI>();
            scoreText.font = ResolveOverlayFontAsset();
            scoreText.fontSize = 14;
            scoreText.fontStyle = FontStyles.Bold;
            scoreText.alignment = TextAlignmentOptions.Center;
            scoreText.enableWordWrapping = false;
            scoreText.overflowMode = TextOverflowModes.Ellipsis;
            scoreText.color = UiAccentTextColor;

            _playerOverlayViews.Add(playerId, new PlayerOverlayView(root, rootRect, nameText, scoreText));
        }

        private static TMP_FontAsset? ResolveOverlayFontAsset()
        {
            var projectFont = Resources.Load<TMP_FontAsset>(TmpFallbackFontAssetResourcePath);
            if (projectFont != null)
            {
                return projectFont;
            }

            if (TMP_Settings.defaultFontAsset != null)
            {
                return TMP_Settings.defaultFontAsset;
            }

            return Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        }

        private void UpdatePlayerOverlayViews()
        {
            if (_sceneUiPresenter.OverlayLayer == null)
            {
                return;
            }

            var camera = Camera.main;
            if (camera == null)
            {
                foreach (var overlay in _playerOverlayViews.Values)
                {
                    overlay.Root.SetActive(false);
                }

                return;
            }

            var pixelsPerWorldUnit = Screen.height / (camera.orthographicSize * 2f);
            var diameterPixels = PlayerVisualDiameter * pixelsPerWorldUnit;
            var labelWidth = Mathf.Max(96f, diameterPixels * 2f);
            var nameHeight = Mathf.Max(18f, diameterPixels * 0.36f);
            var scoreHeight = Mathf.Max(16f, diameterPixels * 0.3f);

            foreach (var entry in _playerOverlayViews)
            {
                if (!_views.TryGetValue(entry.Key, out var view))
                {
                    entry.Value.Root.SetActive(false);
                    continue;
                }

                var screenPosition = camera.WorldToScreenPoint(view.Root.transform.position);
                if (screenPosition.z <= 0f)
                {
                    entry.Value.Root.SetActive(false);
                    continue;
                }

                entry.Value.Root.SetActive(true);
                entry.Value.RootRect.anchoredPosition = new Vector2(screenPosition.x, screenPosition.y);
                entry.Value.RootRect.sizeDelta = new Vector2(labelWidth, nameHeight + scoreHeight + 4f);

                var nameRect = entry.Value.NameText.rectTransform;
                nameRect.sizeDelta = new Vector2(labelWidth, nameHeight);
                nameRect.anchoredPosition = new Vector2(0f, nameHeight * 0.55f);
                entry.Value.NameText.fontSize = Mathf.RoundToInt(Mathf.Clamp(diameterPixels * 0.24f, 14f, 22f));

                var scoreRect = entry.Value.ScoreText.rectTransform;
                scoreRect.sizeDelta = new Vector2(labelWidth, scoreHeight);
                scoreRect.anchoredPosition = new Vector2(0f, -(scoreHeight * 0.55f));
                entry.Value.ScoreText.fontSize = Mathf.RoundToInt(Mathf.Clamp(diameterPixels * 0.22f, 13f, 20f));
            }
        }

    }
}
