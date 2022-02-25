using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nager.TcpClient.UnitTest
{
    [TestClass]
    public class ConcurrentSendReceiveTcpClientTest
    {
        private string CreateTestData(char value, int testDataLength)
        {
            return new string(value, testDataLength);
        }

        private IEnumerable<byte[]> SplitByteArray(IEnumerable<byte> source, byte marker)
        {
            var current = new List<byte>();

            foreach (byte b in source)
            {
                if (b == marker)
                {
                    if (current.Count > 0)
                    {
                        yield return current.ToArray();
                    }

                    current.Clear();
                    continue;
                }

                current.Add(b);
            }

            if (current.Count > 0)
            {
                yield return current.ToArray();
            }
        }

        [TestMethod]
        public async Task SendAndReceiveData_Successful()
        {
            var ipAddress = "tcpbin.com";
            var port = 4242;

            var isDataReceived = false;

            var testChars = new char[] { 'A', 'b', 'C', 'D', 'E', '0', '1', 'X', 'Y', 'z' };
            var testDataLength = 2000;
            var receiveBuffer = new List<byte>();

            var clientConfig = new TcpClientConfig
            {
                ReceiveBufferSize = testDataLength / 7
            };

            void OnDataReceived(byte[] receivedData)
            {
                receiveBuffer.AddRange(receivedData);

                isDataReceived = true;
                Trace.WriteLine($"ReceivedData: {BitConverter.ToString(receivedData)}");
            }

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();

            using var tcpClient = new TcpClient(clientConfig: clientConfig, logger: mockLoggerTcpClient.Object);
            tcpClient.DataReceived += OnDataReceived;
            tcpClient.Connect(ipAddress, port, 1000);

            var tasks = new List<Task>();
            var barrier = new Barrier(testChars.Length);

            for (var i = 0; i < testChars.Length; i++)
            {
                var testChar = testChars[i];

                tasks.Add(Task.Run(async () =>
                {
                    var data = Encoding.UTF8.GetBytes($"{this.CreateTestData(testChar, testDataLength)}\n");

                    Trace.WriteLine($"Wait all tasks are ready {testChar}");
                    barrier.SignalAndWait();

                    Trace.WriteLine($"SendAsync {testChar}");
                    await tcpClient.SendAsync(data);
                }));
            }

            await Task.WhenAll(tasks);

            //Wait for response of echo server
            await Task.Delay(1000);

            tcpClient.Disconnect();
            tcpClient.DataReceived -= OnDataReceived;

            Assert.IsTrue(isDataReceived, "No data received");

            /*
             * Verify that all data packets have arrived as a whole unit 
             * and that no fragmentation has occurred due to parallel transmission
            */

            var parts = this.SplitByteArray(receiveBuffer, 0x0A);
            Assert.AreEqual(parts.Count(), testChars.Length, "Not all packages received");
            foreach (var part in parts)
            {
                Assert.AreEqual(part.Length, testDataLength);
            }
        }
    }
}