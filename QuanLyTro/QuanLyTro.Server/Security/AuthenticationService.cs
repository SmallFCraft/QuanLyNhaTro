using System.Collections.Concurrent;
using QuanLyTro.Server.Data;
using QuanLyTro.Shared.Models;

namespace QuanLyTro.Server.Security;

/// <summary>
/// Đăng nhập/đăng xuất. Thông báo sai dùng chung để không tiết lộ tài khoản tồn tại.
/// Lockout: 5 sai liên tiếp theo username → khóa 1 phút (TimeProvider injectable để test).
/// </summary>
public sealed class AuthenticationService
{
    private readonly Database _db;
    private readonly SessionStore _sessions;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset LockUntil)> _failures = new(StringComparer.Ordinal);

    private const int MaxFailures = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(1);

    public AuthenticationService(Database db, SessionStore sessions, TimeProvider? clock = null)
    {
        _db = db;
        _sessions = sessions;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<SessionInfo> LoginAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new ValidationException("Vui lòng nhập tên đăng nhập và mật khẩu.");

        // Kiểm tra khóa tạm trước khi xác định thông báo sai
        var now = _clock.GetUtcNow();
        if (_failures.TryGetValue(username, out var f) && f.LockUntil > now)
            throw new AccountLockedException($"Tài khoản tạm khóa, thử lại sau {(int)Math.Ceiling((f.LockUntil - now).TotalSeconds)} giây.");

        var rows = await _db.QueryAsync(
            @"SELECT id, ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro, khu_tro_id, chuyen_mon, bi_khoa
              FROM tai_khoan WHERE ten_dang_nhap = @u LIMIT 1", new { u = username });
        var row = rows.Count == 1 ? rows[0] : null;

        if (row is null || !PasswordHasher.Verify(password, (string)row["mat_khau_hash"]!))
        {
            RecordFailure(username, now);
            throw new AuthenticationFailedException();
        }
        if (Convert.ToInt32(row["bi_khoa"]) == 1)
            throw new AccountLockedException("Tài khoản đã bị khóa. Liên hệ chủ trọ.");

        _failures.TryRemove(username, out _);

        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_');
        var session = new SessionInfo(
            Convert.ToInt32(row["id"]),
            username,
            Enum.Parse<VaiTroNguoiDung>((string)row["vai_tro"]!),
            row["khu_tro_id"] is null or DBNull ? null : Convert.ToInt32(row["khu_tro_id"]),
            row["chuyen_mon"] is null or DBNull ? null : Enum.Parse<ChuyenMonNhanVien>((string)row["chuyen_mon"]!),
            token);
        await _sessions.AddAsync(session);
        return session;
    }

    public Task LogoutAsync(string token) => _sessions.RemoveAsync(token);

    private void RecordFailure(string username, DateTimeOffset now)
    {
        _failures.AddOrUpdate(username,
            _ => (1, DateTimeOffset.MinValue),
            (_, prev) =>
            {
                // Nếu đã từng khóa và thời khóa đã qua → đếm lại từ đầu.
                var continuing = prev.LockUntil <= now;
                var next = continuing ? prev.Count + 1 : 1;
                return next >= MaxFailures
                    ? (next, now.Add(LockDuration))
                    : (next, DateTimeOffset.MinValue);
            });
    }
}
