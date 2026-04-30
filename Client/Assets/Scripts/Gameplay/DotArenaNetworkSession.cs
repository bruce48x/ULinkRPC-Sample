#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Shared.Interfaces;
using ULinkRPC.Client;

namespace SampleClient.Gameplay
{
    internal sealed class DotArenaNetworkSession
    {
        private readonly Action<Exception?> _onDisconnected;
        private RpcClient? _controlConnection;
        private IPlayerService? _controlPlayerService;
        private RpcClient? _realtimeConnection;
        private IPlayerService? _realtimePlayerService;
        private string _playerId = string.Empty;
        private string _token = string.Empty;
        private string _realtimeRoomId = string.Empty;
        private string _realtimeMatchId = string.Empty;
        private bool _ignoreControlDisconnect;
        private bool _ignoreRealtimeDisconnect;

        public DotArenaNetworkSession(Action<Exception?> onDisconnected)
        {
            _onDisconnected = onDisconnected;
        }

        public bool IsConnected { get; private set; }

        public bool IsConnecting { get; private set; }

        public bool IsRealtimeConnected { get; private set; }

        public bool IsRealtimeConnecting { get; private set; }

        public async Task<LoginReply> ConnectAndLoginAsync(
            string host,
            int port,
            string path,
            string account,
            string password,
            IPlayerCallback callback,
            CancellationToken cancellationToken)
        {
            if (IsConnecting)
            {
                throw new InvalidOperationException("Connection attempt is already in progress.");
            }

            IsConnecting = true;
            try
            {
                var callbacks = new RpcClient.RpcCallbackBindings();
                callbacks.Add(callback);

                _controlConnection = Rpc.WebSocketRpcClientFactory.Create(host, port, path, callbacks);
                _controlConnection.Disconnected += HandleControlDisconnected;

                await _controlConnection.ConnectAsync(cancellationToken);

                _controlPlayerService = _controlConnection.Api.Shared.Player;
                var reply = await _controlPlayerService.LoginAsync(new LoginRequest
                {
                    Account = account,
                    Password = password
                });

                if (reply.Code != 0)
                {
                    await DisposeAsync(logout: false).ConfigureAwait(false);
                    return reply;
                }

                _playerId = reply.PlayerId;
                _token = reply.Token;
                IsConnected = true;
                return reply;
            }
            finally
            {
                IsConnecting = false;
            }
        }

        public async Task SubmitInputAsync(InputMessage input)
        {
            var playerService = _realtimePlayerService ?? _controlPlayerService;
            if (playerService == null)
            {
                return;
            }

            await playerService.SubmitInput(input).ConfigureAwait(false);
        }

        public async Task StartMatchmakingAsync(CancellationToken cancellationToken = default)
        {
            if (_controlPlayerService == null || string.IsNullOrWhiteSpace(_playerId))
            {
                return;
            }

            await _controlPlayerService.StartMatchmakingAsync(new MatchmakingRequest
            {
                PlayerId = _playerId,
                Token = _token
            }).ConfigureAwait(false);
        }

        public async Task CancelMatchmakingAsync(CancellationToken cancellationToken = default)
        {
            if (_controlPlayerService == null || string.IsNullOrWhiteSpace(_playerId))
            {
                return;
            }

            await _controlPlayerService.CancelMatchmakingAsync(new CancelMatchmakingRequest
            {
                PlayerId = _playerId,
                Token = _token
            }).ConfigureAwait(false);
        }

