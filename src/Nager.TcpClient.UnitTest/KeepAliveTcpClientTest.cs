using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nager.TcpClient.UnitTest
{
    [TestClass]
    public class KeepAliveTcpClientTest
    {
        [TestMethod]
        public void KeepAliveConfig_Initialize_Successful()
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
    }
}