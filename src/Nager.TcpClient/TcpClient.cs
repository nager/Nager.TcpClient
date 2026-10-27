using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Nager.TcpClient
{
    /// <summary>
    /// A simple, thread-safe TcpClient with a background receive loop
    /// </summary>
    /// <remarks>
    /// <para>
    /// State machine: Disconnected → Connecting → Connected → Disconnected (→ Disposed).
    /// Every transition happens under <c>_stateLock</c>, events are always raised outside of it.
    /// </para>
    /// <para>
    /// Each connection attempt owns a private <see cref="TcpConnection"/> (socket, stream, receive buffer and receive loop).
    /// A late result of an aborted attempt can therefore never interfere with a newer connection, and there is no
    /// polling loop while the client is disconnected.
    /// </para>
    /// <para>
    /// <see cref="TcpClientConfig.SendTimeout"/> and <see cref="TcpClientConfig.ReceiveTimeout"/> are applied to the
    /// asynchronous operations of this class (the socket options only cover synchronous socket calls).
    /// A value of 0 or <see cref="Timeout.Infinite"/> disables the timeout. When a send times out or is cancelled
    /// the connection is closed, because the content of the stream is undefined after an aborted write.
    /// </para>
    /// </remarks>
    public class TcpClient :
        IDisposable
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        , IAsyncDisposable
#endif
    {
        private enum ConnectionState
        {
            Disconnected,
            Connecting,
            Connected,
            Disposed
        }

        private enum SendResult
        {
            Success,
            Failed,
            Cancelled
        }

        /// <summary>
        /// How long Dispose waits for the receive loop (and a handler currently running on it) to finish
        /// </summary>
        private static readonly TimeSpan DisposeWaitTimeout = TimeSpan.FromSeconds(1);

        /// <summary>
        /// True on the execution context of the receive loop, i.e. inside every event handler raised by it.
        /// Dispose uses it to avoid waiting for the receive loop from within the receive loop.
        /// </summary>
        private static readonly AsyncLocal<bool> InsideReceiveLoop = new AsyncLocal<bool>();

        private readonly ILogger<TcpClient> _logger;
        private readonly TcpClientConfig _tcpClientConfig;
        private readonly TcpClientKeepAliveConfig? _keepAliveConfig;

        private readonly object _stateLock = new object();

        // NetworkStream supports only one concurrent write - serializes the senders.
        // Intentionally never disposed: SemaphoreSlim holds no kernel handle unless AvailableWaitHandle is used,
        // and disposing it while a sender waits is undefined behaviour.
        private readonly SemaphoreSlim _sendSemaphore = new SemaphoreSlim(1, 1);

        private volatile ConnectionState _state;
        private volatile TcpConnection? _connection;
        private int _disposed;

        /// <summary>
        /// Is client connected
        /// </summary>
        public bool IsConnected => this._state == ConnectionState.Connected;

        /// <summary>
        /// Event to call when the connection is established.
        /// Raised on the thread that called Connect/ConnectAsync.
        /// Every subscriber is invoked separately: an exception thrown by one handler is logged,
        /// the remaining handlers still run and the connection state is not affected.
        /// </summary>
        public event Action? Connected;

        /// <summary>
        /// Event to call when the connection is destroyed - by the remote endpoint, by a failed send,
        /// by a timeout or by <see cref="Disconnect"/>. Raised exactly once per connection.
        /// <see cref="Dispose()"/> does not raise this event, call <see cref="Disconnect"/> first if handlers must run.
        /// Every subscriber is invoked separately: an exception thrown by one handler is logged,
        /// the remaining handlers still run and the connection state is not affected.
        /// </summary>
        public event Action? Disconnected;

        /// <summary>
        /// Event to call when byte data has become available from the server.
        /// The event is raised synchronously on the receive loop - a slow handler blocks reading
        /// (but does not count towards <see cref="TcpClientConfig.ReceiveTimeout"/>).
        /// Every subscriber is invoked separately: an exception thrown by one handler is logged,
        /// the remaining handlers still run and the connection is not closed.
        /// </summary>
        public event Action<byte[]>? DataReceived;

        /// <summary>
        /// TcpClient
        /// </summary>
        /// <param name="clientConfig"></param>
        /// <param name="keepAliveConfig"></param>
        /// <param name="logger"></param>
        /// <exception cref="ArgumentOutOfRangeException">The configuration contains an invalid value</exception>
        public TcpClient(
            TcpClientConfig? clientConfig = default,
            TcpClientKeepAliveConfig? keepAliveConfig = default,
            ILogger<TcpClient>? logger = default)
        {
            clientConfig ??= new TcpClientConfig();

            ValidateConfig(clientConfig);

            this._tcpClientConfig = clientConfig;
            this._keepAliveConfig = keepAliveConfig;
            this._logger = logger ?? NullLogger<TcpClient>.Instance;
        }

        #region Connect

        /// <summary>
        /// Connect
        /// </summary>
        /// <param name="ipAddressOrHostname"></param>
        /// <param name="port"></param>
        /// <param name="connectTimeoutInMilliseconds">default: 2s, <see cref="Timeout.Infinite"/> for no timeout</param>
        /// <returns>
        /// true when the connection was established.
        /// false when the client is already connected, another connect is in progress,
        /// the timeout was reached or the connection failed.
        /// </returns>
        /// <exception cref="ObjectDisposedException">The client has been disposed</exception>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public bool Connect(
            string ipAddressOrHostname,
            int port,
            int connectTimeoutInMilliseconds = 2000)
        {
            ValidateEndpoint(ipAddressOrHostname, port);

            if (connectTimeoutInMilliseconds <= 0 && connectTimeoutInMilliseconds != Timeout.Infinite)
            {
                throw new ArgumentOutOfRangeException(nameof(connectTimeoutInMilliseconds), connectTimeoutInMilliseconds, "Timeout must be greater than 0 or Timeout.Infinite");
            }

            this.ThrowIfDisposed();

            var connection = this.TryBeginConnect();
            if (connection is null)
            {
                this._logger.LogDebug("{Method} - Already connected or a connect is in progress", nameof(Connect));
                return false;
            }

            try
            {
                this._logger.LogDebug("{Method} - Connecting to {Host}:{Port}", nameof(Connect), ipAddressOrHostname, port);

                if (!TryConnectBlocking(connection.Client, ipAddressOrHostname, port, connectTimeoutInMilliseconds, out var connectException))
                {
                    this._logger.LogError("{Method} - Timeout reached ({Timeout} ms)", nameof(Connect), connectTimeoutInMilliseconds);

                    // Closing the socket also completes the still pending BeginConnect
                    this.AbortConnect(connection);
                    return false;
                }

                if (connectException != null)
                {
                    this._logger.LogError(connectException, "{Method} - Cannot connect", nameof(Connect));
                    this.AbortConnect(connection);
                    return false;
                }

                return this.CompleteConnect(connection);
            }
            catch (Exception exception)
            {
                this._logger.LogError(exception, "{Method} - Cannot connect", nameof(Connect));
                this.AbortConnect(connection);
                return false;
            }
        }

        /// <summary>
        /// ConnectAsync
        /// </summary>
        /// <param name="ipAddressOrHostname"></param>
        /// <param name="port"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>
        /// true when the connection was established.
        /// false when the client is already connected, another connect is in progress or the connection failed.
        /// </returns>
        /// <exception cref="ObjectDisposedException">The client has been disposed</exception>
        /// <exception cref="OperationCanceledException">The connect was cancelled via <paramref name="cancellationToken"/></exception>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public async Task<bool> ConnectAsync(
            string ipAddressOrHostname,
            int port,
            CancellationToken cancellationToken = default)
        {
            ValidateEndpoint(ipAddressOrHostname, port);
            this.ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            var connection = this.TryBeginConnect();
            if (connection is null)
            {
                this._logger.LogDebug("{Method} - Already connected or a connect is in progress", nameof(ConnectAsync));
                return false;
            }

            try
            {
                this._logger.LogDebug("{Method} - Connecting to {Host}:{Port}", nameof(ConnectAsync), ipAddressOrHostname, port);

#if NET5_0_OR_GREATER

                await connection.Client.ConnectAsync(ipAddressOrHostname, port, cancellationToken).ConfigureAwait(false);

#else

                // There is no cancellable ConnectAsync on this target - closing the socket is the only way to abort it
                using (cancellationToken.Register(static state => ((TcpConnection)state!).Dispose(), connection))
                {
                    await connection.Client.ConnectAsync(ipAddressOrHostname, port).ConfigureAwait(false);
                }

#endif

                return this.CompleteConnect(connection);
            }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested)
            {
                this._logger.LogDebug("{Method} - Cancelled", nameof(ConnectAsync));
                this.AbortConnect(connection);

                if (exception is OperationCanceledException)
                {
                    throw;
                }

                throw new OperationCanceledException("The connect was cancelled", exception, cancellationToken);
            }
            catch (Exception exception)
            {
                this._logger.LogError(exception, "{Method} - Cannot connect", nameof(ConnectAsync));
                this.AbortConnect(connection);
                return false;
            }
        }

        /// <summary>
        /// Connects synchronously with a timeout. The result of the connect operation is always observed via
        /// EndConnect inside the callback, even when the caller stopped waiting, so no exception ends up unobserved.
        /// </summary>
        /// <returns>false when the timeout was reached, otherwise true (check <paramref name="connectException"/>)</returns>
        private static bool TryConnectBlocking(
            System.Net.Sockets.TcpClient tcpClient,
            string ipAddressOrHostname,
            int port,
            int connectTimeoutInMilliseconds,
            out Exception? connectException)
        {
            var completion = new TaskCompletionSource<Exception?>();

            tcpClient.BeginConnect(ipAddressOrHostname, port, asyncResult =>
            {
                try
                {
                    tcpClient.EndConnect(asyncResult);
                    completion.TrySetResult(null);
                }
                catch (Exception exception)
                {
                    // Also the path taken when the socket was closed because the caller gave up waiting
                    completion.TrySetResult(exception);
                }
            }, state: null);

            if (!completion.Task.Wait(connectTimeoutInMilliseconds))
            {
                connectException = null;
                return false;
            }

            connectException = completion.Task.Result;
            return true;
        }

        /// <summary>
        /// Creates the socket and switches to Connecting, or returns null if that is not possible right now
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        private TcpConnection? TryBeginConnect()
        {
            lock (this._stateLock)
            {
                if (this._state == ConnectionState.Disposed)
                {
                    throw new ObjectDisposedException(nameof(TcpClient));
                }

                if (this._state != ConnectionState.Disconnected)
                {
                    return null;
                }

                var connection = new TcpConnection(this.CreateTcpClient(), this._tcpClientConfig.ReceiveBufferSize);

                this._connection = connection;
                this._state = ConnectionState.Connecting;

                return connection;
            }
        }

        /// <summary>
        /// Attaches the stream, starts the receive loop and raises Connected.
        /// Fails when Disconnect or Dispose ran while the socket was connecting.
        /// </summary>
        private bool CompleteConnect(TcpConnection connection)
        {
            // GetStream throws when the socket was closed by Disconnect/Dispose in the meantime - the callers handle that
            var stream = this.PrepareStream(connection.Client);

            if (!this.TryCompleteConnect(connection, stream))
            {
                this._logger.LogDebug("{Method} - Connect was aborted by Disconnect or Dispose", nameof(CompleteConnect));

                connection.Dispose();

                // The stream was never attached to the connection, close it separately
                try
                {
                    stream.Dispose();
                }
                catch (Exception)
                {
                    // The socket is already closed, nothing left to release
                }

                return false;
            }

            this._logger.LogInformation("{Method} - Connected", nameof(CompleteConnect));
            this.RaiseConnected();

            return true;
        }

        private bool TryCompleteConnect(TcpConnection connection, Stream stream)
        {
            lock (this._stateLock)
            {
                if (!ReferenceEquals(this._connection, connection) || this._state != ConnectionState.Connecting)
                {
                    return false;
                }

                connection.Stream = stream;
                connection.ReceiveTask = Task.Run(() => this.ReceiveLoopAsync(connection));

                this._state = ConnectionState.Connected;

                return true;
            }
        }

        /// <summary>
        /// Rolls back a failed connect attempt. A no-op for the state if a newer connection exists
        /// or Disconnect/Dispose already removed this one - the socket is closed in any case.
        /// </summary>
        private void AbortConnect(TcpConnection connection)
        {
            lock (this._stateLock)
            {
                if (ReferenceEquals(this._connection, connection) && this._state == ConnectionState.Connecting)
                {
                    this._connection = null;
                    this._state = ConnectionState.Disconnected;
                }
            }

            connection.Dispose();
        }

        private System.Net.Sockets.TcpClient CreateTcpClient()
        {
            return new System.Net.Sockets.TcpClient
            {
                ReceiveBufferSize = this._tcpClientConfig.ReceiveBufferSize,
                SendBufferSize = this._tcpClientConfig.SendBufferSize,
                NoDelay = this._tcpClientConfig.NoDelay,

                // These two only affect synchronous socket operations.
                // The asynchronous equivalents are implemented in WriteAsync and ReceiveLoopAsync.
                ReceiveTimeout = this._tcpClientConfig.ReceiveTimeout,
                SendTimeout = this._tcpClientConfig.SendTimeout
            };
        }

        private Stream PrepareStream(System.Net.Sockets.TcpClient tcpClient)
        {
            var stream = tcpClient.GetStream();

            if (this._keepAliveConfig != null)
            {
                if (tcpClient.SetKeepAlive(this._keepAliveConfig.KeepAliveTime, this._keepAliveConfig.KeepAliveInterval, this._keepAliveConfig.KeepAliveRetryCount))
                {
                    this._logger.LogTrace("{Method} - KeepAlive configured", nameof(PrepareStream));
                }
                else
                {
                    this._logger.LogWarning("{Method} - Cannot set KeepAlive config", nameof(PrepareStream));
                }
            }

            return stream;
        }

        #endregion

        #region Disconnect

        /// <summary>
        /// Closes the connection and raises <see cref="Disconnected"/> if the client was connected.
        /// A connect that is still in progress is aborted. Safe to call from any event handler.
        /// Does not wait for the receive loop, it stops on its own. A no-op after Dispose.
        /// </summary>
        public void Disconnect()
        {
            TcpConnection? connection;
            bool wasConnected;

            lock (this._stateLock)
            {
                if (this._state == ConnectionState.Disposed)
                {
                    return;
                }

                connection = this._connection;
                wasConnected = this._state == ConnectionState.Connected;

                this._connection = null;
                this._state = ConnectionState.Disconnected;
            }

            if (connection is null)
            {
                this._logger.LogTrace("{Method} - Not connected", nameof(Disconnect));
                return;
            }

            this._logger.LogInformation("{Method} - Disconnecting", nameof(Disconnect));

            // Closing the socket aborts a pending connect or read
            connection.Dispose();

            if (wasConnected)
            {
                this.RaiseDisconnected();
            }
        }

        /// <summary>
        /// Removes the given connection if it is still the current one and raises <see cref="Disconnected"/> exactly once.
        /// Safe to call multiple times and from multiple threads for the same connection.
        /// </summary>
        private void HandleConnectionLost(TcpConnection connection)
        {
            bool raiseDisconnected;

            lock (this._stateLock)
            {
                if (ReferenceEquals(this._connection, connection))
                {
                    raiseDisconnected = this._state == ConnectionState.Connected;

                    this._connection = null;
                    this._state = ConnectionState.Disconnected;
                }
                else
                {
                    // Already replaced by a newer connection or removed by Disconnect/Dispose
                    raiseDisconnected = false;
                }
            }

            connection.Dispose();

            if (raiseDisconnected)
            {
                this._logger.LogInformation("{Method} - Connection closed", nameof(HandleConnectionLost));
                this.RaiseDisconnected();
            }
        }

        #endregion

        #region Send

        /// <summary>
        /// Send data async. Concurrent calls are serialized, the data of one call is never interleaved with another.
        /// When the write fails, times out (<see cref="TcpClientConfig.SendTimeout"/>) or is cancelled,
        /// the connection is closed, because the content of the stream is undefined after an aborted write.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>true if the data was written to the stream, false if the client is not connected or the write failed</returns>
        /// <exception cref="ObjectDisposedException">The client has been disposed</exception>
        /// <exception cref="OperationCanceledException">The send was cancelled via <paramref name="cancellationToken"/></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public async Task<bool> SendAsync(
            byte[] data,
            CancellationToken cancellationToken = default)
        {
            if (data is null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            this.ThrowIfDisposed();

            TcpConnection connection;
            SendResult result;

            await this._sendSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                this.ThrowIfDisposed();

                var current = this._connection;
                var stream = current?.Stream;
                if (current is null || stream is null)
                {
                    this._logger.LogError("{Method} - Not connected", nameof(SendAsync));
                    return false;
                }

                connection = current;
                result = await this.WriteAsync(connection, stream, data, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                this._sendSemaphore.Release();
            }

            if (result == SendResult.Success)
            {
                return true;
            }

            // Raised outside the send semaphore, so a Disconnected handler may call SendAsync again without deadlocking
            this.HandleConnectionLost(connection);

            if (result == SendResult.Cancelled)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return false;
        }

        private async Task<SendResult> WriteAsync(
            TcpConnection connection,
            Stream stream,
            byte[] data,
            CancellationToken cancellationToken)
        {
            // BitConverter.ToString is expensive, only build the string when the level is enabled
            if (this._logger.IsEnabled(LogLevel.Debug))
            {
                this._logger.LogDebug("{Method} - Data:{Data}", nameof(WriteAsync), BitConverter.ToString(data));
            }

            var sendTimeout = this._tcpClientConfig.SendTimeout;

            using var timeoutCts = sendTimeout > 0 ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : null;
            timeoutCts?.CancelAfter(sendTimeout);

            var token = timeoutCts?.Token ?? cancellationToken;

            try
            {
                // Closing the socket is what actually aborts a pending write on every target framework
                // (NetworkStream.WriteAsync on netstandard2.0 ignores the token once the write has started)
                using (token.Register(static state => ((TcpConnection)state!).Dispose(), connection))
                {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER

                    await stream.WriteAsync(data.AsMemory(), token).ConfigureAwait(false);

#else

                    await stream.WriteAsync(data, 0, data.Length, token).ConfigureAwait(false);

#endif

                    await stream.FlushAsync(token).ConfigureAwait(false);
                }

                return SendResult.Success;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                this._logger.LogDebug("{Method} - Cancelled, connection closed", nameof(WriteAsync));
                return SendResult.Cancelled;
            }
            catch (Exception) when (timeoutCts?.IsCancellationRequested == true)
            {
                this._logger.LogError("{Method} - Timeout reached ({Timeout} ms), connection closed", nameof(WriteAsync), sendTimeout);
                return SendResult.Failed;
            }
            catch (ObjectDisposedException)
            {
                this._logger.LogDebug("{Method} - Connection was closed while sending", nameof(WriteAsync));
                return SendResult.Failed;
            }
            catch (Exception exception)
            {
                this._logger.LogError(exception, "{Method} - Write failed, connection closed", nameof(WriteAsync));
                return SendResult.Failed;
            }
        }

        #endregion

        #region Receive

        private async Task ReceiveLoopAsync(TcpConnection connection)
        {
            // Marks this execution context and every handler invoked from it - see Dispose
            InsideReceiveLoop.Value = true;

            var stream = connection.Stream;
            if (stream is null)
            {
                this._logger.LogError("{Method} - Stream is null", nameof(ReceiveLoopAsync));
                this.HandleConnectionLost(connection);
                return;
            }

            var buffer = connection.ReceiveBuffer;
            var receiveTimeout = this._tcpClientConfig.ReceiveTimeout;

            CancellationTokenSource? idleTimeoutCts = null;
            CancellationTokenRegistration idleTimeoutRegistration = default;

            if (receiveTimeout > 0)
            {
                idleTimeoutCts = new CancellationTokenSource();

                // Closing the socket is what actually aborts a pending read on every target framework
                idleTimeoutRegistration = idleTimeoutCts.Token.Register(static state => ((TcpConnection)state!).Dispose(), connection);
            }

            this._logger.LogDebug("{Method} - Started", nameof(ReceiveLoopAsync));

            try
            {
                while (true)
                {
                    int bytesRead;

                    try
                    {
                        idleTimeoutCts?.CancelAfter(receiveTimeout);

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER

                        bytesRead = await stream.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);

#else

                        bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);

#endif

                        // Disarm while the handler runs - a slow handler must not count as idle time
                        idleTimeoutCts?.CancelAfter(Timeout.Infinite);
                    }
                    catch (Exception) when (idleTimeoutCts?.IsCancellationRequested == true)
                    {
                        this._logger.LogWarning("{Method} - No data received for {ReceiveTimeout} ms, connection closed", nameof(ReceiveLoopAsync), receiveTimeout);
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        // Closed locally by Disconnect, Dispose or a failed send
                        this._logger.LogDebug("{Method} - Connection closed", nameof(ReceiveLoopAsync));
                        break;
                    }
                    catch (IOException exception) when (IsExpectedDisconnect(exception))
                    {
                        this._logger.LogDebug(exception, "{Method} - Connection lost", nameof(ReceiveLoopAsync));
                        break;
                    }
                    catch (Exception exception)
                    {
                        this._logger.LogWarning(exception, "{Method} - Read failed", nameof(ReceiveLoopAsync));
                        break;
                    }

                    if (bytesRead == 0)
                    {
                        this._logger.LogInformation("{Method} - Remote endpoint closed the connection", nameof(ReceiveLoopAsync));
                        break;
                    }

                    this._logger.LogTrace("{Method} - BytesRead:{BytesRead}", nameof(ReceiveLoopAsync), bytesRead);

                    // Every subscriber gets its own copy, the receive buffer is reused for the next read
                    var data = new byte[bytesRead];
                    Buffer.BlockCopy(buffer, 0, data, 0, bytesRead);

                    this.RaiseDataReceived(data);
                }
            }
            finally
            {
                idleTimeoutRegistration.Dispose();
                idleTimeoutCts?.Dispose();

                // No-op when Disconnect/Dispose already removed this connection - Disconnected is raised exactly once
                this.HandleConnectionLost(connection);

                this._logger.LogDebug("{Method} - Stopped", nameof(ReceiveLoopAsync));
            }
        }

        /// <summary>
        /// Socket errors that simply mean "the connection is gone" and deserve no warning
        /// </summary>
        private static bool IsExpectedDisconnect(IOException exception)
        {
            if (exception.InnerException is not SocketException socketException)
            {
                return false;
            }

            switch (socketException.SocketErrorCode)
            {
                case SocketError.ConnectionReset:   // Remote endpoint sent RST (e.g. restarted)
                case SocketError.ConnectionAborted:
                case SocketError.OperationAborted:  // Socket closed locally while the read was pending
                case SocketError.TimedOut:          // KeepAlive detected a dead peer / unplugged cable
                case SocketError.Shutdown:
                case SocketError.Interrupted:
                    return true;

                default:
                    return false;
            }
        }

        #endregion

        #region Events

        private void RaiseConnected()
        {
            this.RaiseEvent(this.Connected, nameof(Connected), static handler => handler());
        }

        private void RaiseDisconnected()
        {
            this.RaiseEvent(this.Disconnected, nameof(Disconnected), static handler => handler());
        }

        private void RaiseDataReceived(byte[] data)
        {
            this.RaiseEvent(this.DataReceived, nameof(DataReceived), handler => handler(data));
        }

        /// <summary>
        /// Invokes every subscriber of the event separately. A throwing handler is logged and
        /// does not prevent the handlers registered after it from running.
        /// </summary>
        private void RaiseEvent<TDelegate>(
            TDelegate? multicast,
            string eventName,
            Action<TDelegate> invoke)
            where TDelegate : Delegate
        {
            if (multicast is null)
            {
                this._logger.LogTrace("{Method} - No one has subscribed to {Event}", nameof(RaiseEvent), eventName);
                return;
            }

            foreach (var handler in multicast.GetInvocationList())
            {
                try
                {
                    invoke((TDelegate)handler);
                }
                catch (Exception exception)
                {
                    // A faulty subscriber must neither tear down the connection nor silence the other subscribers
                    this._logger.LogError(exception, "{Method} - Handler of {Event} failed", nameof(RaiseEvent), eventName);
                }
            }
        }

        #endregion

        #region Dispose

        /// <summary>
        /// Closes the connection without raising <see cref="Disconnected"/> and releases all resources.
        /// Waits briefly for the receive loop to stop, unless called from one of its handlers.
        /// </summary>
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
            if (!disposing)
            {
                return;
            }

            var receiveTask = this.DisposeCore();
            if (receiveTask is null || receiveTask.IsCompleted)
            {
                return;
            }

            if (InsideReceiveLoop.Value)
            {
                // Called from a DataReceived/Disconnected handler - waiting would wait for ourselves
                this._logger.LogTrace("{Method} - Called from the receive loop, not waiting for it", nameof(Dispose));
                return;
            }

            try
            {
                if (!receiveTask.Wait(DisposeWaitTimeout))
                {
                    this._logger.LogWarning("{Method} - Receive loop did not stop within {Timeout}", nameof(Dispose), DisposeWaitTimeout);
                }
            }
            catch (AggregateException)
            {
                // The receive loop never throws, but never let Dispose fail
            }
        }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER

        /// <summary>
        /// Closes the connection without raising <see cref="Disconnected"/> and releases all resources.
        /// Waits briefly for the receive loop to stop, unless called from one of its handlers.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            var receiveTask = this.DisposeCore();
            if (receiveTask != null && !receiveTask.IsCompleted && !InsideReceiveLoop.Value)
            {
                var completed = await Task.WhenAny(receiveTask, Task.Delay(DisposeWaitTimeout)).ConfigureAwait(false);
                if (completed != receiveTask)
                {
                    this._logger.LogWarning("{Method} - Receive loop did not stop within {Timeout}", nameof(DisposeAsync), DisposeWaitTimeout);
                }
            }

            this.Dispose(disposing: false);
            GC.SuppressFinalize(this);
        }

