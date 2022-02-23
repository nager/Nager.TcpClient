using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nager.TcpClient.UnitTest
{
    [TestClass]
    public class DisposeTcpClientTest
    {
        [TestMethod]
        public void Dispose_FullFlow_Successful()
        {
            var ipAddress = "tcpbin.com";
            var port = 4242;

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();

            var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);
            tcpClient.Connect(ipAddress, port, connectionTimeoutInMilliseconds: 1000);
            tcpClient.Disconnect();
            tcpClient.Dispose();
        }

        [TestMethod]
        public void Dispose_DoubleDispose_Successful()
        {
            var ipAddress = "tcpbin.com";
            var port = 4242;

            var mockLoggerTcpClient = LoggerHelper.GetLogger<TcpClient>();

            var tcpClient = new TcpClient(logger: mockLoggerTcpClient.Object);
            tcpClient.Connect(ipAddress, port, connectionTimeoutInMilliseconds: 1000);

            tcpClient.Disconnect();
            tcpClient.Disconnect();

            tcpClient.Dispose();
            tcpClient.Dispose();
        }
    }
}