#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Shared.Gameplay;
using Shared.Interfaces;
using ULinkRPC.Client;
using UnityEngine;
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
        private const float PlayerNameOffsetY = 0.1f;
        private const float PlayerScoreOffsetY = -0.14f;
        private const float PickupPulseAmplitude = 0.08f;
        private const float PickupPulseFrequency = 3.2f;
        private const string JellyShaderName = "SampleClient/BuffPickupJelly";
        private const int PlayerSortingOrder = 20;
        private const int PlayerOutlineSortingOrder = 25;
        private const int PlayerTextSortingOrder = 30;
        private const int PickupSortingOrder = 12;
        private const int PickupLabelSortingOrder = 14;
        private const float PlayerTextDepth = -0.2f;
        private const float PlayerNameScale = 0.12f;
        private const float PlayerScoreScale = 0.1f;
        private const float PickupLabelScale = 0.45f;
        private const float PlayerTextCharacterSize = 0.08f;
        private const float InputSendIntervalSeconds = 0.05f;
        private const float InterpolationDurationSeconds = 0.1f;

        private static readonly Color BackgroundColor = new(0.02f, 0.03f, 0.05f, 1f);
        private static readonly Color BoardColor = new(0.08f, 0.1f, 0.14f, 1f);
        private static readonly Color GridColor = new(0.75f, 0.86f, 0.94f, 0.1f);
        private static readonly Color BorderColor = new(1f, 0.84f, 0.31f, 0.24f);
        private static readonly Color DangerColor = new(1f, 0.24f, 0.24f, 0.08f);
        private static readonly Color PlayerOutlineColor = new(1f, 1f, 1f, 0.92f);
        private static readonly Color SpeedPickupColor = new(1f, 0.86f, 0.22f, 0.95f);
        private static readonly Color KnockbackPickupColor = new(1f, 0.22f, 0.22f, 0.95f);
        private static readonly ArenaConfig GameplayConfig = ArenaConfig.CreateDefault();

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

        private WorldState? _pendingWorldState;
        private readonly Queue<PlayerDead> _pendingDeaths = new();
        private MatchEnd? _pendingMatchEnd;

        private Sprite _pixelSprite = null!;
        private Sprite _playerSprite = null!;
        private Sprite _playerOutlineSprite = null!;
        private Shader? _jellyShader;
        private string _status = "Connecting...";
        private string _eventMessage = "等待玩家加入";
        private float _eventMessageUntil;
        private int _lastWorldTick = -1;
        private int _lastLoggedPlayerCount = -1;
        private bool _shutdownStarted;
        private string _lastLoggedInputVector = string.Empty;
#if UNITY_EDITOR
        private Vector2 _editorMoveOverride;
        private bool _editorDashOverride;
        private bool _hasEditorInputOverride;
