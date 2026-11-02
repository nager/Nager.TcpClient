using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Sockets;

namespace Nager.TcpClient.UnitTest
{
    [TestClass]
    public class KeepAliveTcpClientTest
    {
        [TestMethod]
        public void KeepAliveConfig_Initialize_Successful()
        {
            var ipAddress = "tcpbin.com";
            var port = 4242;

            var keepAliveConfig = new TcpClientKeepAliveConfig();

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();

            var tcpClient = new TcpClient(
                keepAliveConfig: keepAliveConfig,
                logger: mockLoggerTcpClient.Object);

            tcpClient.Connect(ipAddress, port, 1000);

            tcpClient.Disconnect();
            tcpClient.Dispose();
        }

        [TestMethod]
        public void SetKeepAlive_ValidValues_ReturnsTrue()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;

                using var tcpClient = new System.Net.Sockets.TcpClient();
                tcpClient.Connect(IPAddress.Loopback, port);

                var result = tcpClient.SetKeepAlive(tcpKeepAliveTime: 5, tcpKeepAliveInterval: 2, tcpKeepAliveRetryCount: 3);

                Assert.IsTrue(result);
                Assert.AreEqual(1, (int)tcpClient.Client.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive)!);
            }
            finally
            {
                listener.Stop();
            }
        }

        [TestMethod]
        [DataRow(0, 2, 3)]
        [DataRow(-1, 2, 3)]
        [DataRow(2, 0, 3)]
        [DataRow(2, 2, 0)]
        public void SetKeepAlive_InvalidValues_ReturnsFalse(int time, int interval, int retryCount)
        {
            using var tcpClient = new System.Net.Sockets.TcpClient();

            var result = tcpClient.SetKeepAlive(time, interval, retryCount);

            Assert.IsFalse(result);
        }
    }
}