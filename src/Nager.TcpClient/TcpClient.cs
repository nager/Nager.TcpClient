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
        private readonly Task _dataReceiverTask;
        private readonly TcpClientKeepAliveConfig? _keepAliveConfig;
        private readonly object _lock = new object();

        private readonly byte[] _receiveBuffer;

        private System.Net.Sockets.TcpClient? _tcpClient;
        private Stream? _stream;
        private bool _isConnected;

        /// <summary>
        /// Is client connected
        /// </summary>
        public bool IsConnected {  get { return _isConnected; } }

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
        /// <param name="keepAliveConfig"></param>
        /// <param name="logger"></param>
        public TcpClient(
            TcpClientConfig? clientConfig = default,
            TcpClientKeepAliveConfig? keepAliveConfig = default,
            ILogger<TcpClient>? logger = default)
        {
            this._cancellationTokenSource = new CancellationTokenSource();
            this._keepAliveConfig = keepAliveConfig;

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

            this._dataReceiverTask = Task.Run(async () => await this.DataReceiverAsync(this._cancellationTokenSource.Token), this._cancellationTokenSource.Token);
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
                this._logger.LogTrace($"{nameof(Dispose)} - start");

                if (this._cancellationTokenSource != null)
                {
                    if (!this._cancellationTokenSource.IsCancellationRequested)
                    {
                        this._cancellationTokenSource.Cancel();
                    }
                }

                this._cancellationTokenSource?.Dispose();

                this._dataReceiverTask.Wait(50);

                this._streamCancellationTokenRegistration.Dispose();

                this.DisposeTcpClientAndStream();

                this._logger.LogTrace($"{nameof(Dispose)} - done");
            }
        }

        private void DisposeTcpClientAndStream()
        {
            if (this._stream != null)
            {
                if (this._stream.CanWrite || this._stream.CanRead || this._stream.CanSeek)
                {
                    this._logger.LogTrace($"{nameof(DisposeTcpClientAndStream)} - Dispose stream");

                    this._stream?.Close();
                    this._stream?.Dispose();
                }
            }

            if (this._tcpClient != null)
            {
                if (this._tcpClient.Connected)
                {
                    this._logger.LogTrace($"{nameof(DisposeTcpClientAndStream)} - Close TcpClient");
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

            if (this._keepAliveConfig != null)
            {
                if (!this._tcpClient.SetKeepAlive(this._keepAliveConfig.KeepAliveTime, this._keepAliveConfig.KeepAliveInterval, this._keepAliveConfig.KeepAliveRetryCount))
                {
                    this._logger.LogError($"{nameof(PrepareStream)} - Cannot set KeepAlive config");
                }
            }
        }

        private bool SwitchToConnected()
        {
            lock (this._lock)
            {
                if (this._isConnected)
                {
                    return false;
                }

                this._isConnected = true;
                this.Connected?.Invoke();

                return true;
            }
        }

        private bool SwitchToDisconnected()
        {
            lock (this._lock)
            {
                if (!this._isConnected)
                {
                    return false;
                }

                this._isConnected = false;
                this.Disconnected?.Invoke();

                return true;
            }
        }

        /// <summary>
        /// Connect
        /// </summary>
        /// <param name="ipAddressOrHostname"></param>
        /// <param name="port"></param>
        /// <param name="connectionTimeoutInMilliseconds">default: 2s</param>
        public bool Connect(
            string ipAddressOrHostname,
            int port,
            int connectionTimeoutInMilliseconds = 2000)
        {
            ipAddressOrHostname = ipAddressOrHostname ?? throw new ArgumentNullException(nameof(ipAddressOrHostname));

            if (this._isConnected)
            {
                return false;
            }

            this._tcpClient = new System.Net.Sockets.TcpClient();

            this._logger.LogDebug($"{nameof(Connect)} - Connecting");
            IAsyncResult asyncResult = this._tcpClient.BeginConnect(ipAddressOrHostname, port, null, null);
            var waitHandle = asyncResult.AsyncWaitHandle;

            if (!asyncResult.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(connectionTimeoutInMilliseconds), exitContext: false))
            {
                this._logger.LogError($"{nameof(Connect)} - Timeout");

                this._tcpClient.Close();
                waitHandle.Close();

                return false;
            }

            this._tcpClient.EndConnect(asyncResult);

            this._logger.LogInformation($"{nameof(Connect)} - Connected");
            this.SwitchToConnected();

            this.PrepareStream();
            return true;
        }

        /// <summary>
        /// ConnectAsync
        /// </summary>
        /// <param name="ipAddressOrHostname"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        public async Task<bool> ConnectAsync(
            string ipAddressOrHostname,
            int port)
        {
            ipAddressOrHostname = ipAddressOrHostname ?? throw new ArgumentNullException(nameof(ipAddressOrHostname));

            if (this._isConnected)
            {
                return false;
            }

            this._tcpClient = new System.Net.Sockets.TcpClient();

            this._logger.LogDebug($"{nameof(ConnectAsync)} - Connecting");

            try
            {
                await this._tcpClient.ConnectAsync(ipAddressOrHostname, port).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                this._logger.LogError(exception, $"{nameof(ConnectAsync)} - Cannot connect");
                return false;
            }

            this._logger.LogInformation($"{nameof(ConnectAsync)} - Connected");
            this.SwitchToConnected();

            this.PrepareStream();
            return true;
        }

