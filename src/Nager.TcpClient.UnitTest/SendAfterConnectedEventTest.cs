using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Text;
using System.Threading.Tasks;

namespace Nager.TcpClient.UnitTest
{
    [TestClass]
    public class SendAfterConnectedEventTest
    {
        private static Task<bool> SendMessageAsync(TcpClient tcpClient)
        {
            var data = Encoding.UTF8.GetBytes("ping\n");
            return tcpClient.SendAsync(data);
        }

        [TestMethod]
        public async Task SendAfterConnect_Successful()
        {
            var ipAddress = "tcpbin.com";
            var port = 4242;

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();
            using var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);

            Task<bool>? sendTask = null;
            tcpClient.Connected += () => sendTask = SendMessageAsync(tcpClient);

            var connected = tcpClient.Connect(ipAddress, port, 1000);
            Assert.IsTrue(connected, "Connect failed");
            Assert.IsNotNull(sendTask, "Connected event was not raised");

            var sent = await sendTask;
            Assert.IsTrue(sent, "SendAsync failed");

            VerifySendDataLogged(mockLoggerTcpClient);
        }

        [TestMethod]
        public async Task SendAfterConnectAsync_Successful()
        {
            var ipAddress = "tcpbin.com";
            var port = 4242;

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();
            using var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);

            Task<bool>? sendTask = null;
            tcpClient.Connected += () => sendTask = SendMessageAsync(tcpClient);

            var connected = await tcpClient.ConnectAsync(ipAddress, port);
            Assert.IsTrue(connected, "ConnectAsync failed");
            Assert.IsNotNull(sendTask, "Connected event was not raised");

            var sent = await sendTask;
            Assert.IsTrue(sent, "SendAsync failed");

            VerifySendDataLogged(mockLoggerTcpClient);
        }

        private static void VerifySendDataLogged(Mock<ILogger<TcpClient>> mockLogger)
        {
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => (o.ToString() ?? string.Empty).Contains("Data:")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }
}