#endif

        /// <summary>
        /// Marks the client as disposed and closes the current connection. Idempotent.
        /// </summary>
        /// <returns>The receive loop of the closed connection, if there was one</returns>
        private Task? DisposeCore()
        {
            if (Interlocked.Exchange(ref this._disposed, 1) == 1)
            {
                return null;
            }

            this._logger.LogTrace("{Method} - Start", nameof(DisposeCore));

            TcpConnection? connection;

            lock (this._stateLock)
            {
                connection = this._connection;

                this._connection = null;
                this._state = ConnectionState.Disposed;
            }

            if (connection is null)
            {
                return null;
            }

            // Closing the socket aborts a pending connect or read, the receive loop then stops on its own
            connection.Dispose();

            return connection.ReceiveTask;
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref this._disposed) == 1)
            {
                throw new ObjectDisposedException(nameof(TcpClient));
            }
        }

        #endregion

        #region Validation

        /// <summary>
        /// Validates everything that is later handed to the socket, so an invalid configuration
        /// fails in the constructor and not in the middle of a connect
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static void ValidateConfig(TcpClientConfig clientConfig)
        {
            if (clientConfig.ReceiveBufferSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(clientConfig), clientConfig.ReceiveBufferSize, $"{nameof(clientConfig.ReceiveBufferSize)} must be greater than 0");
            }

            if (clientConfig.SendBufferSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(clientConfig), clientConfig.SendBufferSize, $"{nameof(clientConfig.SendBufferSize)} must be greater than or equal to 0");
            }

            if (!IsValidTimeout(clientConfig.SendTimeout))
            {
                throw new ArgumentOutOfRangeException(nameof(clientConfig), clientConfig.SendTimeout, $"{nameof(clientConfig.SendTimeout)} must be greater than or equal to 0, or Timeout.Infinite");
            }

            if (!IsValidTimeout(clientConfig.ReceiveTimeout))
            {
                throw new ArgumentOutOfRangeException(nameof(clientConfig), clientConfig.ReceiveTimeout, $"{nameof(clientConfig.ReceiveTimeout)} must be greater than or equal to 0, or Timeout.Infinite");
            }
        }

        /// <summary>
        /// 0 and <see cref="Timeout.Infinite"/> disable a timeout, every positive value is a duration in milliseconds
        /// </summary>
        private static bool IsValidTimeout(int timeoutInMilliseconds)
        {
            return timeoutInMilliseconds >= 0 || timeoutInMilliseconds == Timeout.Infinite;
        }

        private static void ValidateEndpoint(string ipAddressOrHostname, int port)
        {
            if (ipAddressOrHostname is null)
            {
                throw new ArgumentNullException(nameof(ipAddressOrHostname));
            }

            if (string.IsNullOrWhiteSpace(ipAddressOrHostname))
            {
                throw new ArgumentException("Value cannot be empty", nameof(ipAddressOrHostname));
            }

            if (port < 1 || port > IPEndPoint.MaxPort)
            {
                throw new ArgumentOutOfRangeException(nameof(port), port, $"Port must be between 1 and {IPEndPoint.MaxPort}");
            }
        }

        #endregion
    }
}
