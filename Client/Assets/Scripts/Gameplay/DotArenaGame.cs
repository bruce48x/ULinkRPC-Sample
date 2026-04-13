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
        private readonly Dictionary<PickupType, PickupView> _pickupViews = new();
        private readonly Dictionary<string, PlayerOverlayView> _playerOverlayViews = new(StringComparer.Ordinal);

        private DotArenaNetworkSession? _networkSession;
        private DotArenaWorldSynchronizer? _worldSynchronizer;
        private ArenaSimulation? _localMatch;
        private string _localPlayerId = string.Empty;
        private bool _singlePlayerStartRequested;
        private EntryMenuState _entryMenuState = EntryMenuState.ModeSelect;
        private SessionMode _sessionMode = SessionMode.None;
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
        private string _lastLoggedInputVector = string.Empty;
        private int _lastRoundRemainingSeconds;
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
            message => _eventMessage = message);

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
            if (_sceneUiPresenter.HasSceneUi)
            {
                return;
            }

            if (!HasActiveSession)
            {
                DrawEntryMenu();
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

        public void OnUiConnectRequested()
        {
            if (IsConnecting)
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
            _sceneUiPresenter.Bind(
                transform,
                OnUiSinglePlayerSelected,
                OnUiMultiplayerSelected,
                OnUiConnectRequested,
                OnUiBackToModeSelect,
                OnUiAccountChanged,
                OnUiPasswordChanged);
        }

        private void RefreshSceneUi()
        {
            _sceneUiPresenter.Refresh(new DotArenaSceneUiSnapshot
            {
                HasSession = HasActiveSession,
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
                Host = _host,
                Port = _port,
                Path = _path,
                CurrentEventMessage = GetCurrentEventMessage(),
                LastRoundRemainingSeconds = _lastRoundRemainingSeconds,
                MenuLoginStatusText = GetMenuLoginStatusText(),
                IsConnecting = IsConnecting
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

        private async Task ConnectAsync()
        {
            if (IsConnecting || IsConnected || _sessionMode == SessionMode.SinglePlayer) return;

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
                _localWinCount = Math.Max(0, reply.WinCount);
                _hasAuthenticatedProfile = true;
                _authenticatedPlayerId = _localPlayerId;
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
        }

        private void CaptureInputIntent()
        {
            if (ConsumeEditorDashOverride())
            {
                _dashQueued = true;
            }

            if (DotArenaInputUtility.IsKeyDown(KeyCode.Space))
            {
                _dashQueued = true;
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
                view.ApplyPresentation(DotArenaPresentation.ResolvePlayerColor(deadEvent.PlayerId), PlayerLifeState.Dead, false, false, false);
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
            if (_sessionMode == SessionMode.SinglePlayer)
            {
                Debug.LogWarning($"[DotArena] Ignored remote disconnect while running single-player: {ex?.Message ?? "Disconnected"}");
                return;
            }

            ResetSessionPresentation();
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
            await NetworkSession.DisposeAsync().ConfigureAwait(false);
            if (clearSessionState)
            {
                _sessionMode = SessionMode.None;
                _localPlayerId = string.Empty;
                _localMatch = null;
            }
        }

        private void ProcessMenuRequests()
        {
            if (HasActiveSession || IsConnecting)
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
            renderer.color = DotArenaPresentation.ResolvePlayerColor(playerId);
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
            view.ApplyPresentation(DotArenaPresentation.ResolvePlayerColor(playerId), PlayerLifeState.Idle, true, false, false);
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
                ? DotArenaPresentation.FormatScore(renderState.Score)
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
                parts.Add($"{DotArenaPresentation.GetPickupDisplayName(PickupType.SpeedBoost)} {renderState.SpeedBoostRemainingSeconds}s");
            }

            if (renderState.KnockbackBoostRemainingSeconds > 0)
            {
                parts.Add($"{DotArenaPresentation.GetPickupDisplayName(PickupType.KnockbackBoost)} {renderState.KnockbackBoostRemainingSeconds}s");
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
