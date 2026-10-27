using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nager.TcpClient
{
    /// <summary>
    /// Everything that belongs to one connection attempt. Disposing it closes the socket,
    /// which aborts a pending connect, read or write on every target framework.
    /// </summary>
    internal sealed class TcpConnection
    {
        private volatile Stream? _stream;
        private int _disposed;

        public TcpConnection(System.Net.Sockets.TcpClient client, int receiveBufferSize)
        {
            this.Client = client;
            this.ReceiveBuffer = new byte[receiveBufferSize];
        }

        public System.Net.Sockets.TcpClient Client { get; }

        /// <summary>
        /// Private buffer of this connection - a late read of an old connection can never corrupt a new one
        /// </summary>
        public byte[] ReceiveBuffer { get; }

        /// <summary>
        /// Set (under the state lock) once the connection is established
        /// </summary>
        public Stream? Stream
        {
            get => this._stream;
            set => this._stream = value;
        }

        /// <summary>
        /// Set (under the state lock) together with <see cref="Stream"/>
        /// </summary>
        public Task? ReceiveTask { get; set; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this._disposed, 1) == 1)
            {
                return;
            }

            var stream = this._stream;
            if (stream != null)
            {
                try
                {
                    stream.Dispose();
                }
                catch (Exception)
                {
                    // Closing a dead stream may throw, nothing left to release
                }
            }

            try
            {
                // Close() exists on every target framework and disposes the client internally
                this.Client.Close();
            }
            catch (Exception)
            {
                // Already closed
            }
        }
    }
}
