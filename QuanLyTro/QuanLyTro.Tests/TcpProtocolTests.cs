using System.Net;
using System.Net.Sockets;
using System.Text;
using QuanLyTro.Server.Config;
using QuanLyTro.Server.Network;
using QuanLyTro.Shared.Protocol;

namespace QuanLyTro.Tests;

[TestClass]
public class TcpProtocolTests
{
    private static (TcpServer Server, int Port) StartServer(int maxPacketBytes = 262144)
    {
        var port = Random.Shared.Next(20000, 45000);
        var opts = new ServerOptions { TcpPort = port, MaxPacketBytes = maxPacketBytes };
        var server = new TcpServer(opts, (packet, ctx) =>
            Task.FromResult(ResponsePacket.Ok(packet.RequestId, new { echo = packet.Action })));
        server.StartAsync(CancellationToken.None).Wait();
        return (server, port);
    }

    private static async Task<string> SendAndReceiveLine(NetworkStream stream, string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
        return await ReadLineAsync(stream);
    }

    private static async Task<string> ReadLineAsync(NetworkStream stream)
    {
        var sb = new StringBuilder();
        var buf = new byte[1];
        while (true)
        {
            var n = await stream.ReadAsync(buf);
            if (n == 0) throw new EndOfStreamException("connection closed");
            var ch = (char)buf[0];
            if (ch == '\n') break;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    [TestMethod]
    public async Task TwoPacketsInOneWrite_BothGetResponses_WithCorrectRequestIds()
    {
        var (server, port) = StartServer();
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var stream = client.GetStream();

            var p1 = JsonDefaults.Serialize(RequestPacket.Create("A", null));
            var p2 = JsonDefaults.Serialize(RequestPacket.Create("B", null));
            var combined = Encoding.UTF8.GetBytes(p1 + "\n" + p2 + "\n"); // 2 gói dính 1 lần ghi
            await stream.WriteAsync(combined);
            await stream.FlushAsync();

            var r1 = await ReadLineAsync(stream);
            var r2 = await ReadLineAsync(stream);
            var resp1 = JsonDefaults.Deserialize<ResponsePacket>(r1)!;
            var resp2 = JsonDefaults.Deserialize<ResponsePacket>(r2)!;
            Assert.IsTrue(resp1.Success);
            Assert.IsTrue(resp2.Success);
            Assert.AreNotEqual(resp1.RequestId, resp2.RequestId);
        }
        finally { await server.StopAsync(); }
    }

    [TestMethod]
    public async Task OversizePacket_ProtocolErrorThenConnectionCloses()
    {
        var (server, port) = StartServer(maxPacketBytes: 1024);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var stream = client.GetStream();

            var huge = JsonDefaults.Serialize(RequestPacket.Create("BIG", null)) + new string('x', 5000);
            var bytes = Encoding.UTF8.GetBytes(huge + "\n");
            await stream.WriteAsync(bytes);
            await stream.FlushAsync();

            var resp = await ReadLineAsync(stream);
            StringAssert.Contains(resp, "\"success\":false");
            // sau lỗi, server đóng kết nối
            var buf = new byte[64];
            var n = await stream.ReadAsync(buf, 0, 64); // ReadAsync trả 0 khi server close
            Assert.AreEqual(0, n, "server phải đóng kết nối sau lỗi giao thức");
        }
        finally { await server.StopAsync(); }
    }

    [TestMethod]
    public async Task MalformedJson_ProtocolErrorThenConnectionCloses_ServerSurvives()
    {
        var (server, port) = StartServer();
        try
        {
            using (var bad = new TcpClient())
            {
                await bad.ConnectAsync(IPAddress.Loopback, port);
                var bs = bad.GetStream();
                await bs.WriteAsync(Encoding.UTF8.GetBytes("{khong phai json\n"));
                await bs.FlushAsync();
                var resp = await ReadLineAsync(bs);
                StringAssert.Contains(resp, "\"success\":false");
            }
            // server vẫn nhận connection mới sau khi 1 client gửi rác
            using var good = new TcpClient();
            await good.ConnectAsync(IPAddress.Loopback, port);
            var line = await SendAndReceiveLine(good.GetStream(),
                JsonDefaults.Serialize(RequestPacket.Create("OK", null)));
            Assert.IsTrue(JsonDefaults.Deserialize<ResponsePacket>(line)!.Success);
        }
        finally { await server.StopAsync(); }
    }

    [TestMethod]
    public async Task HalfFrameThenDisconnect_ServerSurvives_NewConnectionsStillWork()
    {
        var (server, port) = StartServer();
        try
        {
            using (var half = new TcpClient())
            {
                await half.ConnectAsync(IPAddress.Loopback, port);
                var hs = half.GetStream();
                await hs.WriteAsync(Encoding.UTF8.GetBytes("{\"requestId\":\"x\",\"acti")); // nửa gói
                await hs.FlushAsync();
                // đóng đột ngột không gửi nốt
            }
            await Task.Delay(100); // cho server xử lý disconnect

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var line = await SendAndReceiveLine(client.GetStream(),
                JsonDefaults.Serialize(RequestPacket.Create("SAU_DISCONN", null)));
            Assert.IsTrue(JsonDefaults.Deserialize<ResponsePacket>(line)!.Success);
        }
        finally { await server.StopAsync(); }
    }
}
