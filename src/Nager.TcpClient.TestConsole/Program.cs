using Microsoft.Extensions.Logging;
using Nager.TcpClient;

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Trace);
    builder.AddConsole();
});

var logger = loggerFactory.CreateLogger<TcpClient>();


var tcpClient = new TcpClient(logger: logger, keepAliveConfig: new TcpClientKeepAliveConfig());
tcpClient.Disconnected += () => Console.WriteLine("Disconnected");
if (!await tcpClient.ConnectAsync("tcpbin.com", 4242))
{
    Console.WriteLine("Cannot connect");
    return;
}
Console.WriteLine("Connected");

Console.WriteLine("Press any key for quit");
Console.ReadLine();

tcpClient.Disconnect();
tcpClient.Dispose();