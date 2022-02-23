# Nager.TcpClient

An easy tcp client

## How can I use it?

The package is available on [nuget](https://www.nuget.org/packages/Nager.TcpClient)
```
PM> install-package Nager.TcpClient
```

## Examples of use
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
