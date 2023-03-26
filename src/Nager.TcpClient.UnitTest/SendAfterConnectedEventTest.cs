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
        private async void SendMessage(TcpClient tcpClient)
        {
            var data = Encoding.UTF8.GetBytes("ping\n");
            await tcpClient.SendAsync(data);
        }

        [TestMethod]
        public void SendAfterConnect_Successful()
        {
            var ipAddress = "tcpbin.com";
            var port = 4242;

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();
            using var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);
            tcpClient.Connected += () => SendMessage(tcpClient);
            tcpClient.Connect(ipAddress, port, 1000);

            mockLoggerTcpClient.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString().Contains("SendAsync")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [TestMethod]
        public async Task SendAfterConnectAsync_Successful()
        {
            var ipAddress = "tcpbin.com";
            var port = 4242;

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();
            using var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);
            tcpClient.Connected += () => SendMessage(tcpClient);
            await tcpClient.ConnectAsync(ipAddress, port);

            mockLoggerTcpClient.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString().Contains("SendAsync")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }
}
