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
        private RpcClient? _connection;
        private IPlayerService? _playerService;

        public DotArenaNetworkSession(Action<Exception?> onDisconnected)
        {
            _onDisconnected = onDisconnected;
        }

        public bool IsConnected { get; private set; }

        public bool IsConnecting { get; private set; }

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

                _connection = Rpc.WebSocketRpcClientFactory.Create(host, port, path, callbacks);
                _connection.Disconnected += HandleDisconnected;

                await _connection.ConnectAsync(cancellationToken);

                _playerService = _connection.Api.Shared.Player;
                var reply = await _playerService.LoginAsync(new LoginRequest
                {
                    Account = account,
                    Password = password
                });

                if (reply.Code != 0)
                {
                    await DisposeAsync(logout: false).ConfigureAwait(false);
                    return reply;
                }

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
            if (_playerService == null)
            {
                return;
            }

            await _playerService.SubmitInput(input);
        }

        public async Task DisposeAsync(bool logout = true)
        {
            if (_connection == null)
            {
                _playerService = null;
                IsConnected = false;
                IsConnecting = false;
                return;
            }

            var connection = _connection;
            var playerService = _playerService;
            var shouldLogout = logout && IsConnected && playerService != null;

            _connection = null;
            connection.Disconnected -= HandleDisconnected;

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
                _playerService = null;
                IsConnected = false;
                IsConnecting = false;
            }
        }

        private void HandleDisconnected(Exception? ex)
        {
            IsConnected = false;
            _playerService = null;
            _onDisconnected(ex);
        }
    }
}
