#if (!NET5_0_OR_GREATER)

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;
using System.Threading.Tasks;

namespace Nager.TcpClient.UnitTest
{
    [TestClass]
    public class ConnectAsyncTcpClientTest
    {
        [TestMethod]
        public async Task ConnectAsync_WebTestService_Successful()
        {
            var ipAddress = "tcpbin.com";
            var port = 4242;

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();

            using var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);
            var connectSuccessful = await tcpClient.ConnectAsync(ipAddress, port);

            Assert.IsTrue(connectSuccessful, "Cannot connect");
            Assert.IsTrue(tcpClient.IsConnected, "IsConnected has wrong state");
        }

        [TestMethod]
        public async Task ConnectAsync_NonRoutableIpAddress_Failure()
        {
            var ipAddress = "10.255.255.1";
            var port = 4242;

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();

            using var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);
            var connectSuccessful = await tcpClient.ConnectAsync(ipAddress, port);

            Assert.IsFalse(connectSuccessful, "Connection should not be possible");
            Assert.IsFalse(tcpClient.IsConnected, "IsConnected has wrong state");
        }

        [TestMethod]
        public async Task ConnectAsync_NonRoutableIpAddressWithCancellationTokenTimeout_Failure()
        {
            var ipAddress = "10.255.255.1";
            var port = 4242;

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();

            using var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);

            using var cancellationTokenSource = new CancellationTokenSource(millisecondsDelay: 1000);
            var connectSuccessful = await tcpClient.ConnectAsync(ipAddress, port, cancellationTokenSource.Token);

            Assert.IsFalse(connectSuccessful, "Connection should not be possible");
            Assert.IsFalse(tcpClient.IsConnected, "IsConnected has wrong state");
        }
    }
}

#endif