        public async Task<bool> EnsureRealtimeConnectedAsync(
            RealtimeConnectionInfo realtimeConnection,
            IPlayerCallback callback,
            CancellationToken cancellationToken)
        {
            if (realtimeConnection == null)
            {
                return false;
            }

            if (realtimeConnection.Transport != RealtimeTransportKind.Kcp)
            {
                return false;
            }

            if (IsRealtimeConnected &&
                string.Equals(_realtimeRoomId, realtimeConnection.RoomId, StringComparison.Ordinal) &&
                string.Equals(_realtimeMatchId, realtimeConnection.MatchId, StringComparison.Ordinal))
            {
                return true;
            }

            if (IsRealtimeConnecting &&
                string.Equals(_realtimeRoomId, realtimeConnection.RoomId, StringComparison.Ordinal) &&
                string.Equals(_realtimeMatchId, realtimeConnection.MatchId, StringComparison.Ordinal))
            {
                return false;
            }

            await DisposeRealtimeAsync().ConfigureAwait(false);

            IsRealtimeConnecting = true;
            _realtimeRoomId = realtimeConnection.RoomId ?? string.Empty;
            _realtimeMatchId = realtimeConnection.MatchId ?? string.Empty;

            try
            {
                var callbacks = new RpcClient.RpcCallbackBindings();
                callbacks.Add(callback);

                _realtimeConnection = Rpc.KcpRpcClientFactory.Create(realtimeConnection.Host, realtimeConnection.Port, callbacks);
                _realtimeConnection.Disconnected += HandleRealtimeDisconnected;

                await _realtimeConnection.ConnectAsync(cancellationToken).ConfigureAwait(false);

                _realtimePlayerService = _realtimeConnection.Api.Shared.Player;
                var reply = await _realtimePlayerService.AttachRealtimeAsync(new RealtimeAttachRequest
                {
                    PlayerId = _playerId,
                    Token = string.IsNullOrWhiteSpace(realtimeConnection.SessionToken) ? _token : realtimeConnection.SessionToken,
                    RoomId = realtimeConnection.RoomId ?? string.Empty,
                    MatchId = realtimeConnection.MatchId ?? string.Empty
                }).ConfigureAwait(false);

                if (reply.Code != 0)
                {
                    await DisposeRealtimeAsync().ConfigureAwait(false);
                    return false;
                }

                IsRealtimeConnected = true;
                return true;
            }
            finally
            {
                IsRealtimeConnecting = false;
            }
        }

        public async Task DisposeAsync(bool logout = true)
        {
            await DisposeRealtimeAsync().ConfigureAwait(false);

            if (_controlConnection == null)
            {
                _controlPlayerService = null;
                IsConnected = false;
                IsConnecting = false;
                return;
            }

            var connection = _controlConnection;
            var playerService = _controlPlayerService;
            var shouldLogout = logout && IsConnected && playerService != null;

            _controlConnection = null;
            _ignoreControlDisconnect = true;
            connection.Disconnected -= HandleControlDisconnected;

            try
            {
                if (shouldLogout)
                {
                    await playerService!.LogoutAsync(new LogoutRequest()).ConfigureAwait(false);
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
                _ignoreControlDisconnect = false;
                _controlPlayerService = null;
                _playerId = string.Empty;
                _token = string.Empty;
                IsConnected = false;
                IsConnecting = false;
            }
        }

        public async Task DisposeRealtimeAsync()
        {
            if (_realtimeConnection == null)
            {
                _realtimePlayerService = null;
                IsRealtimeConnected = false;
                IsRealtimeConnecting = false;
                _realtimeRoomId = string.Empty;
                _realtimeMatchId = string.Empty;
                return;
            }

            var connection = _realtimeConnection;
            _realtimeConnection = null;
            _ignoreRealtimeDisconnect = true;
            connection.Disconnected -= HandleRealtimeDisconnected;

            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                _ignoreRealtimeDisconnect = false;
                _realtimePlayerService = null;
                IsRealtimeConnected = false;
                IsRealtimeConnecting = false;
                _realtimeRoomId = string.Empty;
                _realtimeMatchId = string.Empty;
            }
        }

        private void HandleControlDisconnected(Exception? ex)
        {
            if (_ignoreControlDisconnect)
            {
                return;
            }

            IsConnected = false;
            _controlPlayerService = null;
            _playerId = string.Empty;
            _token = string.Empty;
            _onDisconnected(ex);
        }

        private void HandleRealtimeDisconnected(Exception? ex)
        {
            if (_ignoreRealtimeDisconnect)
            {
                return;
            }

            IsRealtimeConnected = false;
            _realtimePlayerService = null;
            _realtimeRoomId = string.Empty;
            _realtimeMatchId = string.Empty;
            _onDisconnected(ex);
        }
    }
}
