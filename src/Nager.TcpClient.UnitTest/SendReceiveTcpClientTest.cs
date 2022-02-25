using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nager.TcpClient.UnitTest
{
    [TestClass]
    public class SendReceiveTcpClientTest
    {
        [TestMethod]
        public async Task SendAndReceiveData_Successful()
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
        public async Task SendAndReceiveDataWithSmallReceiveBuffer_Successful()
        {
            var ipAddress = "tcpbin.com";
            var port = 4242;

            var config = new TcpClientConfig
            {
                ReceiveBufferSize = 2
            };

            var data = Encoding.UTF8.GetBytes("ping\n");
            var receiveBuffer = new List<byte>();

            void OnDataReceived(byte[] receivedData)
            {
                receiveBuffer.AddRange(receivedData);
            }

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();

            using var tcpClient = new TcpClient(config, logger: mockLoggerTcpClient.Object);
            tcpClient.DataReceived += OnDataReceived;
            tcpClient.Connect(ipAddress, port, 1000);
            await tcpClient.SendAsync(data);
            await Task.Delay(400);
            tcpClient.Disconnect();
            tcpClient.DataReceived -= OnDataReceived;

            var isReceivedDataValid = Enumerable.SequenceEqual(receiveBuffer, data);
            Assert.IsTrue(isReceivedDataValid, "Invalid data received");
        }

        [TestMethod]
        public async Task SendAndReceiveData_WithSecondConnect_Successful()
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