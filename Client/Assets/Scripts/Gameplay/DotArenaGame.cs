#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Shared.Gameplay;
using Shared.Interfaces;
using ULinkRPC.Client;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
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
        private const int WindowWidth = 1200;
        private const int WindowHeight = 600;
        private const float ArenaVisualPadding = 1.8f;
        private const float FollowCameraSize = 11.8f;
        private const float CameraFollowSharpness = 7f;
        private const float PlayerNameOffsetY = 0.11f;
        private const float PlayerScoreOffsetY = -0.13f;
        private const float PickupPulseAmplitude = 0.08f;
        private const float PickupPulseFrequency = 3.2f;
        private const float PickupAbsorbDurationSeconds = 0.26f;
        private const string JellyShaderName = "SampleClient/PlayerJelly";
        private const string PickupAbsorbShaderName = "SampleClient/PickupAbsorb";
        private const int PlayerSortingOrder = 20;
        private const int PlayerOutlineSortingOrder = 25;
        private const int PlayerTextBackdropSortingOrder = 28;
        private const int PlayerTextSortingOrder = 30;
        private const int PickupSortingOrder = 12;
        private const int PickupLabelSortingOrder = 14;
        private const float PlayerTextDepth = -0.2f;
        private const float PlayerNameScale = 0.16f;
        private const float PlayerScoreScale = 0.13f;
        private const float PickupLabelScale = 0.45f;
        private const float PlayerTextCharacterSize = 0.4f;
        private const float PlayerNameBackdropWidth = 1.1f;
        private const float PlayerNameBackdropHeight = 0.24f;
        private const float PlayerScoreBackdropWidth = 0.8f;
        private const float PlayerScoreBackdropHeight = 0.2f;
        private const float InputSendIntervalSeconds = 0.05f;
        private const float SinglePlayerTickSeconds = 0.05f;
        private const int MaxSinglePlayerCatchUpTicks = 4;
        private const float InterpolationDurationSeconds = 0.1f;

        private static readonly Color BackgroundColor = new(0.02f, 0.03f, 0.05f, 1f);
        private static readonly Color BoardColor = new(0.08f, 0.1f, 0.14f, 1f);
        private static readonly Color SafeZoneColor = new(0.14f, 0.18f, 0.24f, 0.9f);
        private static readonly Color GridColor = new(0.75f, 0.86f, 0.94f, 0.1f);
        private static readonly Color BorderColor = new(1f, 0.84f, 0.31f, 0.24f);
        private static readonly Color DangerColor = new(1f, 0.24f, 0.24f, 0.08f);
        private static readonly Color PlayerOutlineColor = new(1f, 1f, 1f, 0.92f);
        private static readonly Color PlayerTextBackdropColor = new(0.04f, 0.06f, 0.1f, 0.72f);
        private static readonly Color UiPanelBackgroundColor = new(0.07f, 0.1f, 0.14f, 0.96f);
        private static readonly Color UiInputBackgroundColor = new(0.14f, 0.19f, 0.25f, 1f);
        private static readonly Color UiPrimaryTextColor = new(0.96f, 0.98f, 1f, 1f);
        private static readonly Color UiSecondaryTextColor = new(0.84f, 0.9f, 0.96f, 1f);
        private static readonly Color UiMutedTextColor = new(0.73f, 0.8f, 0.88f, 1f);
        private static readonly Color UiAccentTextColor = new(1f, 0.92f, 0.7f, 1f);
        private static readonly Color ScorePickupColor = new(0.22f, 0.9f, 1f, 0.95f);
        private static readonly Color SpeedPickupColor = new(1f, 0.86f, 0.22f, 0.95f);
        private static readonly Color KnockbackPickupColor = new(1f, 0.22f, 0.22f, 0.95f);
        private static readonly ArenaConfig GameplayConfig = ArenaConfig.CreateDefault();
        private const string TmpFallbackFontAssetResourcePath = "Fonts & Materials/DotArenaCJK SDF";

        private static readonly Color[] RemotePalette =
        {
            new(0.2f, 0.96f, 0.67f, 1f),
            new(1f, 0.42f, 0.48f, 1f),
            new(1f, 0.74f, 0.18f, 1f),
            new(0.33f, 0.76f, 1f, 1f),
            new(0.88f, 0.49f, 1f, 1f),
            new(1f, 0.61f, 0.3f, 1f)
        };

        [SerializeField] private string _host = "127.0.0.1";
        [SerializeField] private int _port = 20000;
        [SerializeField] private string _path = "/ws";
        [SerializeField] private string _account = "a";
        [SerializeField] private string _password = "b";

        private readonly CancellationTokenSource _cts = new();
        private readonly object _callbackLock = new();
        private readonly Dictionary<string, DotView> _views = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PlayerRenderState> _renderStates = new(StringComparer.Ordinal);
        private readonly Dictionary<PickupType, PickupView> _pickupViews = new();
        private readonly Dictionary<string, PlayerOverlayView> _playerOverlayViews = new(StringComparer.Ordinal);

        private RpcClient? _connection;
        private IPlayerService? _playerService;
        private ArenaSimulation? _localMatch;
        private string _localPlayerId = string.Empty;
        private bool _isConnected;
        private bool _isConnecting;
        private bool _singlePlayerStartRequested;
        private EntryMenuState _entryMenuState = EntryMenuState.ModeSelect;
        private SessionMode _sessionMode = SessionMode.None;
        private int _inputTick;
        private bool _dashQueued;
        private float _nextInputAt;
        private float _singlePlayerTickAccumulator;

        private WorldState? _pendingWorldState;
        private readonly Queue<PlayerDead> _pendingDeaths = new();
        private MatchEnd? _pendingMatchEnd;
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
        private string _lastLoggedInputVector = string.Empty;
        private GameObject? _sceneUiRoot;
        private GameObject? _hudPanel;
        private GameObject? _entryPanel;
        private RectTransform? _overlayLayer;
        private GameObject? _modeSelectPanel;
        private GameObject? _multiplayerPanel;
        private TMP_Text? _hudTitleText;
        private TMP_Text? _hudStatusText;
        private TMP_Text? _hudPlayerText;
        private TMP_Text? _hudTickText;
        private TMP_Text? _hudModeText;
        private TMP_Text? _hudHintText;
        private TMP_Text? _hudEventText;
        private TMP_Text? _hudCountdownText;
        private int _lastRoundRemainingSeconds;
        private TMP_Text? _entryTitleText;
        private TMP_Text? _entryStatusText;
        private TMP_Text? _modeSelectDescriptionText;
        private TMP_Text? _multiplayerSubtitleText;
        private TMP_Text? _accountLabelText;
        private TMP_Text? _passwordLabelText;
        private TMP_Text? _accountPlaceholderText;
        private TMP_Text? _passwordPlaceholderText;
        private Button? _singlePlayerButton;
        private Button? _multiplayerButton;
        private Button? _matchButton;
        private Button? _backButton;
        private TMP_Text? _singlePlayerButtonText;
        private TMP_Text? _multiplayerButtonText;
        private TMP_Text? _matchButtonText;
        private TMP_Text? _backButtonText;
        private TMP_InputField? _accountInputField;
        private TMP_InputField? _passwordInputField;
        private TMP_FontAsset? _tmpFontAsset;
        private SpriteRenderer? _safeZoneRenderer;
        private SpriteRenderer? _topBorderRenderer;
        private SpriteRenderer? _bottomBorderRenderer;
        private SpriteRenderer? _leftBorderRenderer;
        private SpriteRenderer? _rightBorderRenderer;
        private Vector2 _currentArenaHalfExtents = GameplayConfig.ArenaHalfExtents;
#if UNITY_EDITOR
        private Vector2 _editorMoveOverride;
        private bool _editorDashOverride;
        private bool _hasEditorInputOverride;