#if (NET5_0_OR_GREATER)

        /// <summary>
        /// ConnectAsync
        /// </summary>
        /// <param name="ipAddressOrHostname"></param>
        /// <param name="port"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<bool> ConnectAsync(
            string ipAddressOrHostname,
            int port,
            CancellationToken cancellationToken = default)
        {
            ipAddressOrHostname = ipAddressOrHostname ?? throw new ArgumentNullException(nameof(ipAddressOrHostname));

            if (this._isConnected)
            {
                return false;
            }

            this._tcpClient = new System.Net.Sockets.TcpClient();

            this._logger.LogDebug($"{nameof(ConnectAsync)} - Connecting");

            try
            {
                await this._tcpClient.ConnectAsync(ipAddressOrHostname, port, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                this._logger.LogError(exception, $"{nameof(ConnectAsync)} - Cannot connect");
                return false;
            }

            this._logger.LogInformation($"{nameof(ConnectAsync)} - Connected");
            this.SwitchToConnected();

            this.PrepareStream();
            return true;
        }

#endif

        /// <summary>
        /// Disconnect
        /// </summary>
        public void Disconnect()
        {
            this._logger.LogInformation($"{nameof(Disconnect)}");
            this.DisposeTcpClientAndStream();
            this.SwitchToDisconnected();
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
#if (NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER)
            await this._stream.WriteAsync(data.AsMemory(0, data.Length), cancellationToken).ConfigureAwait(false);
#else
            await this._stream.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
#endif
        }

        private async Task DataReceiverAsync(CancellationToken cancellationToken = default)
        {
            this._logger.LogInformation($"{nameof(DataReceiverAsync)} - Starting");
            var defaultTimeout = TimeSpan.FromMilliseconds(100);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (this._tcpClient == null)
                {
                    this._logger.LogTrace($"{nameof(DataReceiverAsync)} - TcpClient not initialized");
                    await Task.Delay(defaultTimeout, cancellationToken).ContinueWith(task => { }).ConfigureAwait(false);
                    continue;
                }

                if (!this._tcpClient.Connected)
                {
                    this.SwitchToDisconnected();
                    this._logger.LogTrace($"{nameof(DataReceiverAsync)} - Client not connected");
                    await Task.Delay(defaultTimeout, cancellationToken).ContinueWith(task => { }).ConfigureAwait(false);
                    continue;
                }

                if (this._stream == null)
                {
                    this._logger.LogTrace($"{nameof(DataReceiverAsync)} - Stream not ready");
                    await Task.Delay(defaultTimeout, cancellationToken).ContinueWith(task => { }).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    this._logger.LogTrace($"{nameof(DataReceiverAsync)} - Wait for data...");

                    await DataReadAsync(cancellationToken).ContinueWith(async task =>
                    {
                        if (task.IsCanceled)
                        {
                            this._logger.LogTrace($"{nameof(DataReceiverAsync)} - Timeout");
                            return;
                        }

                        byte[] data = task.Result;

                        if (data == null || data.Length == 0)
                        {
                            this._logger.LogTrace($"{nameof(DataReceiverAsync)} - No data received");
                            await Task.Delay(defaultTimeout).ConfigureAwait(false);
                            return;
                        }

                        this.DataReceived?.Invoke(data);

                    }, cancellationToken).ContinueWith(task => { }).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    this._logger.LogInformation("Disconnected");
                    this.SwitchToDisconnected();
                    this._logger.LogError(exception, $"{nameof(DataReceiverAsync)}");
                    break;
                }
            }

            this._logger.LogInformation($"{nameof(DataReceiverAsync)} - Stopped");
        }

        private async Task<byte[]> DataReadAsync(CancellationToken cancellationToken)
        {
            if (this._stream == null)
            {
                return Array.Empty<byte>();
            }

            this._logger.LogTrace($"{nameof(DataReadAsync)} - Read data...");
#if (NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER)
            var numberOfBytesToRead = await this._stream.ReadAsync(this._receiveBuffer.AsMemory(0, this._receiveBuffer.Length), cancellationToken).ConfigureAwait(false);
#else
            var numberOfBytesToRead = await this._stream.ReadAsync(this._receiveBuffer, 0, this._receiveBuffer.Length, cancellationToken).ConfigureAwait(false);
#endif
            this._logger.LogTrace($"{nameof(DataReadAsync)} - NumberOfBytesToRead:{numberOfBytesToRead}");

            using var memoryStream = new MemoryStream();
#if (NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER)
            await memoryStream.WriteAsync(this._receiveBuffer.AsMemory(0, numberOfBytesToRead), cancellationToken).ConfigureAwait(false);
#else
            await memoryStream.WriteAsync(this._receiveBuffer, 0, numberOfBytesToRead, cancellationToken).ConfigureAwait(false);
#endif
            return memoryStream.ToArray();
        }
    }
}