#endif

        private bool HasActiveSession => _sessionMode == SessionMode.SinglePlayer || _isConnected;

        private void Start()
        {
            ApplyLaunchOverrides();
            ConfigureWindow();
            InitializeConnectionMode();
            ConfigureCamera();
            BuildArena();
        }

        private void Update()
        {
            CaptureInputIntent();
            ProcessMenuRequests();
            HandleInput();
            TickLocalMatch();
            ApplyPendingCallbacks();
            UpdateViews();
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
            GUI.Label(new Rect(contentRect.x, contentRect.y + 24f, contentRect.width, 18f), $"状态: {_status}", bodyStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 44f, contentRect.width, 18f),
                $"玩家: {(_localPlayerId.Length > 0 ? _localPlayerId : _account)}   积分: {GetLocalPlayerScoreText()}", bodyStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 64f, contentRect.width, 18f),
                $"服务端 Tick: {_lastWorldTick}   同步人数: {_views.Count}   Buff: {GetLocalPlayerBuffText()}", bodyStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 84f, contentRect.width, 18f),
                _sessionMode == SessionMode.SinglePlayer
                    ? "模式: 本地单机"
                    : $"地址: {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}", bodyStyle);

            GUI.Label(new Rect(contentRect.x, contentRect.y + 104f, contentRect.width, 18f),
                "W/A/S/D 移动, Space 冲刺。客户端只发输入，位置以服务端广播为准。", bodyStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 124f, contentRect.width, 18f),
                $"事件: {GetCurrentEventMessage()}", bodyStyle);

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
                "选择一种游玩方式。单机会直接进入并自动补足 AI 到 4 人。", bodyStyle);

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
            _status = $"Connecting {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}";

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

            _lastWorldTick = worldState.Tick;
            if (worldState.Players.Count != _lastLoggedPlayerCount)
            {
                _lastLoggedPlayerCount = worldState.Players.Count;
                Debug.Log($"[DotArena] WorldState tick={worldState.Tick}, players={worldState.Players.Count}, local={_localPlayerId}");
                Debug.Log($"[DotArena] Players => {string.Join(", ", worldState.Players.ConvertAll(static p => $"{p.PlayerId}@({p.X:0.00},{p.Y:0.00}) alive={p.Alive}"))}");
            }

            var activeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var player in worldState.Players)
            {
                activeIds.Add(player.PlayerId);
                var targetPosition = new Vector2(player.X, player.Y);
                var isNewView = false;
                var isNewRenderState = false;
                var shouldSnapToTarget = _sessionMode == SessionMode.SinglePlayer;

                if (!_views.TryGetValue(player.PlayerId, out var view))
                {
                    view = CreateView(player.PlayerId);
                    _views.Add(player.PlayerId, view);
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

                var currentPosition = view.GetPosition();
                if (isNewView || isNewRenderState)
                {
                    currentPosition = targetPosition;
                }

                renderState.PreviousPosition = shouldSnapToTarget ? targetPosition : currentPosition;
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
                if (previousState != PlayerLifeState.Stunned && player.State == PlayerLifeState.Stunned)
                {
                    view.TriggerCollisionJelly();
                }
                view.ApplyPresentation(ResolveColor(player.PlayerId), player.State, player.Alive,
                    player.SpeedBoostRemainingSeconds > 0, player.KnockbackBoostRemainingSeconds > 0);
                if (player.PlayerId == _localPlayerId)
                {
                    if (previousSpeedBuff <= 0 && player.SpeedBoostRemainingSeconds > 0)
                    {
                        PushEvent($"拾取{GetPickupDisplayName(PickupType.SpeedBoost)}: 移速提升 50%，持续 10 秒");
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
            }

            ApplyPickupState(worldState);
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
            PushEvent(matchEnd.WinnerPlayerId == _localPlayerId
                ? "本局胜利"
                : $"胜者: {matchEnd.WinnerPlayerId}");
        }

        private void TickLocalMatch()
        {
            if (_sessionMode != SessionMode.SinglePlayer || _localMatch == null)
            {
                return;
            }

            var step = _localMatch.Tick(Time.deltaTime);
            ApplyWorldState(step.WorldState);

            foreach (var deadEvent in step.Deaths)
            {
                HandleDeadEvent(deadEvent);
            }

            if (step.MatchEnd != null)
            {
                HandleMatchEnd(step.MatchEnd);
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

                Vector2 position;
                if (_sessionMode == SessionMode.SinglePlayer)
                {
                    position = renderState.TargetPosition;
                }
                else
                {
                    var elapsed = Mathf.Clamp01((Time.time - renderState.ReceivedAt) / InterpolationDurationSeconds);
                    var smoothed = elapsed * elapsed * (3f - (2f * elapsed));
                    position = Vector2.Lerp(renderState.PreviousPosition, renderState.TargetPosition, smoothed);
                }

                entry.Value.SetPosition(position);
            }

            var pickupScale = GameplayConfig.PickupCollisionRadius * 2f;
            foreach (var pickupView in _pickupViews.Values)
            {
                var pulse = 1f + (Mathf.Sin(Time.time * PickupPulseFrequency) * PickupPulseAmplitude);
                pickupView.Root.transform.localScale = new Vector3(pickupScale * pulse, pickupScale * pulse, 1f);
            }

            foreach (var dotView in _views.Values)
            {
                dotView.UpdateJelly(Time.time);
            }
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
            _status = ex == null ? "Disconnected" : $"Disconnected: {ex.Message}";
            Debug.LogWarning($"[DotArena] {_status}");
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
            mainCamera.orthographicSize = Mathf.Max(ArenaHalfWidth, ArenaHalfHeight) + ArenaVisualPadding;
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
                Score = 1
            });
            _entryMenuState = EntryMenuState.Hidden;
            _status = "单机匹配中...";
            _eventMessage = "正在进入本地单机模式";
            _lastWorldTick = -1;
            _inputTick = 0;
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

            var arenaRoot = new GameObject("ArenaRoot");
            arenaRoot.transform.SetParent(transform, false);

            CreateRect(arenaRoot.transform, "DangerZone", Vector2.zero,
                new Vector2((ArenaHalfWidth + 1f) * 2f, (ArenaHalfHeight + 1f) * 2f), DangerColor, -30);

            CreateRect(arenaRoot.transform, "Board", Vector2.zero, new Vector2(ArenaHalfWidth * 2f, ArenaHalfHeight * 2f),
                BoardColor, -20);

            for (var i = -8; i <= 8; i += 2)
            {
                CreateRect(arenaRoot.transform, $"Vertical-{i}", new Vector2(i, 0f),
                    new Vector2(0.05f, ArenaHalfHeight * 2f), GridColor, -10);
                CreateRect(arenaRoot.transform, $"Horizontal-{i}", new Vector2(0f, i),
                    new Vector2(ArenaHalfWidth * 2f, 0.05f), GridColor, -10);
            }

            CreateRect(arenaRoot.transform, "TopBorder", new Vector2(0f, ArenaHalfHeight),
                new Vector2(ArenaHalfWidth * 2f + 0.18f, 0.18f), BorderColor, -5);
            CreateRect(arenaRoot.transform, "BottomBorder", new Vector2(0f, -ArenaHalfHeight),
                new Vector2(ArenaHalfWidth * 2f + 0.18f, 0.18f), BorderColor, -5);
            CreateRect(arenaRoot.transform, "LeftBorder", new Vector2(-ArenaHalfWidth, 0f),
                new Vector2(0.18f, ArenaHalfHeight * 2f + 0.18f), BorderColor, -5);
            CreateRect(arenaRoot.transform, "RightBorder", new Vector2(ArenaHalfWidth, 0f),
                new Vector2(0.18f, ArenaHalfHeight * 2f + 0.18f), BorderColor, -5);
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

            _views.Clear();
            _pickupViews.Clear();
            _renderStates.Clear();
            _pendingWorldState = null;
            _pendingDeaths.Clear();
            _pendingMatchEnd = null;
            _lastWorldTick = -1;
            _lastLoggedPlayerCount = -1;
            _dashQueued = false;
            _nextInputAt = 0f;
        }

        private void ApplyPickupState(WorldState worldState)
        {
            var activeTypes = new HashSet<PickupType>();
            foreach (var pickup in worldState.Pickups)
            {
                activeTypes.Add(pickup.Type);
                if (!_pickupViews.TryGetValue(pickup.Type, out var view))
                {
                    view = CreatePickupView(pickup.Type);
                    _pickupViews.Add(pickup.Type, view);
                }

                view.Root.SetActive(true);
                view.Root.transform.position = new Vector3(pickup.X, pickup.Y, 0f);
            }

            foreach (var entry in _pickupViews)
            {
                if (!activeTypes.Contains(entry.Key))
                {
                    entry.Value.Root.SetActive(false);
                }
            }
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
            nameText.color = new Color(0.92f, 0.96f, 1f, 0.92f);
            ConfigureTextRenderer(nameText.GetComponent<MeshRenderer>(), PlayerTextSortingOrder);

            var scoreLabel = new GameObject("ScoreLabel");
            scoreLabel.transform.SetParent(viewRoot.transform, false);
            scoreLabel.transform.localPosition = new Vector3(0f, PlayerScoreOffsetY, PlayerTextDepth);
            scoreLabel.transform.localScale = Vector3.one * PlayerScoreScale;

            var scoreText = scoreLabel.AddComponent<TextMesh>();
            scoreText.text = "1";
            scoreText.fontSize = 44;
            scoreText.characterSize = PlayerTextCharacterSize;
            scoreText.anchor = TextAnchor.MiddleCenter;
            scoreText.alignment = TextAlignment.Center;
            scoreText.fontStyle = FontStyle.Bold;
            scoreText.color = new Color(1f, 0.97f, 0.78f, 0.95f);
            ConfigureTextRenderer(scoreText.GetComponent<MeshRenderer>(), PlayerTextSortingOrder);

            var view = new DotView(viewRoot, renderer, outlineRenderer, nameText, scoreText);
            view.SetIdentity(playerId, 1);
            view.ApplyPresentation(ResolveColor(playerId), PlayerLifeState.Idle, true, false, false);
            return view;
        }

        private Color ResolveColor(string playerId)
        {
            var index = GetStableColorIndex(playerId);
            return RemotePalette[index];
        }

        private void CreateRect(Transform parent, string objectName, Vector2 position, Vector2 size, Color color,
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
        }

        private PickupView CreatePickupView(PickupType pickupType)
        {
            var pickupRoot = new GameObject($"{pickupType}Pickup");
            pickupRoot.transform.SetParent(transform, false);

            var renderer = pickupRoot.AddComponent<SpriteRenderer>();
            renderer.sprite = _playerSprite;
            renderer.color = GetPickupColor(pickupType);
            renderer.sortingOrder = PickupSortingOrder;
            var glow = new GameObject("Glow");
            glow.transform.SetParent(pickupRoot.transform, false);
            glow.transform.localPosition = new Vector3(0f, 0f, 0.01f);

            var glowRenderer = glow.AddComponent<SpriteRenderer>();
            glowRenderer.sprite = _playerOutlineSprite;
            glowRenderer.color = Color.Lerp(GetPickupColor(pickupType), Color.white, 0.35f);
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
                RespawnDelaySeconds = source.RespawnDelaySeconds
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
                PickupType.SpeedBoost => SpeedPickupColor,
                PickupType.KnockbackBoost => KnockbackPickupColor,
                _ => Color.white
            };
        }

        private static string GetPickupDisplayName(PickupType pickupType)
        {
            return pickupType switch
            {
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

        private static float PlayerVisualDiameter => GameplayConfig.PlayerVisualRadius * 2f;

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

                var wobble = (0.06f + (impactPulse * 0.34f)) * wobbleScale;
                var speed = 3.6f + (impactPulse * 7.5f);
                material.SetFloat("_WobbleAmount", wobble);
                material.SetFloat("_WobbleSpeed", speed + (Mathf.Sin(time * 1.3f) * 0.15f));
            }
        }

        private sealed class PickupView
        {
            public PickupView(GameObject root, SpriteRenderer renderer, SpriteRenderer glowRenderer, TextMesh labelText)
            {
                Root = root;
                Renderer = renderer;
                GlowRenderer = glowRenderer;
                LabelText = labelText;
            }

            public GameObject Root { get; }
            public SpriteRenderer Renderer { get; }
            public SpriteRenderer GlowRenderer { get; }
            public TextMesh LabelText { get; }
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