#endif

        private bool HasActiveSession => _sessionMode != SessionMode.None;

        private void Start()
        {
            ApplyLaunchOverrides();
            ConfigureWindow();
            InitializeConnectionMode();
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
            if (_sceneUiRoot != null)
            {
                return;
            }

            if (!HasActiveSession)
            {
                DrawEntryMenu();
                return;
            }

            const float width = 400f;
            const float height = 160f;

            var boxRect = new Rect(16f, 16f, width, height);
            var contentRect = new Rect(28f, 24f, width - 24f, height - 16f);

            var previousColor = GUI.color;
            GUI.color = new Color(0.04f, 0.06f, 0.08f, 0.9f);
            GUI.Box(boxRect, GUIContent.none);
            GUI.color = previousColor;

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.86f, 0.91f, 0.96f, 1f) }
            };

            GUI.Label(new Rect(contentRect.x, contentRect.y, contentRect.width, 24f), "ULinkRPC Dot Arena", titleStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 24f, contentRect.width, 18f), $"\u72b6\u6001: {_status}", bodyStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 44f, contentRect.width, 18f),
                $"\u73a9\u5bb6: {(_localPlayerId.Length > 0 ? _localPlayerId : _account)}   \u79ef\u5206: {GetLocalPlayerScoreText()}   \u80dc\u573a: {_localWinCount}", bodyStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 64f, contentRect.width, 18f),
                $"\u670d\u52a1\u7aef Tick: {_lastWorldTick}   \u540c\u6b65\u4eba\u6570: {_views.Count}   Buff: {GetLocalPlayerBuffText()}", bodyStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 84f, contentRect.width, 18f),
                _sessionMode == SessionMode.SinglePlayer
                    ? "\u6a21\u5f0f: \u672c\u5730\u5355\u673a"
                    : $"\u5730\u5740: {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}", bodyStyle);

            GUI.Label(new Rect(contentRect.x, contentRect.y + 104f, contentRect.width, 18f),
                "W/A/S/D \u79fb\u52a8, Space \u51b2\u523a\u3002\u5ba2\u6237\u7aef\u53ea\u53d1\u8f93\u5165\uff0c\u4f4d\u7f6e\u4ee5\u670d\u52a1\u7aef\u5e7f\u64ad\u4e3a\u51c6\u3002", bodyStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 124f, contentRect.width, 18f),
                $"\u4e8b\u4ef6: {GetCurrentEventMessage()}", bodyStyle);

            DrawPlayerOverlays();
        }

        private void DrawPlayerOverlays()
        {
            var camera = Camera.main;
            if (camera == null || _views.Count == 0)
            {
                return;
            }

            var pixelsPerWorldUnit = Screen.height / (camera.orthographicSize * 2f);
            var diameterPixels = PlayerVisualDiameter * pixelsPerWorldUnit;
            var labelWidth = Mathf.Max(96f, diameterPixels * 2f);
            var nameHeight = Mathf.Max(18f, diameterPixels * 0.36f);
            var scoreHeight = Mathf.Max(16f, diameterPixels * 0.3f);

            var nameStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(diameterPixels * 0.24f, 14f, 22f)),
                clipping = TextClipping.Overflow,
                normal = { textColor = new Color(0.94f, 0.97f, 1f, 1f) }
            };

            var scoreStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(diameterPixels * 0.22f, 13f, 20f)),
                clipping = TextClipping.Overflow,
                normal = { textColor = new Color(1f, 0.97f, 0.78f, 1f) }
            };

            foreach (var entry in _views)
            {
                if (!_renderStates.TryGetValue(entry.Key, out var renderState))
                {
                    continue;
                }

                var worldPosition = entry.Value.Root.transform.position;
                var screenPosition = camera.WorldToScreenPoint(worldPosition);
                if (screenPosition.z <= 0f)
                {
                    continue;
                }

                var centerX = screenPosition.x;
                var centerY = Screen.height - screenPosition.y;
                var nameRect = new Rect(centerX - (labelWidth * 0.5f), centerY - (nameHeight * 1.05f), labelWidth, nameHeight);
                var scoreRect = new Rect(centerX - (labelWidth * 0.5f), centerY + (scoreHeight * 0.05f), labelWidth, scoreHeight);

                GUI.Label(nameRect, entry.Key, nameStyle);
                GUI.Label(scoreRect, $"score: {FormatScore(renderState.Score)}", scoreStyle);
            }
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
            }
        }

        private void DrawModeSelect(Rect contentRect, GUIStyle bodyStyle)
        {
            GUI.Label(new Rect(contentRect.x, contentRect.y + 70f, contentRect.width, 36f),
                $"选择一种游玩方式。单机会直接进入并自动补足 AI 到 4 人。\n{GetMenuLoginStatusText()}", bodyStyle);

            var previousEnabled = GUI.enabled;
            GUI.enabled = !_isConnecting;
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
            GUI.enabled = !_isConnecting;
            _account = GUI.TextField(new Rect(contentRect.x + labelWidth + 14f, accountY, fieldWidth, fieldHeight), _account);
            _password = GUI.PasswordField(new Rect(contentRect.x + labelWidth + 14f, passwordY, fieldWidth, fieldHeight), _password, '*');

            if (GUI.Button(new Rect(contentRect.x + 8f, buttonY, 120f, 28f), _isConnecting ? "匹配中..." : "匹配"))
            {
                _ = ConnectAsync();
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
            if (_isConnecting)
            {
                return;
            }

            _singlePlayerStartRequested = true;
        }

        public void OnUiMultiplayerSelected()
        {
            if (_isConnecting)
            {
                return;
            }

            _entryMenuState = EntryMenuState.MultiplayerAuth;
            _status = "Enter account credentials";
            _eventMessage = "\u70b9\u51fb\u5339\u914d\u5f00\u59cb\u8054\u673a";
            SyncSceneUiInputs();
            RefreshSceneUi();
        }

        public void OnUiBackToModeSelect()
        {
            if (_isConnecting)
            {
                return;
            }

            _entryMenuState = EntryMenuState.ModeSelect;
            _status = "\u8bf7\u9009\u62e9\u6a21\u5f0f";
            _eventMessage = "\u8bf7\u9009\u62e9\u5355\u673a\u6216\u8054\u673a";
            RefreshSceneUi();
        }

        public void OnUiConnectRequested()
        {
            if (_isConnecting)
            {
                return;
            }

            _ = ConnectAsync();
        }

        public void OnUiAccountChanged(string value)
        {
            _account = value;
        }

        public void OnUiPasswordChanged(string value)
        {
            _password = value;
        }

        private void BindSceneUi()
        {
            _sceneUiRoot = FindSceneUiObject("SceneUI");
            if (_sceneUiRoot == null)
            {
                return;
            }

            _tmpFontAsset ??= LoadTmpFontAsset();
            ApplySceneUiFonts();

            _overlayLayer = FindSceneUiRect("SceneUI/OverlayLayer");
            _hudPanel = FindSceneUiObject("SceneUI/HUDPanel");
            _entryPanel = FindSceneUiObject("SceneUI/EntryPanel");
            _modeSelectPanel = FindSceneUiObject("SceneUI/EntryPanel/ModeSelectPanel");
            _multiplayerPanel = FindSceneUiObject("SceneUI/EntryPanel/MultiplayerPanel");

            _hudTitleText = FindSceneUiText("SceneUI/HUDPanel/TitleText");
            _hudStatusText = FindSceneUiText("SceneUI/HUDPanel/StatusText");
            _hudPlayerText = FindSceneUiText("SceneUI/HUDPanel/PlayerText");
            _hudTickText = FindSceneUiText("SceneUI/HUDPanel/TickText");
            _hudModeText = FindSceneUiText("SceneUI/HUDPanel/ModeText");
            _hudHintText = FindSceneUiText("SceneUI/HUDPanel/HintText");
            _hudEventText = FindSceneUiText("SceneUI/HUDPanel/EventText");
            _hudCountdownText = FindSceneUiText("SceneUI/HUDPanel/CountdownText");

            _entryTitleText = FindSceneUiText("SceneUI/EntryPanel/TitleText");
            _entryStatusText = FindSceneUiText("SceneUI/EntryPanel/StatusText");
            _modeSelectDescriptionText = FindSceneUiText("SceneUI/EntryPanel/ModeSelectPanel/DescriptionText");

            _multiplayerSubtitleText = FindSceneUiText("SceneUI/EntryPanel/MultiplayerPanel/SubtitleText");
            _accountLabelText = FindSceneUiText("SceneUI/EntryPanel/MultiplayerPanel/AccountLabel");
            _passwordLabelText = FindSceneUiText("SceneUI/EntryPanel/MultiplayerPanel/PasswordLabel");
            _accountPlaceholderText = FindSceneUiText("SceneUI/EntryPanel/MultiplayerPanel/AccountInput/Text Area/Placeholder");
            _passwordPlaceholderText = FindSceneUiText("SceneUI/EntryPanel/MultiplayerPanel/PasswordInput/Text Area/Placeholder");

            _singlePlayerButton = FindSceneUiButton("SceneUI/EntryPanel/ModeSelectPanel/SinglePlayerButton");
            _multiplayerButton = FindSceneUiButton("SceneUI/EntryPanel/ModeSelectPanel/MultiplayerButton");
            _matchButton = FindSceneUiButton("SceneUI/EntryPanel/MultiplayerPanel/MatchButton");
            _backButton = FindSceneUiButton("SceneUI/EntryPanel/MultiplayerPanel/BackButton");

            _singlePlayerButtonText = FindSceneUiText("SceneUI/EntryPanel/ModeSelectPanel/SinglePlayerButton/Label");
            _multiplayerButtonText = FindSceneUiText("SceneUI/EntryPanel/ModeSelectPanel/MultiplayerButton/Label");
            _matchButtonText = FindSceneUiText("SceneUI/EntryPanel/MultiplayerPanel/MatchButton/Label");
            _backButtonText = FindSceneUiText("SceneUI/EntryPanel/MultiplayerPanel/BackButton/Label");

            _accountInputField = FindSceneUiInputField("SceneUI/EntryPanel/MultiplayerPanel/AccountInput");
            _passwordInputField = FindSceneUiInputField("SceneUI/EntryPanel/MultiplayerPanel/PasswordInput");

            ApplySceneUiTheme();

            if (_singlePlayerButton != null)
            {
                _singlePlayerButton.onClick.RemoveAllListeners();
                _singlePlayerButton.onClick.AddListener(OnUiSinglePlayerSelected);
            }

            if (_multiplayerButton != null)
            {
                _multiplayerButton.onClick.RemoveAllListeners();
                _multiplayerButton.onClick.AddListener(OnUiMultiplayerSelected);
            }

            if (_matchButton != null)
            {
                _matchButton.onClick.RemoveAllListeners();
                _matchButton.onClick.AddListener(OnUiConnectRequested);
            }

            if (_backButton != null)
            {
                _backButton.onClick.RemoveAllListeners();
                _backButton.onClick.AddListener(OnUiBackToModeSelect);
            }

            if (_accountInputField != null)
            {
                _accountInputField.onValueChanged.RemoveAllListeners();
                _accountInputField.onValueChanged.AddListener(OnUiAccountChanged);
            }

            if (_passwordInputField != null)
            {
                _passwordInputField.onValueChanged.RemoveAllListeners();
                _passwordInputField.onValueChanged.AddListener(OnUiPasswordChanged);
            }

            SyncSceneUiInputs();
        }

        private void RefreshSceneUi()
        {
            if (_sceneUiRoot == null)
            {
                return;
            }

            var hasSession = HasActiveSession;
            if (_hudPanel != null) _hudPanel.SetActive(hasSession);
            if (_entryPanel != null) _entryPanel.SetActive(!hasSession);
            if (_modeSelectPanel != null) _modeSelectPanel.SetActive(_entryMenuState == EntryMenuState.ModeSelect);
            if (_multiplayerPanel != null) _multiplayerPanel.SetActive(_entryMenuState == EntryMenuState.MultiplayerAuth);

            SetText(_hudTitleText, "ULinkRPC \u70b9\u9635\u7ade\u6280\u573a");
            SetText(_hudStatusText, $"\u72b6\u6001: {_status}");
            SetText(_hudPlayerText, $"\u73a9\u5bb6: {(_localPlayerId.Length > 0 ? _localPlayerId : _account)}   \u79ef\u5206: {GetLocalPlayerScoreText()}   \u80dc\u573a: {_localWinCount}");
            SetText(_hudTickText, $"Tick: {_lastWorldTick}   \u540c\u6b65\u4eba\u6570: {_views.Count}   Buff: {GetLocalPlayerBuffText()}");
            SetText(_hudModeText, _sessionMode == SessionMode.SinglePlayer
                ? "\u6a21\u5f0f: \u672c\u5730\u5355\u673a"
                : $"\u5730\u5740: {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}");
            SetText(_hudHintText, "W/A/S/D \u79fb\u52a8\uff0cSpace \u51b2\u523a\u3002\u4f4d\u7f6e\u4ee5\u6743\u5a01\u72b6\u6001\u4e3a\u51c6\u3002");
            SetText(_hudEventText, $"\u4e8b\u4ef6: {GetCurrentEventMessage()}");
            if (_lastRoundRemainingSeconds > 0)
            {
                var minutes = _lastRoundRemainingSeconds / 60;
                var seconds = _lastRoundRemainingSeconds % 60;
                SetText(_hudCountdownText, $"Time: {minutes:D2}:{seconds:D2}");
            }
            else
            {
                SetText(_hudCountdownText, string.Empty);
            }

            SetText(_entryTitleText, "\u70b9\u9635\u7ade\u6280\u573a");
            SetText(_entryStatusText, _status);
            SetText(_modeSelectDescriptionText, $"\u9009\u62e9\u6a21\u5f0f\u3002\u5355\u673a\u5c06\u7acb\u5373\u5f00\u59cb\uff0c\u5e76\u8865\u8db3 4 \u540d AI\u3002\n{GetMenuLoginStatusText()}");
            SetText(_multiplayerSubtitleText, "\u8054\u673a\u5339\u914d");
            SetText(_accountLabelText, "\u8d26\u53f7");
            SetText(_passwordLabelText, "\u5bc6\u7801");
            SetText(_accountPlaceholderText, "\u8bf7\u8f93\u5165\u8d26\u53f7");
            SetText(_passwordPlaceholderText, "\u8bf7\u8f93\u5165\u5bc6\u7801");
            SetText(_singlePlayerButtonText, "\u5355\u673a");
            SetText(_multiplayerButtonText, "\u8054\u673a");
            SetText(_matchButtonText, _isConnecting ? "\u5339\u914d\u4e2d..." : "\u5339\u914d");
            SetText(_backButtonText, "\u8fd4\u56de");

            if (_singlePlayerButton != null) _singlePlayerButton.interactable = !_isConnecting;
            if (_multiplayerButton != null) _multiplayerButton.interactable = !_isConnecting;
            if (_matchButton != null) _matchButton.interactable = !_isConnecting;
            if (_backButton != null) _backButton.interactable = !_isConnecting;
            if (_accountInputField != null) _accountInputField.interactable = !_isConnecting;
            if (_passwordInputField != null) _passwordInputField.interactable = !_isConnecting;

            SyncSceneUiInputs();
        }

        private void SyncSceneUiInputs()
        {
            if (_accountInputField != null && !_accountInputField.isFocused && _accountInputField.text != _account)
            {
                _accountInputField.SetTextWithoutNotify(_account);
            }

            if (_passwordInputField != null && !_passwordInputField.isFocused && _passwordInputField.text != _password)
            {
                _passwordInputField.SetTextWithoutNotify(_password);
            }
        }

        private GameObject? FindSceneUiObject(string path)
        {
            var target = transform.Find(path);
            return target != null ? target.gameObject : null;
        }

        private void ApplySceneUiFonts()
        {
            if (_sceneUiRoot == null)
            {
                return;
            }

            _tmpFontAsset ??= LoadTmpFontAsset();
            if (_tmpFontAsset == null)
            {
                return;
            }

            foreach (var text in _sceneUiRoot.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.font == null)
                {
                    text.font = _tmpFontAsset;
                }
            }
        }

        private void ApplySceneUiTheme()
        {
            StylePanelImage(_hudPanel, UiPanelBackgroundColor);
            StylePanelImage(_entryPanel, UiPanelBackgroundColor);

            StyleText(_hudTitleText, UiAccentTextColor, 16f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_entryTitleText, UiAccentTextColor, 22f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);

            StyleText(_hudStatusText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudPlayerText, UiSecondaryTextColor, 13f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudTickText, UiSecondaryTextColor, 13f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudModeText, UiSecondaryTextColor, 13f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudHintText, UiMutedTextColor, 12f, true, TextAlignmentOptions.TopLeft, TextOverflowModes.Truncate);
            StyleText(_hudEventText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);
            StyleText(_hudCountdownText, UiAccentTextColor, 14f, false, TextAlignmentOptions.TopLeft, TextOverflowModes.Ellipsis);

            StyleText(_entryStatusText, UiPrimaryTextColor, 14f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_modeSelectDescriptionText, UiSecondaryTextColor, 13f, true, TextAlignmentOptions.Top, TextOverflowModes.Truncate);
            StyleText(_multiplayerSubtitleText, UiPrimaryTextColor, 15f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_accountLabelText, UiSecondaryTextColor, 13f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);
            StyleText(_passwordLabelText, UiSecondaryTextColor, 13f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);
            StyleText(_accountPlaceholderText, UiMutedTextColor, 13f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);
            StyleText(_passwordPlaceholderText, UiMutedTextColor, 13f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);

            StyleButton(_singlePlayerButton);
            StyleButton(_multiplayerButton);
            StyleButton(_matchButton);
            StyleButton(_backButton);
            StyleText(_singlePlayerButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_multiplayerButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_matchButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);
            StyleText(_backButtonText, UiPrimaryTextColor, 13f, false, TextAlignmentOptions.Center, TextOverflowModes.Ellipsis);

            StyleInputField(_accountInputField);
            StyleInputField(_passwordInputField);
        }

        private static void StylePanelImage(GameObject? panel, Color color)
        {
            if (panel == null)
            {
                return;
            }

            if (panel.TryGetComponent<Image>(out var image))
            {
                image.color = color;
            }
        }

        private static void StyleText(
            TMP_Text? text,
            Color color,
            float fontSize,
            bool wrap,
            TextAlignmentOptions alignment,
            TextOverflowModes overflowMode)
        {
            if (text == null)
            {
                return;
            }

            text.color = color;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.enableWordWrapping = wrap;
            text.overflowMode = overflowMode;
            text.richText = false;
        }

        private static void StyleButton(Button? button)
        {
            if (button == null)
            {
                return;
            }

            var colors = button.colors;
            colors.normalColor = new Color(0.2f, 0.29f, 0.38f, 1f);
            colors.highlightedColor = new Color(0.27f, 0.39f, 0.5f, 1f);
            colors.pressedColor = new Color(0.14f, 0.22f, 0.3f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.2f, 0.2f, 0.22f, 0.7f);
            colors.colorMultiplier = 1f;
            button.colors = colors;
        }

        private static void StyleInputField(TMP_InputField? inputField)
        {
            if (inputField == null)
            {
                return;
            }

            if (inputField.targetGraphic is Image inputImage)
            {
                inputImage.color = UiInputBackgroundColor;
            }

            if (inputField.textComponent != null)
            {
                StyleText(inputField.textComponent, UiPrimaryTextColor, 14f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);
            }

            if (inputField.placeholder is TMP_Text placeholderText)
            {
                StyleText(placeholderText, UiMutedTextColor, 13f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);
            }
        }

        private static TMP_FontAsset? LoadTmpFontAsset()
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

            var fallback = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            return fallback ?? TMP_Settings.defaultFontAsset;
        }

        private TMP_Text? FindSceneUiText(string path)
        {
            var target = transform.Find(path);
            return target != null ? target.GetComponent<TMP_Text>() : null;
        }

        private Button? FindSceneUiButton(string path)
        {
            var target = transform.Find(path);
            return target != null ? target.GetComponent<Button>() : null;
        }

        private TMP_InputField? FindSceneUiInputField(string path)
        {
            var target = transform.Find(path);
            return target != null ? target.GetComponent<TMP_InputField>() : null;
        }

        private RectTransform? FindSceneUiRect(string path)
        {
            var target = transform.Find(path);
            return target != null ? target.GetComponent<RectTransform>() : null;
        }

        private static void SetText(TMP_Text? label, string value)
        {
            if (label == null || label.text == value)
            {
                return;
            }

            label.text = value;
        }

        public void OnWorldState(WorldState worldState)
        {
            lock (_callbackLock)
            {
                _pendingWorldState = CloneWorldState(worldState);
            }
        }

        public void OnPlayerDead(PlayerDead deadEvent)
        {
            lock (_callbackLock)
            {
                _pendingDeaths.Enqueue(new PlayerDead
                {
                    PlayerId = deadEvent.PlayerId,
                    Tick = deadEvent.Tick
                });
            }
        }

        public void OnMatchEnd(MatchEnd matchEnd)
        {
            lock (_callbackLock)
            {
                _pendingMatchEnd = new MatchEnd
                {
                    WinnerPlayerId = matchEnd.WinnerPlayerId,
                    Tick = matchEnd.Tick
                };
            }
        }

        private async Task ConnectAsync()
        {
            if (_isConnecting || _isConnected || _sessionMode == SessionMode.SinglePlayer) return;

            _isConnecting = true;
            _status = $"\u6b63\u5728\u8fde\u63a5 {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}";

            try
            {
                var callbacks = new RpcClient.RpcCallbackBindings();
                callbacks.Add(this);

                _connection = Rpc.WebSocketRpcClientFactory.Create(_host, _port, _path, callbacks);
                _connection.Disconnected += OnDisconnected;

                await _connection.ConnectAsync(_cts.Token);

                _playerService = _connection.Api.Shared.Player;
                var reply = await _playerService.LoginAsync(new LoginRequest
                {
                    Account = _account,
                    Password = _password
                });

                if (reply.Code != 0)
                {
                    _status = $"Login failed, code={reply.Code}";
                    await DisposeConnectionAsync();
                    return;
                }

                _localPlayerId = string.IsNullOrWhiteSpace(reply.PlayerId) ? _account : reply.PlayerId;
                _localWinCount = Math.Max(0, reply.WinCount);
                _hasAuthenticatedProfile = true;
                _authenticatedPlayerId = _localPlayerId;
                _isConnected = true;
                _sessionMode = SessionMode.Multiplayer;
                _entryMenuState = EntryMenuState.Hidden;
                _status = $"Connected as {_localPlayerId}";
                Debug.Log($"[DotArena] Connected as {_localPlayerId} -> {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}");
                PushEvent("等待其他玩家加入");
            }
            catch (OperationCanceledException)
            {
                _status = "Connection canceled";
            }
            catch (Exception ex)
            {
                _status = $"Connect failed: {ex.Message}";
                Debug.LogError($"[DotArena] Connect failed: {ex}");
                await DisposeConnectionAsync();
            }
            finally
            {
                _isConnecting = false;
            }
        }

        private void CaptureInputIntent()
        {
            if (ConsumeEditorDashOverride())
            {
                _dashQueued = true;
            }

            if (IsKeyDown(KeyCode.Space))
            {
                _dashQueued = true;
            }
        }

        private void ApplyPendingCallbacks()
        {
            WorldState? worldState = null;
            MatchEnd? matchEnd = null;
            var deadEvents = new List<PlayerDead>();

            lock (_callbackLock)
            {
                if (_pendingWorldState != null)
                {
                    worldState = _pendingWorldState;
                    _pendingWorldState = null;
                }

                while (_pendingDeaths.Count > 0)
                {
                    deadEvents.Add(_pendingDeaths.Dequeue());
                }

                if (_pendingMatchEnd != null)
                {
                    matchEnd = _pendingMatchEnd;
                    _pendingMatchEnd = null;
                }
            }

            if (worldState != null)
            {
                ApplyWorldState(worldState);
            }

            foreach (var deadEvent in deadEvents)
            {
                HandleDeadEvent(deadEvent);
            }

            if (matchEnd != null)
            {
                HandleMatchEnd(matchEnd);
            }
        }

        private void ApplyWorldState(WorldState worldState)
        {
            if (worldState.Tick < _lastWorldTick)
            {
                return;
            }

            var previousArenaHalfExtents = _currentArenaHalfExtents;
            _lastWorldTick = worldState.Tick;
            _lastRoundRemainingSeconds = worldState.RoundRemainingSeconds;
            _currentArenaHalfExtents = new Vector2(worldState.ArenaHalfExtentX, worldState.ArenaHalfExtentY);
            UpdateArenaVisuals();
            if (worldState.Players.Count != _lastLoggedPlayerCount)
            {
                _lastLoggedPlayerCount = worldState.Players.Count;
                Debug.Log($"[DotArena] WorldState tick={worldState.Tick}, players={worldState.Players.Count}, local={_localPlayerId}");
                Debug.Log($"[DotArena] Players => {string.Join(", ", worldState.Players.ConvertAll(static p => $"{p.PlayerId}@({p.X:0.00},{p.Y:0.00}) alive={p.Alive}"))}");
            }

            var activeIds = new HashSet<string>(StringComparer.Ordinal);
            var collectedPickups = new Dictionary<PickupType, string>();
            foreach (var player in worldState.Players)
            {
                activeIds.Add(player.PlayerId);
                var targetPosition = new Vector2(player.X, player.Y);
                var isNewView = false;
                var isNewRenderState = false;

                if (!_views.TryGetValue(player.PlayerId, out var view))
                {
                    view = CreateView(player.PlayerId);
                    _views.Add(player.PlayerId, view);
                    EnsurePlayerOverlay(player.PlayerId);
                    view.SetPosition(targetPosition);
                    isNewView = true;
                    Debug.Log($"[DotArena] Created view for {player.PlayerId}, totalViews={_views.Count}");
                }

                if (!_renderStates.TryGetValue(player.PlayerId, out var renderState))
                {
                    renderState = new PlayerRenderState();
                    _renderStates.Add(player.PlayerId, renderState);
                    isNewRenderState = true;
                }

                var previousState = renderState.State;
                var previousScore = renderState.Score;

                var currentPosition = view.GetPosition();
                if (isNewView || isNewRenderState)
                {
                    currentPosition = targetPosition;
                }

                renderState.PreviousPosition = currentPosition;
                renderState.TargetPosition = targetPosition;
                renderState.ReceivedAt = Time.time;
                renderState.Alive = player.Alive;
                renderState.State = player.State;
                renderState.Score = player.Score;
                var previousSpeedBuff = renderState.SpeedBoostRemainingSeconds;
                var previousKnockbackBuff = renderState.KnockbackBoostRemainingSeconds;
                renderState.SpeedBoostRemainingSeconds = player.SpeedBoostRemainingSeconds;
                renderState.KnockbackBoostRemainingSeconds = player.KnockbackBoostRemainingSeconds;

                view.SetIdentity(player.PlayerId, player.Score);
                if (_playerOverlayViews.TryGetValue(player.PlayerId, out var overlay))
                {
                    overlay.NameText.text = player.PlayerId;
                    overlay.ScoreText.text = $"score: {FormatScore(player.Score)}";
                }
                if (previousState != PlayerLifeState.Stunned && player.State == PlayerLifeState.Stunned)
                {
                    view.TriggerCollisionJelly();
                }
                view.ApplyPresentation(ResolveColor(player.PlayerId), player.State, player.Alive,
                    player.SpeedBoostRemainingSeconds > 0, player.KnockbackBoostRemainingSeconds > 0);
                if (player.PlayerId == _localPlayerId)
                {
                    if (player.Score > previousScore &&
                        !(previousSpeedBuff <= 0 && player.SpeedBoostRemainingSeconds > 0) &&
                        !(previousKnockbackBuff <= 0 && player.KnockbackBoostRemainingSeconds > 0))
                    {
                        PushEvent($"积分 +{player.Score - previousScore}");
                    }

                    if (previousSpeedBuff <= 0 && player.SpeedBoostRemainingSeconds > 0)
                    {
                        PushEvent($"拾取{GetPickupDisplayName(PickupType.SpeedBoost)}: 移速提升 100%，持续 10 秒");
                    }

                    if (previousKnockbackBuff <= 0 && player.KnockbackBoostRemainingSeconds > 0)
                    {
                        PushEvent($"拾取{GetPickupDisplayName(PickupType.KnockbackBoost)}: 撞飞增强 50%，持续 5 秒");
                    }
                }
                if (_views.Count >= 2 && worldState.Players.Exists(static p => p.Alive))
                {
                    _eventMessage = "对局进行中";
                }
            }

            var removedIds = new List<string>();
            foreach (var playerId in _views.Keys)
            {
                if (!activeIds.Contains(playerId))
                {
                    removedIds.Add(playerId);
                }
            }

            foreach (var removedId in removedIds)
            {
                Destroy(_views[removedId].Root);
                _views.Remove(removedId);
                _renderStates.Remove(removedId);
                if (_playerOverlayViews.TryGetValue(removedId, out var overlay))
                {
                    Destroy(overlay.Root);
                    _playerOverlayViews.Remove(removedId);
                }
            }

            if (previousArenaHalfExtents.x >= ArenaHalfWidth - 0.05f &&
                _currentArenaHalfExtents.x < previousArenaHalfExtents.x - 0.05f)
            {
                PushEvent("开始缩圈，注意边界", 2.5f);
            }

            ApplyPickupState(worldState, collectedPickups);
            Debug.Log($"[DotArena] ApplyWorldState complete tick={worldState.Tick}, views={_views.Count}, renders={_renderStates.Count}");
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
                view.ApplyPresentation(ResolveColor(deadEvent.PlayerId), PlayerLifeState.Dead, false, false, false);
            }

            PushEvent(deadEvent.PlayerId == _localPlayerId
                ? "你被淘汰了"
                : $"{deadEvent.PlayerId} 被淘汰");
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

            _ = ReturnToMainMenuAfterMatchAsync(_sessionMode == SessionMode.Multiplayer);
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
            foreach (var pickupView in _pickupViews.Values)
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

            if (!_isConnected || _playerService == null)
            {
                return;
            }

            _ = SendInputAsync(move, dash);
        }

        private async Task SendInputAsync(Vector2 move, bool dash)
        {
            if (_playerService == null)
            {
                return;
            }

            try
            {
                await _playerService.SubmitInput(new InputMessage
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
            if (_sessionMode == SessionMode.SinglePlayer)
            {
                _playerService = null;
                _isConnected = false;
                Debug.LogWarning($"[DotArena] Ignored remote disconnect while running single-player: {ex?.Message ?? "Disconnected"}");
                return;
            }

            ResetSessionPresentation();
            _isConnected = false;
            _playerService = null;
            _localPlayerId = string.Empty;
            _sessionMode = SessionMode.None;
            _localMatch = null;
            _entryMenuState = EntryMenuState.ModeSelect;
            _hasAuthenticatedProfile = false;
            _authenticatedPlayerId = string.Empty;
            _localWinCount = 0;
            _status = ex == null ? "Disconnected" : $"Disconnected: {ex.Message}";
            Debug.LogWarning($"[DotArena] {_status}");
        }

        private async Task ReturnToMainMenuAfterMatchAsync(bool preserveLoginState)
        {
            var authenticatedPlayerId = _authenticatedPlayerId;
            var localWinCount = _localWinCount;

            if (_sessionMode == SessionMode.Multiplayer && _connection != null)
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

            _entryMenuState = EntryMenuState.ModeSelect;
            _status = "选择模式";
            _eventMessage = "请选择单机或联机";

            if (preserveLoginState)
            {
                _hasAuthenticatedProfile = true;
                _authenticatedPlayerId = authenticatedPlayerId;
                _localWinCount = localWinCount;
            }
            else
            {
                _hasAuthenticatedProfile = false;
                _authenticatedPlayerId = string.Empty;
                _localWinCount = 0;
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
            if (_connection == null) return;

            var connection = _connection;
            var playerService = _playerService;
            var shouldLogout = _isConnected && playerService != null;
            _connection = null;
            connection.Disconnected -= OnDisconnected;

            try
            {
                if (shouldLogout)
                {
                    await playerService!.LogoutAsync().ConfigureAwait(false);
                }
            }
            catch
            {
            }

            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                _playerService = null;
                _isConnected = false;
                if (clearSessionState)
                {
                    _sessionMode = SessionMode.None;
                    _localPlayerId = string.Empty;
                    _localMatch = null;
                }
            }
        }

        private void ProcessMenuRequests()
        {
            if (HasActiveSession || _isConnecting)
            {
                return;
            }

            if (_entryMenuState == EntryMenuState.ModeSelect && IsSinglePlayerHotkeyPressed())
            {
                _singlePlayerStartRequested = true;
            }

            if (!_singlePlayerStartRequested)
            {
                return;
            }

            _singlePlayerStartRequested = false;
            BeginSinglePlayerMatch();
        }

        private static bool IsSinglePlayerHotkeyPressed()
        {
            return IsKeyDown(KeyCode.Return) ||
                   IsKeyDown(KeyCode.KeypadEnter) ||
                   IsKeyDown(KeyCode.W) ||
                   IsKeyDown(KeyCode.A) ||
                   IsKeyDown(KeyCode.S) ||
                   IsKeyDown(KeyCode.D) ||
                   IsKeyDown(KeyCode.UpArrow) ||
                   IsKeyDown(KeyCode.LeftArrow) ||
                   IsKeyDown(KeyCode.DownArrow) ||
                   IsKeyDown(KeyCode.RightArrow) ||
                   IsKeyDown(KeyCode.Space);
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
            ResetSessionPresentation();
            _ = DisposeConnectionAsync(clearSessionState: false);
            _isConnecting = false;
            _isConnected = false;
            _playerService = null;
            _sessionMode = SessionMode.SinglePlayer;
            _localPlayerId = "Player";
            _localMatch = new ArenaSimulation(new ArenaSimulationOptions
            {
                Arena = GameplayConfig,
                RespawnDelaySeconds = 5f
            });
            _localMatch.UpsertPlayer(new ArenaPlayerRegistration
            {
                PlayerId = _localPlayerId,
                Score = 0
            });
            _localWinCount = 0;
            _entryMenuState = EntryMenuState.Hidden;
            _status = "单机匹配中...";
            _eventMessage = "正在进入本地单机模式";
            _lastWorldTick = -1;
            _inputTick = 0;
            _singlePlayerTickAccumulator = 0f;
            Debug.Log("[DotArena] BeginSinglePlayerMatch");
            ApplyWorldState(_localMatch.CreateWorldState());
            _status = $"单机模式: {_localPlayerId}";
        }

        private void BuildArena()
        {
            _pixelSprite = CreatePixelSprite();
            _playerSprite = CreateCircleSprite();
            _playerOutlineSprite = CreateCircleOutlineSprite();
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

            foreach (var pickupView in _pickupViews.Values)
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
            _pendingWorldState = null;
            _pendingDeaths.Clear();
            _pendingMatchEnd = null;
            _localWinCount = _sessionMode == SessionMode.Multiplayer ? _localWinCount : 0;
            _lastWorldTick = -1;
            _lastLoggedPlayerCount = -1;
            _dashQueued = false;
            _nextInputAt = 0f;
            _singlePlayerTickAccumulator = 0f;
            _currentArenaHalfExtents = GameplayConfig.ArenaHalfExtents;
            UpdateArenaVisuals();
        }

        private void ApplyPickupState(WorldState worldState, Dictionary<PickupType, string> collectedPickups)
        {
            var pickupScale = GameplayConfig.PickupCollisionRadius * 2f;
            var activeTypes = new HashSet<PickupType>();
            foreach (var pickup in worldState.Pickups)
            {
                activeTypes.Add(pickup.Type);
                if (!_pickupViews.TryGetValue(pickup.Type, out var view))
                {
                    view = CreatePickupView(pickup.Type);
                    _pickupViews.Add(pickup.Type, view);
                }

                view.ShowAt(new Vector3(pickup.X, pickup.Y, 0f), pickupScale);
            }

            foreach (var entry in _pickupViews)
            {
                if (activeTypes.Contains(entry.Key))
                {
                    continue;
                }

                if (entry.Value.IsAbsorbing)
                {
                    continue;
                }

                if (TryGetPickupAbsorbTarget(entry.Key, collectedPickups, entry.Value.Root.transform.position, out var absorbTarget))
                {
                    entry.Value.StartAbsorb(absorbTarget, Time.time, pickupScale);
                    continue;
                }

                entry.Value.Root.SetActive(false);
            }
        }

        private bool TryGetPickupAbsorbTarget(PickupType pickupType, Dictionary<PickupType, string> collectedPickups, Vector3 pickupPosition, out Vector3 targetPosition)
        {
            if (collectedPickups.TryGetValue(pickupType, out var collectorId) &&
                _views.TryGetValue(collectorId, out var collectorView))
            {
                var collectorPosition = collectorView.GetPosition();
                targetPosition = new Vector3(collectorPosition.x, collectorPosition.y, 0f);
                return true;
            }

            var bestDistance = float.MaxValue;
            targetPosition = default;
            foreach (var entry in _views)
            {
                if (!_renderStates.TryGetValue(entry.Key, out var renderState) || !renderState.Alive)
                {
                    continue;
                }

                var candidate = entry.Value.GetPosition();
                var distance = (candidate - new Vector2(pickupPosition.x, pickupPosition.y)).sqrMagnitude;
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                targetPosition = new Vector3(candidate.x, candidate.y, 0f);
            }

            return bestDistance < float.MaxValue;
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

            if (IsKeyPressed(KeyCode.A)) x -= 1f;
            if (IsKeyPressed(KeyCode.D)) x += 1f;
            if (IsKeyPressed(KeyCode.S)) y -= 1f;
            if (IsKeyPressed(KeyCode.W)) y += 1f;

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

        private static bool IsKeyPressed(KeyCode keyCode)
        {
            if (Input.GetKey(keyCode))
            {
                return true;
            }

#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            return keyCode switch
            {
                KeyCode.W => keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed,
                KeyCode.A => keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed,
                KeyCode.S => keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed,
                KeyCode.D => keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed,
                KeyCode.Space => keyboard.spaceKey.isPressed,
                KeyCode.Return => keyboard.enterKey.isPressed,
                KeyCode.KeypadEnter => keyboard.numpadEnterKey.isPressed,
                KeyCode.UpArrow => keyboard.upArrowKey.isPressed,
                KeyCode.LeftArrow => keyboard.leftArrowKey.isPressed,
                KeyCode.DownArrow => keyboard.downArrowKey.isPressed,
                KeyCode.RightArrow => keyboard.rightArrowKey.isPressed,
                _ => false
            };
#else
            return false;
#endif
        }

        private static bool IsKeyDown(KeyCode keyCode)
        {
            if (Input.GetKeyDown(keyCode))
            {
                return true;
            }

#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            return keyCode switch
            {
                KeyCode.W => keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame,
                KeyCode.A => keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame,
                KeyCode.S => keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame,
                KeyCode.D => keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame,
                KeyCode.Space => keyboard.spaceKey.wasPressedThisFrame,
                KeyCode.Return => keyboard.enterKey.wasPressedThisFrame,
                KeyCode.KeypadEnter => keyboard.numpadEnterKey.wasPressedThisFrame,
                KeyCode.UpArrow => keyboard.upArrowKey.wasPressedThisFrame,
                KeyCode.LeftArrow => keyboard.leftArrowKey.wasPressedThisFrame,
                KeyCode.DownArrow => keyboard.downArrowKey.wasPressedThisFrame,
                KeyCode.RightArrow => keyboard.rightArrowKey.wasPressedThisFrame,
                _ => false
            };
#else
            return false;
#endif
        }

        private DotView CreateView(string playerId)
        {
            var viewRoot = new GameObject(playerId);
            viewRoot.transform.SetParent(transform, false);
            Debug.Log($"[DotArena] CreateView root={viewRoot.name} parent={viewRoot.transform.parent?.name}");

            var renderer = viewRoot.AddComponent<SpriteRenderer>();
            renderer.sprite = _playerSprite;
            renderer.color = ResolveColor(playerId);
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
            ConfigureTextRenderer(nameText.GetComponent<MeshRenderer>(), PlayerTextSortingOrder);

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
            ConfigureTextRenderer(scoreText.GetComponent<MeshRenderer>(), PlayerTextSortingOrder);

            var view = new DotView(viewRoot, renderer, outlineRenderer, nameText, scoreText);
            view.SetIdentity(playerId, 0);
            view.ApplyPresentation(ResolveColor(playerId), PlayerLifeState.Idle, true, false, false);
            return view;
        }

        private Color ResolveColor(string playerId)
        {
            var index = GetStableColorIndex(playerId);
            return RemotePalette[index];
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

            var pickupColor = GetPickupColor(pickupType);
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
            labelText.text = GetPickupDisplayName(pickupType);
            labelText.fontSize = 64;
            labelText.characterSize = 0.12f;
            labelText.anchor = TextAnchor.MiddleCenter;
            labelText.alignment = TextAlignment.Center;
            labelText.fontStyle = FontStyle.Bold;
            labelText.color = GetPickupLabelColor(pickupType);
            ConfigureTextRenderer(labelText.GetComponent<MeshRenderer>(), PickupLabelSortingOrder);

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

        private static void ConfigureTextRenderer(MeshRenderer? renderer, int sortingOrder)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private string GetLocalPlayerScoreText()
        {
            if (_localPlayerId.Length == 0)
            {
                return "0";
            }

            return _renderStates.TryGetValue(_localPlayerId, out var renderState)
                ? FormatScore(renderState.Score)
                : "0";
        }

        private string GetLocalPlayerBuffText()
        {
            if (_localPlayerId.Length == 0 || !_renderStates.TryGetValue(_localPlayerId, out var renderState))
            {
                return "无";
            }

            var parts = new List<string>(2);
            if (renderState.SpeedBoostRemainingSeconds > 0)
            {
                parts.Add($"{GetPickupDisplayName(PickupType.SpeedBoost)} {renderState.SpeedBoostRemainingSeconds}s");
            }

            if (renderState.KnockbackBoostRemainingSeconds > 0)
            {
                parts.Add($"{GetPickupDisplayName(PickupType.KnockbackBoost)} {renderState.KnockbackBoostRemainingSeconds}s");
            }

            return parts.Count == 0 ? "无" : string.Join(" / ", parts);
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

        private static WorldState CloneWorldState(WorldState source)
        {
            var clone = new WorldState
            {
                Tick = source.Tick,
                RespawnDelaySeconds = source.RespawnDelaySeconds,
                ArenaHalfExtentX = source.ArenaHalfExtentX,
                ArenaHalfExtentY = source.ArenaHalfExtentY
            };

            foreach (var player in source.Players)
            {
                clone.Players.Add(new PlayerState
                {
                    PlayerId = player.PlayerId,
                    X = player.X,
                    Y = player.Y,
                    Vx = player.Vx,
                    Vy = player.Vy,
                    State = player.State,
                    Alive = player.Alive,
                    RespawnRemainingSeconds = player.RespawnRemainingSeconds,
                    Score = player.Score,
                    SpeedBoostRemainingSeconds = player.SpeedBoostRemainingSeconds,
                    KnockbackBoostRemainingSeconds = player.KnockbackBoostRemainingSeconds
                });
            }

            foreach (var pickup in source.Pickups)
            {
                clone.Pickups.Add(new PickupState
                {
                    Type = pickup.Type,
                    X = pickup.X,
                    Y = pickup.Y
                });
            }

            return clone;
        }

        private static Sprite CreatePixelSprite()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            texture.SetPixel(0, 0, Color.white);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        }

        private static Sprite CreateCircleSprite()
        {
            const int textureSize = 128;
            var texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var center = (textureSize - 1) * 0.5f;
            var radius = center - 0.5f;

            for (var y = 0; y < textureSize; y++)
            {
                for (var x = 0; x < textureSize; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var distance = Mathf.Sqrt((dx * dx) + (dy * dy));
                    var alpha = Mathf.Clamp01(radius - distance);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();

            return Sprite.Create(
                texture,
                new Rect(0f, 0f, textureSize, textureSize),
                new Vector2(0.5f, 0.5f),
                textureSize);
        }

        private static Sprite CreateCircleOutlineSprite()
        {
            const int textureSize = 128;
            const float outlineWidthPixels = 2.25f;

            var texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var center = (textureSize - 1) * 0.5f;
            var radius = center - 0.5f;
            var innerRadius = radius - outlineWidthPixels;

            for (var y = 0; y < textureSize; y++)
            {
                for (var x = 0; x < textureSize; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var distance = Mathf.Sqrt((dx * dx) + (dy * dy));
                    var outerAlpha = Mathf.Clamp01(radius - distance);
                    var innerAlpha = Mathf.Clamp01(innerRadius - distance);
                    var alpha = Mathf.Clamp01(outerAlpha - innerAlpha);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();

            return Sprite.Create(
                texture,
                new Rect(0f, 0f, textureSize, textureSize),
                new Vector2(0.5f, 0.5f),
                textureSize);
        }

        private static int GetStableColorIndex(string playerId)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var ch in playerId)
                {
                    hash ^= ch;
                    hash *= 16777619u;
                }

                return (int)(hash % (uint)RemotePalette.Length);
            }
        }

        private static string FormatScore(int score)
        {
            return score.ToString();
        }

        private static Color GetPickupColor(PickupType pickupType)
        {
            return pickupType switch
            {
                PickupType.ScorePoint => ScorePickupColor,
                PickupType.SpeedBoost => SpeedPickupColor,
                PickupType.KnockbackBoost => KnockbackPickupColor,
                _ => Color.white
            };
        }

        private static string GetPickupDisplayName(PickupType pickupType)
        {
            return pickupType switch
            {
                PickupType.ScorePoint => "积分点",
                PickupType.SpeedBoost => "加速",
                PickupType.KnockbackBoost => "冲击力",
                _ => "Buff"
            };
        }

        private static float GetPlayerJellyPhase(string playerId)
        {
            unchecked
            {
                var hash = 17;
                foreach (var ch in playerId)
                {
                    hash = (hash * 31) + ch;
                }

                return Mathf.Abs(hash % 100) / 20f;
            }
        }

        private static Color GetPickupLabelColor(PickupType pickupType)
        {
            var color = GetPickupColor(pickupType);
            var luminance = (color.r * 0.299f) + (color.g * 0.587f) + (color.b * 0.114f);
            return luminance >= 0.6f ? Color.black : Color.white;
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
            if (_overlayLayer == null || _playerOverlayViews.ContainsKey(playerId))
            {
                return;
            }

            var root = new GameObject($"{playerId}Overlay", typeof(RectTransform));
            root.transform.SetParent(_overlayLayer, false);

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
            nameText.font = _tmpFontAsset ??= LoadTmpFontAsset();
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
            scoreText.font = _tmpFontAsset ??= LoadTmpFontAsset();
            scoreText.fontSize = 14;
            scoreText.fontStyle = FontStyles.Bold;
            scoreText.alignment = TextAlignmentOptions.Center;
            scoreText.enableWordWrapping = false;
            scoreText.overflowMode = TextOverflowModes.Ellipsis;
            scoreText.color = UiAccentTextColor;

            _playerOverlayViews.Add(playerId, new PlayerOverlayView(root, rootRect, nameText, scoreText));
        }

        private void UpdatePlayerOverlayViews()
        {
            if (_overlayLayer == null)
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

        private sealed class PlayerRenderState
        {
            public Vector2 PreviousPosition { get; set; }
            public Vector2 TargetPosition { get; set; }
            public float ReceivedAt { get; set; }
            public PlayerLifeState State { get; set; }
            public bool Alive { get; set; }
            public int Score { get; set; }
            public int SpeedBoostRemainingSeconds { get; set; }
            public int KnockbackBoostRemainingSeconds { get; set; }
        }

        private sealed class PlayerOverlayView
        {
            public PlayerOverlayView(GameObject root, RectTransform rootRect, TextMeshProUGUI nameText, TextMeshProUGUI scoreText)
            {
                Root = root;
                RootRect = rootRect;
                NameText = nameText;
                ScoreText = scoreText;
            }

            public GameObject Root { get; }
            public RectTransform RootRect { get; }
            public TextMeshProUGUI NameText { get; }
            public TextMeshProUGUI ScoreText { get; }
        }

        private sealed class DotView
        {
            private readonly SpriteRenderer _renderer;
            private readonly SpriteRenderer _outlineRenderer;
            private readonly TextMesh _nameText;
            private readonly TextMesh _scoreText;
            private float _impactUntil;

            public DotView(GameObject root, SpriteRenderer renderer, SpriteRenderer outlineRenderer, TextMesh nameText, TextMesh scoreText)
            {
                Root = root;
                _renderer = renderer;
                _outlineRenderer = outlineRenderer;
                _nameText = nameText;
                _scoreText = scoreText;
            }

            public GameObject Root { get; }

            public Vector2 GetPosition()
            {
                var position = Root.transform.position;
                return new Vector2(position.x, position.y);
            }

            public void SetPosition(Vector2 position)
            {
                Root.transform.position = new Vector3(position.x, position.y, 0f);
            }

            public void TriggerCollisionJelly()
            {
                _impactUntil = Time.time + 0.28f;
            }

            public void UpdateJelly(float time)
            {
                var remaining = Mathf.Clamp01((_impactUntil - time) / 0.28f);
                var pulse = remaining * remaining;
                UpdateMaterial(_renderer, time, pulse, 1f);
                UpdateMaterial(_outlineRenderer, time, pulse, 1.2f);
            }

            public void SetIdentity(string playerId, int score)
            {
                _nameText.text = playerId;
                _scoreText.text = FormatScore(score);
            }

            public void ApplyPresentation(Color baseColor, PlayerLifeState state, bool alive, bool hasSpeedBoost, bool hasKnockbackBoost)
            {
                var color = baseColor;
                if (!alive)
                {
                    color = new Color(baseColor.r * 0.35f, baseColor.g * 0.35f, baseColor.b * 0.35f, 0.55f);
                }
                else if (state == PlayerLifeState.Dash)
                {
                    color = Color.Lerp(baseColor, Color.white, 0.3f);
                }
                else if (state == PlayerLifeState.Stunned)
                {
                    color = Color.Lerp(baseColor, new Color(1f, 0.9f, 0.45f, 1f), 0.45f);
                }

                if (hasSpeedBoost)
                {
                    color = Color.Lerp(color, SpeedPickupColor, 0.28f);
                }

                if (hasKnockbackBoost)
                {
                    color = Color.Lerp(color, KnockbackPickupColor, 0.33f);
                }

                _renderer.color = color;
                _outlineRenderer.color = alive
                    ? PlayerOutlineColor
                    : new Color(PlayerOutlineColor.r, PlayerOutlineColor.g, PlayerOutlineColor.b, 0.45f);
                var scaleBoost = hasSpeedBoost || hasKnockbackBoost ? 1.08f : 1f;
                Root.transform.localScale = new Vector3(PlayerVisualDiameter * scaleBoost, PlayerVisualDiameter * scaleBoost, 1f);
                _outlineRenderer.transform.localScale = new Vector3(1.14f, 1.14f, 1f);
            }

            private static void UpdateMaterial(SpriteRenderer renderer, float time, float impactPulse, float wobbleScale)
            {
                var material = renderer.material;
                if (material == null || !material.HasProperty("_WobbleAmount"))
                {
                    return;
                }

                var wobble = (0.18f + (impactPulse * 0.62f)) * wobbleScale;
                var speed = 4.8f + (impactPulse * 9.5f);
                material.SetFloat("_WobbleAmount", wobble);
                material.SetFloat("_WobbleSpeed", speed + (Mathf.Sin(time * 1.3f) * 0.15f));
            }
        }

        private sealed class PickupView
        {
            private readonly Color _baseGlowColor;
            private readonly Color _baseLabelColor;
            private Vector3 _absorbStartPosition;
            private Vector3 _absorbTargetPosition;
            private float _absorbStartedAt;
            private bool _isAbsorbing;

            public PickupView(GameObject root, SpriteRenderer renderer, SpriteRenderer glowRenderer, TextMesh labelText)
            {
                Root = root;
                Renderer = renderer;
                GlowRenderer = glowRenderer;
                LabelText = labelText;
                _baseGlowColor = glowRenderer.color;
                _baseLabelColor = labelText.color;
            }

            public GameObject Root { get; }
            public SpriteRenderer Renderer { get; }
            public SpriteRenderer GlowRenderer { get; }
            public TextMesh LabelText { get; }
            public bool IsAbsorbing => _isAbsorbing;

            public void ShowAt(Vector3 position, float scale)
            {
                _isAbsorbing = false;
                Root.SetActive(true);
                Root.transform.position = position;
                Root.transform.localScale = new Vector3(scale, scale, 1f);
                GlowRenderer.transform.localScale = Vector3.one * 1.24f;

                var glowColor = _baseGlowColor;
                glowColor.a = _baseGlowColor.a;
                GlowRenderer.color = glowColor;

                var labelColor = _baseLabelColor;
                labelColor.a = _baseLabelColor.a;
                LabelText.color = labelColor;

                var material = Renderer.material;
                if (material != null && material.HasProperty("_Dissolve"))
                {
                    material.SetFloat("_Dissolve", 0f);
                }
            }

            public void StartAbsorb(Vector3 targetPosition, float time, float scale)
            {
                if (!Root.activeSelf)
                {
                    return;
                }

                _isAbsorbing = true;
                _absorbStartedAt = time;
                _absorbStartPosition = Root.transform.position;
                _absorbTargetPosition = targetPosition;
                Root.transform.localScale = new Vector3(scale, scale, 1f);

                var material = Renderer.material;
                if (material != null && material.HasProperty("_Dissolve"))
                {
                    material.SetFloat("_Dissolve", 0f);
                }
            }

            public void UpdateVisual(float time, float pulseScale, float absorbDurationSeconds)
            {
                if (!_isAbsorbing)
                {
                    if (Root.activeSelf)
                    {
                        Root.transform.localScale = new Vector3(pulseScale, pulseScale, 1f);
                    }
                    return;
                }

                var progress = Mathf.Clamp01((time - _absorbStartedAt) / absorbDurationSeconds);
                var eased = 1f - Mathf.Pow(1f - progress, 3f);
                Root.transform.position = Vector3.Lerp(_absorbStartPosition, _absorbTargetPosition, eased);
                var scale = Mathf.Lerp(pulseScale, pulseScale * 0.24f, eased);
                Root.transform.localScale = new Vector3(scale, scale, 1f);
                GlowRenderer.transform.localScale = Vector3.one * Mathf.Lerp(1.24f, 0.42f, eased);

                var material = Renderer.material;
                if (material != null && material.HasProperty("_Dissolve"))
                {
                    material.SetFloat("_Dissolve", Mathf.SmoothStep(0f, 1f, progress));
                }

                var glowColor = _baseGlowColor;
                glowColor.a = Mathf.Lerp(_baseGlowColor.a, 0f, eased);
                GlowRenderer.color = glowColor;

                var labelColor = _baseLabelColor;
                labelColor.a = Mathf.Lerp(_baseLabelColor.a, 0f, Mathf.Clamp01(progress * 1.25f));
                LabelText.color = labelColor;

                if (progress >= 1f)
                {
                    _isAbsorbing = false;
                    Root.SetActive(false);
                }
            }
        }

        private enum EntryMenuState
        {
            Hidden = 0,
            ModeSelect = 1,
            MultiplayerAuth = 2
        }

        private enum SessionMode
        {
            None = 0,
            SinglePlayer = 1,
            Multiplayer = 2
        }
    }
}
