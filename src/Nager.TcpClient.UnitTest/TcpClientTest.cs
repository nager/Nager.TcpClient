using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nager.TcpClient.UnitTest
{
    [TestClass]
    public class TcpClientTest
    {
        [TestMethod]
        public void CheckFullDisposeFlow_Successful()
        {
            var ipAddress = "tcpbin.com";
            var port = 4242;

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();

            var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);
            tcpClient.Connect(ipAddress, port, 1000);
            tcpClient.Disconnect();
            tcpClient.Dispose();
        }

        [TestMethod]
        public void CheckDoubleDisposeFlow_Successful()
        {
            var ipAddress = "tcpbin.com";
            var port = 4242;

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();

            var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);
            tcpClient.Connect(ipAddress, port, 1000);

            tcpClient.Disconnect();
            tcpClient.Disconnect();

            tcpClient.Dispose();
            tcpClient.Dispose();
        }

        [TestMethod]
        public void CheckKeepAliveConfig_Successful()
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
        public async Task CheckConnectAsync_Successful()
        {
            var ipAddress = "tcpbin.com";
            var port = 4242;

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();

            var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);
            await tcpClient.ConnectAsync(ipAddress, port);
            tcpClient.Disconnect();
            tcpClient.Dispose();
        }

#if NET5_0_OR_GREATER

        [TestMethod]
        public async Task CheckConnectAsyncWithCancellationToken_Successful()
        {
            var ipAddress = "tcpbin.com";
            var port = 4242;

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();

            using var cancellationTokenSource = new CancellationTokenSource();

            var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);
            await tcpClient.ConnectAsync(ipAddress, port, cancellationTokenSource.Token);
            tcpClient.Disconnect();
            tcpClient.Dispose();
        }

#endif

        [TestMethod]
        public async Task CheckSendAndReceiveWork_Successful()
        {
            var ipAddress = "tcpbin.com";
            var port = 4242;

            var isDataReceived = false;
            var isReceivedDataValid = false;

            var data = Encoding.UTF8.GetBytes("ping\n");

            void OnDataReceived(byte[] receivedData)
            {
                isDataReceived = true;
                isReceivedDataValid = Enumerable.SequenceEqual(receivedData, data);
                Trace.WriteLine($"ReceivedData: {BitConverter.ToString(receivedData)}");
                if (!isReceivedDataValid)
                {
                    Trace.WriteLine($"ExpectedData: {BitConverter.ToString(data)}");
                }
            }

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();

            using var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);
            tcpClient.DataReceived += OnDataReceived;
            tcpClient.Connect(ipAddress, port, 1000);
            await tcpClient.SendAsync(data);
            await Task.Delay(400);
            tcpClient.Disconnect();
            tcpClient.DataReceived -= OnDataReceived;

            Assert.IsTrue(isDataReceived, "No data received");
            Assert.IsTrue(isReceivedDataValid, "Invalid data received");
        }

        [TestMethod]
        public async Task CheckSendAndReceiveWorkWithReconnect_Successful()
        {
            var ipAddress = "tcpbin.com";
            var port = 4242;

            var isDataReceived = false;
            var isReceivedDataValid = false;

            var data = Encoding.UTF8.GetBytes("ping\n");

            void OnDataReceived(byte[] receivedData)
            {
                isDataReceived = true;
                isReceivedDataValid = Enumerable.SequenceEqual(receivedData, data);
                Trace.WriteLine(BitConverter.ToString(receivedData));
            }

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();

            using var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);
            tcpClient.DataReceived += OnDataReceived;
            tcpClient.Connect(ipAddress, port, 1000);

            tcpClient.Disconnect();
            tcpClient.Connect(ipAddress, port, 1000);

            await tcpClient.SendAsync(data);
            await Task.Delay(400);
            tcpClient.Disconnect();
            tcpClient.DataReceived -= OnDataReceived;

            Assert.IsTrue(isDataReceived, "No data received");
            Assert.IsTrue(isReceivedDataValid, "Invalid data received");
        }
    }
}