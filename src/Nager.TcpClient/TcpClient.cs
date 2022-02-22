using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nager.TcpClient
{
    /// <summary>
    /// An easy tcp client
    /// </summary>
    public class TcpClient : IDisposable
    {
        private readonly ILogger<TcpClient> _logger;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly CancellationTokenRegistration _streamCancellationTokenRegistration;

        private readonly byte[] _receiveBuffer;

        private System.Net.Sockets.TcpClient? _tcpClient;
        private Stream? _stream;

        /// <summary>
        /// Event to call when the connection is established.
        /// </summary>
        public event Action? Connected;

        /// <summary>
        /// Event to call when the connection is destroyed.
        /// </summary>
        public event Action? Disconnected;

        /// <summary>
        /// Event to call when byte data has become available from the server.
        /// </summary>
        public event Action<byte[]>? DataReceived;

        /// <summary>
        /// TcpClient
        /// </summary>
        /// <param name="clientConfig"></param>
        /// <param name="logger"></param>
        public TcpClient(
            TcpClientConfig? clientConfig = default,
            ILogger<TcpClient>? logger = default)
        {
            this._cancellationTokenSource = new CancellationTokenSource();

            if (clientConfig == default)
            {
                clientConfig = new TcpClientConfig();
            }

            this._receiveBuffer = new byte[clientConfig.ReceiveBufferSize];

            if (logger == default)
            {
                logger = new NullLogger<TcpClient>();
            }
            this._logger = logger;

            this._streamCancellationTokenRegistration = this._cancellationTokenSource.Token.Register(() =>
            {
                if (this._stream == null)
                {
                    return;
                }

                this._stream.Close();
            });

            _ = Task.Run(async () => await this.DataReceiver(this._cancellationTokenSource.Token), this._cancellationTokenSource.Token);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                this._cancellationTokenSource.Cancel();
                this._cancellationTokenSource.Dispose();

                this._streamCancellationTokenRegistration.Dispose();

                this.DisposeTcpClientAndStream();
            }
        }

        private void DisposeTcpClientAndStream()
        {
            this._stream?.Close();
            this._stream?.Dispose();

            if (this._tcpClient != null)
            {
                if (this._tcpClient.Connected)
                {
                    this._tcpClient.Close();
                }
                this._tcpClient.Dispose();
            }
        }

        private void PrepareStream()
        {
            if (this._tcpClient == null)
            {
                this._logger.LogError($"{nameof(PrepareStream)} - TcpClient is null");
                return;
            }

            this._stream = this._tcpClient.GetStream();
        }

        /// <summary>
        /// Connect
        /// </summary>
        /// <param name="ipAddressOrHostname"></param>
        /// <param name="port"></param>
        /// <param name="connectionTimeoutInMilliseconds"></param>
        /// <exception cref="TimeoutException"></exception>
        public void Connect(
            string ipAddressOrHostname,
            int port,
            int connectionTimeoutInMilliseconds)
        {
            ipAddressOrHostname = ipAddressOrHostname ?? throw new ArgumentNullException(nameof(ipAddressOrHostname));

            this._tcpClient = new System.Net.Sockets.TcpClient();

            this._logger.LogDebug("Connecting");
            IAsyncResult asyncResult = this._tcpClient.BeginConnect(ipAddressOrHostname, port, null, null);
            var waitHandle = asyncResult.AsyncWaitHandle;

            if (!asyncResult.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(connectionTimeoutInMilliseconds), exitContext: false))
            {
                this._tcpClient.Close();
                waitHandle.Close();
                throw new TimeoutException($"Timeout reached, {connectionTimeoutInMilliseconds}ms");
            }

            this._tcpClient.EndConnect(asyncResult);

            this._logger.LogInformation("Connected");
            this.Connected?.Invoke();

            this.PrepareStream();
        }

        /// <summary>
        /// ConnectAsync
        /// </summary>
        /// <param name="ipAddressOrHostname"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        public async Task ConnectAsync(
            string ipAddressOrHostname,
            int port)
        {
            this._tcpClient = new System.Net.Sockets.TcpClient();

            this._logger.LogDebug("Connecting");
            await this._tcpClient.ConnectAsync(ipAddressOrHostname, port);
            this._logger.LogInformation("Connected");
            this.Connected?.Invoke();

            this.PrepareStream();
        }

