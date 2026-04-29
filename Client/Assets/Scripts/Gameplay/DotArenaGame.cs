#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Shared.Gameplay;
using Shared.Interfaces;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame : MonoBehaviour, IPlayerCallback
    {
        [SerializeField] private string _host = "127.0.0.1";
        [SerializeField] private int _port = 20000;
        [SerializeField] private string _path = "/ws";
        [SerializeField] private string _account = "a";
        [SerializeField] private string _password = "b";

        private readonly CancellationTokenSource _cts = new();
        private readonly DotArenaCallbackInbox _callbackInbox = new();
        private readonly DotArenaSceneUiPresenter _sceneUiPresenter = new();
        private readonly DotArenaPlayerOverlayPresenter _overlayPresenter = new();
        private readonly Dictionary<string, DotView> _views = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PlayerRenderState> _renderStates = new(StringComparer.Ordinal);
        private readonly List<PickupView> _pickupViews = new();

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
        private string _status = "连接中...";
        private string _eventMessage = "等待玩家加入";
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
            _overlayPresenter.Views,
            CreateView,
            playerId => _overlayPresenter.EnsureOverlay(_sceneUiPresenter, playerId),
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
    }
}
