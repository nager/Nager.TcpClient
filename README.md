# Nager.TcpClient

<img src="https://raw.githubusercontent.com/nager/Nager.TcpClient/main/doc/icon.png" width="150" title="Portalum Zvt Client" alt="Portalum Zvt Client" align="left">

An easy **TcpClient** with async Connect logic. Received data packets are available via an event `DataReceived`. There are also events for `Connected` and `Disconnected`. Additionally there is the possibility to activate a Tcp KeepAlive. The library offers the possibility to pass an `ILogger` for logging.

<br>
<br>

## How can I use it?

The package is available on [nuget](https://www.nuget.org/packages/Nager.TcpClient)
```
PM> install-package Nager.TcpClient
```

## Examples of use

### Connect with a timeout of 1000ms
Connect to an online echo service `tcpbin.com` who sends back all packages
```cs
void OnDataReceived(byte[] receivedData)
{
}

using var tcpClient = new TcpClient();
tcpClient.DataReceived += OnDataReceived;
tcpClient.Connect("tcpbin.com", 4242, 1000);
await tcpClient.SendAsync(new byte[] { 0x01, 0x0A });
await Task.Delay(400);
tcpClient.Disconnect();
tcpClient.DataReceived -= OnDataReceived;
```

### ConnectAsync with a timeout of 1000ms (Available from .NET 5 and higher)
Connect to an online echo service `tcpbin.com` who sends back all packages
```cs
void OnDataReceived(byte[] receivedData)
{
}

using var cancellationTokenSource = new CancellationTokenSource(1000);

using var tcpClient = new TcpClient();
tcpClient.DataReceived += OnDataReceived;
tcpClient.ConnectAsync("tcpbin.com", 4242, cancellationTokenSource.Token);
await tcpClient.SendAsync(new byte[] { 0x01, 0x0A });
await Task.Delay(400);
tcpClient.Disconnect();
tcpClient.DataReceived -= OnDataReceived;
```

## Other projects in the .NET enviorment

| Project | Description |
| ------------- | ------------- |
| [SuperSimpleTcp](https://github.com/jchristn/SuperSimpleTcp) | TcpServer and TcpClient (with SSL support) |
| [DotNetty](https://github.com/Azure/DotNetty) | TcpServer and TcpClient |
| [SpanNetty](https://github.com/cuteant/SpanNetty) | TcpServer and TcpClient (Modern implementation of DotNetty) |
