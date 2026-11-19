using System.Net;
using System.Net.Sockets;
using System.Text;
using QuanLyTro.Server.Config;
using QuanLyTro.Shared.Protocol;

namespace QuanLyTro.Server.Network;

public sealed record ClientContext(string ConnectionId);

/// <summary>
/// TCP server đa client bất đồng bộ. Framing: mỗi JSON object là một dòng kết thúc \n.
/// Ghi socket đồng bộ qua SemaphoreSlim để nhiều response song song không trộn gói.
/// </summary>
public sealed class TcpServer
{
    private readonly ServerOptions _opts;
    private readonly Func<RequestPacket, ClientContext, Task<ResponsePacket>> _handler;
    private TcpListener? _listener;
    private readonly List<ClientConnection> _clients = new();
    private readonly SemaphoreSlim _clientsLock = new(1, 1);
    private int _connectionSeq;

    public TcpServer(ServerOptions opts, Func<RequestPacket, ClientContext, Task<ResponsePacket>> handler)
    {
        _opts = opts;
        _handler = handler;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _listener = new TcpListener(IPAddress.Any, _opts.TcpPort);
        _listener.Start();
        _ = AcceptLoopAsync(ct);
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _listener?.Stop();
        await _clientsLock.WaitAsync();
        try
        {
            foreach (var c in _clients) c.Dispose();
            _clients.Clear();
        }
        finally { _clientsLock.Release(); }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await _listener!.AcceptTcpClientAsync(ct); }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }

            var conn = new ClientConnection(tcp, _opts, _handler, Interlocked.Increment(ref _connectionSeq));
            await _clientsLock.WaitAsync(ct);
            try { _clients.Add(conn); }
            finally { _clientsLock.Release(); }
            _ = conn.RunAsync(ct).ContinueWith(async _ =>
            {
                await _clientsLock.WaitAsync();
                try { _clients.Remove(conn); }
                finally { _clientsLock.Release(); }
                conn.Dispose();
            });
        }
    }
}

/// <summary>Một kết nối client: đọc buffer tích lũy, tách dòng, dispatch, ghi response khóa ghi.</summary>
public sealed class ClientConnection : IDisposable
{
    private readonly TcpClient _tcp;
    private readonly ServerOptions _opts;
    private readonly Func<RequestPacket, ClientContext, Task<ResponsePacket>> _handler;
    private readonly ClientContext _ctx;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private NetworkStream? _stream;

    internal ClientConnection(TcpClient tcp, ServerOptions opts,
        Func<RequestPacket, ClientContext, Task<ResponsePacket>> handler, int connId)
    {
        _tcp = tcp;
        _opts = opts;
        _handler = handler;
        _ctx = new ClientContext($"conn-{connId}");
        _stream = tcp.GetStream();
    }

    internal async Task RunAsync(CancellationToken ct)
    {
        var buffer = new MemoryStream();
        var readBuf = new byte[8192];
        var stream = _stream!;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var n = await stream.ReadAsync(readBuf, ct);
                if (n == 0) break; // client đóng kết nối

                buffer.Write(readBuf, 0, n);
                buffer.Position = 0;
                await ProcessBufferedLines(buffer, stream, ct);
            }
        }
        catch (IOException) { /* disconnect giữa chừng */ }
        catch (ObjectDisposedException) { }
        catch (OperationCanceledException) { }
    }

    private async Task ProcessBufferedLines(MemoryStream buffer, NetworkStream stream, CancellationToken ct)
    {
        // Tách các dòng hoàn chỉnh trong buffer; giữ phần chưa kết thúc cho lần đọc sau.
        var bytes = buffer.ToArray();
        buffer.SetLength(0);
        var start = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != (byte)'\n') continue;
            var lineLen = i - start;
            if (lineLen > 0)
                await HandleFrame(bytes, start, lineLen, stream);
            else
                await WriteResponseAsync(stream, ResponsePacket.Fail(Guid.NewGuid().ToString("N"), "Empty line."), ct);
            start = i + 1;
        }
        buffer.Write(bytes, start, bytes.Length - start); // phần nửa gói còn lại
        buffer.Position = buffer.Length;
    }

    private async Task HandleFrame(byte[] bytes, int offset, int length, NetworkStream stream)
    {
        if (length > _opts.MaxPacketBytes)
        {
            await WriteResponseAsync(stream, ResponsePacket.Fail(
                Guid.NewGuid().ToString("N"),
                $"Packet vượt giới hạn {_opts.MaxPacketBytes} bytes."), CancellationToken.None);
            _stream = null;
            try { _tcp.Close(); } catch { }
            return;
        }

        var line = Encoding.UTF8.GetString(bytes, offset, length);
        RequestPacket packet;
        try
        {
            packet = JsonDefaults.Deserialize<RequestPacket>(line)
                     ?? throw new InvalidOperationException();
        }
        catch
        {
            await WriteResponseAsync(stream, ResponsePacket.Fail(
                Guid.NewGuid().ToString("N"), "Packet không phải JSON hợp lệ."), CancellationToken.None);
            _stream = null;
            try { _tcp.Close(); } catch { }
            return;
        }

        // Xử lý song song; response vẫn ghi qua _writeLock đúng requestId
        _ = Task.Run(async () =>
        {
            ResponsePacket resp;
            try { resp = await _handler(packet, _ctx); }
            catch (Exception ex) { resp = ResponsePacket.Fail(packet.RequestId, ex.Message); }
            var s = _stream;
            if (s is not null) await WriteResponseAsync(s, resp, CancellationToken.None);
        });
    }

    private async Task WriteResponseAsync(NetworkStream stream, ResponsePacket resp, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonDefaults.Serialize(resp) + "\n");
        await _writeLock.WaitAsync(ct);
        try { await stream.WriteAsync(bytes, ct); await stream.FlushAsync(ct); }
        finally { _writeLock.Release(); }
    }

    public void Dispose()
    {
        try { _tcp.Close(); } catch { }
    }
}