#if (NET6_0 || NET5_0)

        /// <summary>
        /// ConnectAsync
        /// </summary>
        /// <param name="ipAddressOrHostname"></param>
        /// <param name="port"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task ConnectAsync(
            string ipAddressOrHostname,
            int port,
            CancellationToken cancellationToken = default)
        {
            this._tcpClient = new System.Net.Sockets.TcpClient();

            this._logger.LogDebug("Connecting");
            await this._tcpClient.ConnectAsync(ipAddressOrHostname, port, cancellationToken);
            this._logger.LogInformation("Connected");
            this.Connected?.Invoke();

            this.PrepareStream();
        }

#endif

        /// <summary>
        /// Disconnect
        /// </summary>
        public void Disconnect()
        {
            this.DisposeTcpClientAndStream();
        }

        /// <summary>
        /// Send data async
        /// </summary>
        /// <param name="data"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task SendAsync(
            byte[] data,
            CancellationToken cancellationToken = default)
        {
            if (this._stream == null)
            {
                return;
            }

            this._logger.LogDebug($"{nameof(SendAsync)} {BitConverter.ToString(data)}");
            await this._stream.WriteAsync(data, 0, data.Length, cancellationToken);
        }

        private async Task DataReceiver(CancellationToken cancellationToken = default)
        {
            this._logger.LogInformation($"{nameof(DataReceiver)} - Starting");
            var defaultTimeout = TimeSpan.FromMilliseconds(100);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (this._tcpClient == null)
                {
                    this._logger.LogTrace($"{nameof(DataReceiver)} - TcpClient not initialized");
                    await Task.Delay(defaultTimeout, cancellationToken).ContinueWith(task => { }).ConfigureAwait(false);
                    continue;
                }

                if (!this._tcpClient.Connected)
                {
                    this._logger.LogTrace($"{nameof(DataReceiver)} - Client not connected");
                    await Task.Delay(defaultTimeout, cancellationToken).ContinueWith(task => { }).ConfigureAwait(false);
                    continue;
                }

                if (this._stream == null)
                {
                    this._logger.LogTrace($"{nameof(DataReceiver)} - Stream not ready");
                    await Task.Delay(defaultTimeout, cancellationToken).ContinueWith(task => { }).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    this._logger.LogTrace($"{nameof(DataReceiver)} - Wait for data...");

                    await DataReadAsync(cancellationToken).ContinueWith(async task =>
                    {
                        if (task.IsCanceled)
                        {
                            this._logger.LogTrace($"{nameof(DataReceiver)} - Timeout");
                            return;
                        }

                        byte[] data = task.Result;

                        if (data == null)
                        {
                            this._logger.LogTrace($"{nameof(DataReceiver)} - No data received");
                            await Task.Delay(defaultTimeout).ConfigureAwait(false);
                            return;
                        }

                        this.DataReceived?.Invoke(data);

                    }, cancellationToken).ContinueWith(task => { }).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    this._logger.LogInformation("Disconnected");
                    this.Disconnected?.Invoke();
                    this._logger.LogError(exception, $"{nameof(DataReceiver)}");
                    break;
                }
            }

            this._logger.LogInformation($"{nameof(DataReceiver)} - Stopped");
        }

        private async Task<byte[]> DataReadAsync(CancellationToken cancellationToken)
        {
            if (this._stream == null)
            {
                return Array.Empty<byte>();
            }

            this._logger.LogTrace($"{nameof(DataReadAsync)} - Read data...");
            var numberOfBytesToRead = await this._stream.ReadAsync(this._receiveBuffer, 0, this._receiveBuffer.Length, cancellationToken).ConfigureAwait(false);
            this._logger.LogTrace($"{nameof(DataReadAsync)} - NumberOfBytesToRead:{numberOfBytesToRead}");

            using var memoryStream = new MemoryStream();
            await memoryStream.WriteAsync(this._receiveBuffer, 0, numberOfBytesToRead, cancellationToken).ConfigureAwait(false);
            return memoryStream.ToArray();
        }
    }
}