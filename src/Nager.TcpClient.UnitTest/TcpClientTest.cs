using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nager.TcpClient.UnitTest
{
    [TestClass]
    public class TcpClientTest
    {
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
            }

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();

            using var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);
            tcpClient.DataReceived += OnDataReceived;
            tcpClient.Connect(ipAddress, port, 1000);
            await tcpClient.SendAsync(data);
            await Task.Delay(400);
            tcpClient.Disconnect();
            tcpClient.DataReceived -= OnDataReceived;

            Assert.IsTrue(isDataReceived);
            Assert.IsTrue(isReceivedDataValid);
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

            Assert.IsTrue(isDataReceived);
            Assert.IsTrue(isReceivedDataValid);
        }
    }
}