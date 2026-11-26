using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using QuanLyTro.Shared.Protocol;

namespace QuanLyTro.Client.Network;

/// <summary>
/// Giao tiếp TCP client: mỗi request sinh GUID requestId, chờ response cùng requestId.
/// </summary>
public sealed class TcpClientTransport : IDisposable
{
    private System.Net.Sockets.TcpClient? _client;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ResponsePacket>> _pending = new();
    private CancellationTokenSource? _readCts;
    private string _host;
    private int _port;

    public bool IsConnected => _client?.Connected ?? false;

    public TcpClientTransport(string host = "127.0.0.1", int port = 8888)
    {
        _host = host;
        _port = port;
    }

    public async Task ConnectAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            if (_client is { Connected: true } && _stream is not null) return;
            _stream?.Dispose();
            _client?.Dispose();
            _stream = null;
            _client = null;
            _readCts?.Dispose();
            _readCts = new CancellationTokenSource();
            var client = new System.Net.Sockets.TcpClient();
            try
            {
                await client.ConnectAsync(_host, _port);
                _client = client;
                _stream = client.GetStream();
                _ = ReadLoopAsync(client, _stream, _readCts.Token);
            }
            catch (SocketException ex)
            {
                client.Dispose();
                _readCts.Dispose();
                _readCts = null;
                throw new IOException("Không thể kết nối tới máy chủ. Hãy kiểm tra server có đang chạy không.", ex);
            }
            catch
            {
                client.Dispose();
                _readCts.Dispose();
                _readCts = null;
                throw;
            }
        }
        finally { _connectionLock.Release(); }
    }

    public async Task<ResponsePacket> SendAsync(string action, string? token, object? data = null)
    {
        if (!IsConnected) await ConnectAsync();
        var packet = RequestPacket.Create(action, token, data);
        var tcs = new TaskCompletionSource<ResponsePacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[packet.RequestId] = tcs;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(packet, JsonDefaults.Options) + "\n");
            await _writeLock.WaitAsync();
            try { await _stream!.WriteAsync(bytes); await _stream.FlushAsync(); }
            finally { _writeLock.Release(); }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            return await tcs.Task.WaitAsync(cts.Token);
        }
        finally
        {
            _pending.TryRemove(packet.RequestId, out _);
        }
    }

    private async Task ReadLoopAsync(TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        var buf = new byte[8192];
        var line = new MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var n = await stream.ReadAsync(buf, ct);
                if (n == 0) break;
                for (var i = 0; i < n; i++)
                {
                    if (buf[i] == (byte)'\n')
                    {
                        HandleLine(line.ToArray());
                        line.SetLength(0);
                    }
                    else line.WriteByte(buf[i]);
                }
            }
        }
        catch { /* disconnect */ }
        // giải phóng mọi request đang chờ khi mất kết nối
        foreach (var kv in _pending)
            kv.Value.TrySetException(new IOException("Mất kết nối tới server."));
        if (ReferenceEquals(_client, client))
        {
            _stream?.Dispose();
            client.Dispose();
            _stream = null;
            _client = null;
        }
    }

    private void HandleLine(byte[] bytes)
    {
        if (bytes.Length == 0) return;
        try
        {
            var resp = JsonSerializer.Deserialize<ResponsePacket>(bytes, JsonDefaults.Options);
            if (resp is not null && _pending.TryRemove(resp.RequestId, out var tcs))
                tcs.TrySetResult(resp);
        }
        catch { /* bỏ dòng lỗi parse */ }
    }

    public void Dispose()
    {
        _readCts?.Cancel();
        _stream?.Dispose();
        _client?.Dispose();
    }
}
