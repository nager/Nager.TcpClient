using System;
using System.Net.Sockets;

namespace Nager.TcpClient
{
    /// <summary>
    /// TcpClient KeepAlive Extension
    /// </summary>
    public static class TcpClientKeepAliveExtension
    {
        /// <summary>
        /// SetKeepAlive
        /// </summary>
        /// <param name="tcpClient"></param>
        /// <param name="tcpKeepAliveTime">Specifies how often TCP sends keep-alive transmissions (milliseconds)</param>
        /// <param name="tcpKeepAliveInterval">Specifies how often TCP repeats keep-alive transmissions when no response is received</param>
        /// <param name="tcpKeepAliveRetryCount">The number of TCP keep alive probes that will be sent before the connection is terminated</param>
        /// <returns></returns>
        public static bool SetKeepAlive(
            this System.Net.Sockets.TcpClient tcpClient,
            int tcpKeepAliveTime = 2,
            int tcpKeepAliveInterval = 2,
            int tcpKeepAliveRetryCount = 5)
        {
            try
            {
#if (NET5_0_OR_GREATER)

                tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, tcpKeepAliveTime);
                tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, tcpKeepAliveInterval);
                tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, tcpKeepAliveRetryCount);

                return true;
#elif NETSTANDARD2_0

    var socket = tcpClient.Client;

    socket.SetSocketOption(
        SocketOptionLevel.Socket,
        SocketOptionName.KeepAlive,
        true);

    byte[] config = new byte[12];

    BitConverter.GetBytes((uint)1).CopyTo(config, 0);

    BitConverter.GetBytes(
        checked((uint)tcpKeepAliveTime * 1000u))
        .CopyTo(config, 4);

    BitConverter.GetBytes(
        checked((uint)tcpKeepAliveInterval * 1000u))
        .CopyTo(config, 8);

    socket.IOControl(
        IOControlCode.KeepAliveValues,
        config,
        null);

    return true;
#endif
            }
            catch (Exception)
            { }

            return false;
        }
    }
}
