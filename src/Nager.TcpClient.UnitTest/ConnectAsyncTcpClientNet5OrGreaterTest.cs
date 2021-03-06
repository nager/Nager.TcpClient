#if NET5_0_OR_GREATER

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Nager.TcpClient.UnitTest
{
    [TestClass]
    public class ConnectAsyncTcpClientNet5OrGreaterTest
    {
        [TestMethod]
        public async Task ConnectAsync_WebTestService_Successful()
        {
            var ipAddress = "tcpbin.com";
            var port = 4242;

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();

            using var cancellationTokenSource = new CancellationTokenSource();

            using var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);
            var connectSuccessful = await tcpClient.ConnectAsync(ipAddress, port, cancellationTokenSource.Token);

            Assert.IsTrue(connectSuccessful, "Cannot connect");
            Assert.IsTrue(tcpClient.IsConnected, "IsConnected has wrong state");
        }

        [TestMethod]
        public async Task ConnectAsync_NonRoutableIpAddress_Failure()
        {
            var ipAddress = "10.255.255.1";
            var port = 4242;
            var connectionTimeoutMilliseconds = 3000;

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();

            var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);

            var sw = new Stopwatch();
            sw.Start();
            using var cancellationTokenSource = new CancellationTokenSource(connectionTimeoutMilliseconds);
            var connectSuccessful = await tcpClient.ConnectAsync(ipAddress, port, cancellationTokenSource.Token);
            sw.Stop();

            var allowedTolerance = 100; //100ms
            Assert.IsTrue(sw.Elapsed.TotalMilliseconds > (connectionTimeoutMilliseconds - allowedTolerance), $"Abort to early {sw.Elapsed.TotalMilliseconds}");
            Assert.IsFalse(connectSuccessful, "Connection should not be possible");
            Assert.IsFalse(tcpClient.IsConnected, "IsConnected has wrong state");
        }

        [TestMethod]
        public async Task ConnectAsync_InvalidDestination_Failure()
        {
            var ipAddress = "127.0.0.1";
            var port = 12345;

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();

            using var cancellationTokenSource = new CancellationTokenSource();

            var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);
            var connectSuccessful = await tcpClient.ConnectAsync(ipAddress, port, cancellationTokenSource.Token);

            Assert.IsFalse(connectSuccessful, "Connection should not be possible");
            Assert.IsFalse(tcpClient.IsConnected, "IsConnected has wrong state");
        }
    }
}

#endif
