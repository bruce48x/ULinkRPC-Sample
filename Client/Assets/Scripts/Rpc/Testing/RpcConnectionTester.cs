#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Rpc.Generated;
using Shared.Interfaces;
using ULinkRPC.Client;
using ULinkRPC.Transport.WebSocket;
using ULinkRPC.Serializer.MemoryPack;
using UnityEngine;

namespace Rpc.Testing
{
    [Serializable]
    public sealed class RpcEndpointSettings
    {
        public string Host = "127.0.0.1";
        public int Port = 20000;
        public string Path = string.Empty;

        public static RpcEndpointSettings CreateDefault() => new RpcEndpointSettings { Host = "127.0.0.1", Port = 20000, Path = "/ws" };
        
        public static RpcEndpointSettings CreateWebSocket(string host, int port, string path = "/ws")
        {
            return new RpcEndpointSettings
            {
                Host = host,
                Port = port,
                Path = path
            };
        }
        
        public string GetWebSocketUrl()
        {
            var normalizedPath = string.IsNullOrWhiteSpace(Path)
                ? "/ws"
                : Path.StartsWith("/", StringComparison.Ordinal) ? Path : "/" + Path;
            return $"ws://{Host}:{Port}{normalizedPath}";
        }
    }

    public sealed class RpcConnectionTester : MonoBehaviour
    {
        [SerializeField] private RpcEndpointSettings _endpoint = RpcEndpointSettings.CreateDefault();

        
        [Header("Login")] public string Account = "a";
        public string Password = "b";
        public float RequestIntervalSeconds = 1f;

        public bool AutoConnect = true;

        private readonly CancellationTokenSource _cts = new();
        private readonly RpcClient.RpcCallbackBindings _callbacks;

        private RpcClient? _connection;
        private bool _isShuttingDown;
        private IPlayerService? _player;
        private Task? _pollingTask;
        private bool _cleanupStarted;
        private bool _stopped;
        
        public RpcConnectionTester()
        {
            _callbacks = new RpcClient.RpcCallbackBindings();
            _callbacks.Add(new PlayerCallbacks(this));
        }

        private async void Start()
        {
            if (!AutoConnect)
                return;

            await ConnectAndPingAsync();
        }

        private void OnDestroy()
        {
            _ = ShutdownAsync();
        }

        [ContextMenu("Connect And Ping")]
        public async Task ConnectAndPingAsync()
        {
            if (_cleanupStarted || _connection is not null)
                return;

            Debug.Log($"[TCP] Connecting to {_endpoint.Host}:{_endpoint.Port}");

            try
            {
                _connection = new RpcClient(
                    new RpcClientOptions(
                        new WsTransport(_endpoint.GetWebSocketUrl()),
                        new MemoryPackRpcSerializer()),
                    _callbacks);
                await _connection.ConnectAsync(_cts.Token);
                _connection.Disconnected += OnDisconnected;
                _player = _connection.Api.Shared.Player;

                var reply = await _player.LoginAsync(new LoginRequest
                {
                    Account = Account,
                    Password = Password
                });

                Debug.Log($"[TCP] Login ok: account={Account}, code={reply.Code}, token={reply.Token}");
                _pollingTask = RunPollingAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TCP] Connect failed: {ex}");
                await CleanupAsync();
            }
        }
        
        private async Task RunPollingAsync()
        {
            var interval = Mathf.Max(0.1f, RequestIntervalSeconds);

            while (!_cts.IsCancellationRequested && !_stopped)
            {
                try
                {
                    await _player!.Move(new MoveRequest() { Direction = 1, PlayerId = Account });
                    if (_cts.IsCancellationRequested || _stopped)
                        return;

                    Debug.Log($"{Account} Moved");
                    await Task.Delay(TimeSpan.FromSeconds(interval), _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[TCP] Polling failed: {ex.Message}");
                    return;
                }
            }
        }
        
        private async Task CleanupAsync()
        {
            if (_pollingTask is not null)
            {
                try
                {
                    await _pollingTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }
        }

        private void OnDisconnected(Exception? ex)
        {
            if (_stopped)
                return;

            _stopped = true;
            _connection = null;

            if (ex is null)
                Debug.Log("[TCP] Disconnected.");
            else
                Debug.LogWarning($"[TCP] Disconnected: {ex.Message}");
        }

        private string DescribeEndpoint()
        {
            var path = NormalizePath(_endpoint.Path);
            return string.IsNullOrEmpty(path)
                ? $"{_endpoint.Host}:{_endpoint.Port}"
                : $"{_endpoint.Host}:{_endpoint.Port}{path}";
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
        }

        private async Task ShutdownAsync()
        {
            if (_isShuttingDown)
                return;

            _isShuttingDown = true;
            _cts.Cancel();

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            _cts.Dispose();
        }
        
        private sealed class PlayerCallbacks : RpcClient.PlayerCallbackBase
        {
            private readonly RpcConnectionTester _owner;

            public PlayerCallbacks(RpcConnectionTester owner)
            {
                _owner = owner;
            }

            public override void OnMove(PlayerPositions playerPositions)
            {
                Debug.Log($"OnMove {playerPositions.playerPositions.Count}");
            }
        }
    }
}
