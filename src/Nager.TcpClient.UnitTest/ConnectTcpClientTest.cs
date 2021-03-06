using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;

namespace Nager.TcpClient.UnitTest
{
    [TestClass]
    public class ConnectTcpClientTest
    {
        [TestMethod]
        public void Connect_WebTestService_Successful()
        {
            var ipAddress = "tcpbin.com";
            var port = 4242;

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();

            using var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);
            var connectSuccessful = tcpClient.Connect(ipAddress, port);

            Assert.IsTrue(connectSuccessful, "Cannot connect");
            Assert.IsTrue(tcpClient.IsConnected, "IsConnected has wrong state");
        }

        [TestMethod]
        public void Connect_NonRoutableIpAddress_Failure()
        {
            var ipAddress = "10.255.255.1";
            var port = 4242;
            var connectionTimeoutMilliseconds = 3000;

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();

            using var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);

            var sw = new Stopwatch();
            sw.Start();
            var connectSuccessful = tcpClient.Connect(ipAddress, port, connectionTimeoutMilliseconds);
            sw.Stop();

            var allowedTolerance = 100; //100ms
            Assert.IsTrue(sw.Elapsed.TotalMilliseconds > (connectionTimeoutMilliseconds - allowedTolerance), $"Abort to early {sw.Elapsed.TotalMilliseconds}");
            Assert.IsFalse(connectSuccessful, "Connection should not be possible");
            Assert.IsFalse(tcpClient.IsConnected);
        }
    }
}