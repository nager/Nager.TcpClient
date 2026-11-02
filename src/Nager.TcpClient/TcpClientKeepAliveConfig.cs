namespace Nager.TcpClient
{
    /// <summary>
    /// TcpClient KeepAlive Config
    /// </summary>
    public class TcpClientKeepAliveConfig
    {
        /// <summary>
        /// The interval in seconds between keep-alive probes when no response is received
        /// </summary>
        public int KeepAliveInterval = 2;

        /// <summary>
        /// The idle time in seconds before the first keep-alive probe is sent
        /// </summary>
        public int KeepAliveTime = 2;

        /// <summary>
        /// The number of keep-alive probes before the connection is terminated
        /// (not supported on .NET Standard, Windows uses a fixed value of 10)
        /// </summary>
        public int KeepAliveRetryCount = 3;
    }
}
