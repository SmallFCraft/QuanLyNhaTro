using System.Collections.Concurrent;
using QuanLyTro.Shared.Models;

namespace QuanLyTro.Server.Security;

public sealed record SessionInfo(
    int TaiKhoanId,
    string TenDangNhap,
    VaiTroNguoiDung VaiTro,
    int? KhuTroId,
    ChuyenMonNhanVien? ChuyenMon,
    string Token);

public sealed class SessionStore
{
    private readonly ConcurrentDictionary<string, SessionInfo> _sessions = new(StringComparer.Ordinal);

    public Task AddAsync(SessionInfo session)
    {
        _sessions[session.Token] = session;
        return Task.CompletedTask;
    }

    public Task<SessionInfo?> GetAsync(string token) =>
        Task.FromResult(_sessions.TryGetValue(token, out var s) ? s : null);

    public Task RemoveAsync(string token)
    {
        _sessions.TryRemove(token, out _);
        return Task.CompletedTask;
    }
}
