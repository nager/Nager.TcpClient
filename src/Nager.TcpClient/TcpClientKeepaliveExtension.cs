using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;

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
        /// <param name="tcpKeepAliveTime">The idle time in seconds before the first keep-alive probe is sent</param>
        /// <param name="tcpKeepAliveInterval">The interval in seconds between keep-alive probes when no response is received</param>
        /// <param name="tcpKeepAliveRetryCount">
        /// The number of keep-alive probes that will be sent before the connection is terminated.
        /// Not supported on .NET Standard (Windows uses a fixed value of 10).
        /// </param>
        /// <returns>
        /// <c>true</c> if the keep-alive settings could be applied completely.
        /// On .NET Standard under a non-Windows operating system, keep-alive is enabled with
        /// the operating system default timings and <c>false</c> is returned.
        /// </returns>
        public static bool SetKeepAlive(
            this System.Net.Sockets.TcpClient tcpClient,
            int tcpKeepAliveTime = 2,
            int tcpKeepAliveInterval = 2,
            int tcpKeepAliveRetryCount = 5)
        {
            if (tcpKeepAliveTime <= 0 || tcpKeepAliveInterval <= 0 || tcpKeepAliveRetryCount <= 0)
            {
                return false;
            }

            try
            {
                var socket = tcpClient.Client;
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

#if NET5_0_OR_GREATER
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, tcpKeepAliveTime);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, tcpKeepAliveInterval);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, tcpKeepAliveRetryCount);

                return true;
#else
                // SIO_KEEPALIVE_VALS is only available on Windows
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return false;
                }

                // struct tcp_keepalive { u_long onoff; u_long keepalivetime; u_long keepaliveinterval; } (milliseconds)
                var config = new byte[12];
                BitConverter.GetBytes(1u).CopyTo(config, 0);
                BitConverter.GetBytes(checked((uint)tcpKeepAliveTime * 1000u)).CopyTo(config, 4);
                BitConverter.GetBytes(checked((uint)tcpKeepAliveInterval * 1000u)).CopyTo(config, 8);

                socket.IOControl(IOControlCode.KeepAliveValues, config, null);

                return true;
#endif
            }
            catch (Exception)
            { }

            return false;
        }
    }
}
