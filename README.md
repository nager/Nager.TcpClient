# Nager.TcpClient

<img src="https://raw.githubusercontent.com/nager/Nager.TcpClient/main/doc/icon.png" width="150" title="Nager TcpClient" alt="Nager TcpClient" align="left">

A lightweight, asynchronous **TcpClient** with easy-to-use connection handling.
The client triggers `DataReceived`, `Connected`, and `Disconnected` events, making it simple to manage TCP data flow. Optional `TcpKeepAlive` support is available for persistent connections. Logging is customizable by passing an `ILogger` instance, ensuring robust logging.

<br>
<br>

## Comprehensive Test Coverage Across Major Operating Systems

The library is thoroughly tested on all major operating systems, including Ubuntu, Windows, and macOS. A total of 14 tests are executed on each platform to ensure consistent functionality and reliability.

| Operating System | Number of Tests |
|------------------|-----------------|
| Ubuntu           | 14              |
| Windows          | 14              |
| macOS            | 14              |


## How can I use it?

The package is available via [NuGet](https://www.nuget.org/packages/Nager.TcpClient)
```
PM> install-package Nager.TcpClient
```

## Examples of use

For the examples, an online service `tcpbin.com` is used that returns all sent packages. 

### Simple example send and receive data

```cs
void OnDataReceived(byte[] receivedData)
{
}

using var cancellationTokenSource = new CancellationTokenSource(1000);

using var tcpClient = new TcpClient();
tcpClient.DataReceived += OnDataReceived;
await tcpClient.ConnectAsync("tcpbin.com", 4242, cancellationTokenSource.Token);
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
