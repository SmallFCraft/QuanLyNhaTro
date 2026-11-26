using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuanLyTro.Client.Network;

namespace QuanLyTro.Tests;

[TestClass]
public class BridgeTransportTests
{
    [TestMethod]
    public async Task SendAsync_WhenServerIsUnavailable_UsesVietnameseMessage()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var transport = new TcpClientTransport("127.0.0.1", port);

        var error = await Assert.ThrowsExceptionAsync<IOException>(() => transport.SendAsync("PING", null));
        Assert.AreEqual("Không thể kết nối tới máy chủ. Hãy kiểm tra server có đang chạy không.", error.Message);
    }

    [TestMethod]
    public async Task SendAsync_ReconnectsAfterServerClosesConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var transport = new TcpClientTransport("127.0.0.1", port);

        var firstClient = listener.AcceptTcpClientAsync();
        var firstRequest = transport.SendAsync("PING", null).WaitAsync(TimeSpan.FromSeconds(2));
        using (var server = await firstClient.WaitAsync(TimeSpan.FromSeconds(2)))
        {
            var reader = new StreamReader(server.GetStream(), Encoding.UTF8);
            await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
        await Assert.ThrowsExceptionAsync<IOException>(() => firstRequest);

        var secondClient = listener.AcceptTcpClientAsync();
        var secondRequest = transport.SendAsync("PING", null).WaitAsync(TimeSpan.FromSeconds(2));
        using (var server = await secondClient.WaitAsync(TimeSpan.FromSeconds(2)))
        {
            var reader = new StreamReader(server.GetStream(), Encoding.UTF8);
            var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
            using var request = JsonDocument.Parse(line!);
            var requestId = request.RootElement.GetProperty("requestId").GetString();
            var response = JsonSerializer.Serialize(new { requestId, success = true, message = "pong", data = new { online = true } }) + "\n";
            var bytes = Encoding.UTF8.GetBytes(response);
            await server.GetStream().WriteAsync(bytes);
        }
        var result = await secondRequest;
        Assert.IsTrue(result.Success);
        Assert.AreEqual("pong", result.Message);
        Assert.IsNotNull(result.Data);
    }
}
