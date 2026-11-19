using System.Text.Json;
using QuanLyTro.Server.Data;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;
using QuanLyTro.Shared.Protocol;

namespace QuanLyTro.Server.Services;

/// <summary>
/// Handlers M1: đăng nhập/đăng xuất/đổi mật khẩu, quản trị tài khoản (ChuTro),
/// ma trận phân quyền, cấu hình nhận tiền. DTO không bao giờ chứa hash/token.
/// </summary>
public sealed class AccountService
{
    private readonly Database _db;
    private readonly SessionStore _sessions;

    public AccountService(Database db, SessionStore sessions)
    {
        _db = db;
        _sessions = sessions;
    }

    // ---- Đăng nhập/đăng xuất/đổi mật khẩu ----

    public async Task<object> LoginAsync(JsonElement data)
    {
        var username = GetString(data, "tenDangNhap") ?? throw new ValidationException("Thiếu tên đăng nhập.");
        var password = GetString(data, "matKhau") ?? throw new ValidationException("Thiếu mật khẩu.");
        var auth = new AuthenticationService(_db, _sessions);
        var session = await auth.LoginAsync(username, password);
        return new
        {
            token = session.Token,
            taiKhoanId = session.TaiKhoanId,
            tenDangNhap = session.TenDangNhap,
            vaiTro = session.VaiTro.ToString(),
            khuTroId = session.KhuTroId,
            chuyenMon = session.ChuyenMon?.ToString(),
        };
    }

    public async Task<object> LogoutAsync(SessionInfo session, string token)
    {
        await _sessions.RemoveAsync(token);
        return new { ok = true };
    }

    public async Task<object> ChangePasswordAsync(SessionInfo session, JsonElement data)
    {
        var matKhauCu = GetString(data, "matKhauCu") ?? throw new ValidationException("Thiếu mật khẩu cũ.");
        var matKhauMoi = GetString(data, "matKhauMoi") ?? throw new ValidationException("Thiếu mật khẩu mới.");
        if (matKhauMoi.Length < 6)
            throw new ValidationException("Mật khẩu mới phải có ít nhất 6 ký tự.");
        if (GetString(data, "xacNhanMatKhau") is { } xn && xn != matKhauMoi)
            throw new ValidationException("Nhập lại mật khẩu mới không khớp.");

        var row = (await _db.QueryAsync(
            "SELECT mat_khau_hash FROM tai_khoan WHERE id = @id", new { id = session.TaiKhoanId }))
            .SingleOrDefault() ?? throw new NotFoundOrDeniedException();
        if (!PasswordHasher.Verify(matKhauCu, (string)row["mat_khau_hash"]!))
            throw new ValidationException("Mật khẩu cũ không đúng.");

        await _db.ExecuteAsync(
            "UPDATE tai_khoan SET mat_khau_hash = @h, ngay_cap_nhat = NOW() WHERE id = @id",
            new { h = PasswordHasher.Hash(matKhauMoi), id = session.TaiKhoanId });
        return new { ok = true };
    }

    // ---- Quản trị tài khoản (ChuTro) ----

    public async Task<object> ListAsync(SessionInfo session, JsonElement data)
    {
        var rows = await _db.QueryAsync(
            @"SELECT id, ten_dang_nhap, ho_ten, vai_tro, khu_tro_id, chuyen_mon, bi_khoa, ly_do_khoa
              FROM tai_khoan ORDER BY id");
        return rows.Select(AccountDto).ToArray();
    }

    public async Task<object> CreateAsync(SessionInfo session, JsonElement data)
    {
        var tenDangNhap = GetString(data, "tenDangNhap") ?? throw new ValidationException("Thiếu tên đăng nhập.");
        var matKhau = GetString(data, "matKhau") ?? throw new ValidationException("Thiếu mật khẩu ban đầu.");
        var hoTen = GetString(data, "hoTen") ?? throw new ValidationException("Thiếu họ tên.");
        var vaiTro = GetString(data, "vaiTro") ?? throw new ValidationException("Thiếu vai trò.");
        if (!Enum.TryParse<VaiTroNguoiDung>(vaiTro, out var role))
            throw new ValidationException("Vai trò không hợp lệ.");
        var khuTroId = GetOptionalInt(data, "khuTroId");
        var chuyenMon = GetString(data, "chuyenMon");

        // Invariant: khu_tro_id chỉ cho QuanLy, chuyen_mon chỉ cho NhanVien
        if (role != VaiTroNguoiDung.QuanLy && khuTroId is not null)
            throw new ValidationException("Chỉ vai trò QuanLy mới được gán khu trọ.");
        if (role != VaiTroNguoiDung.NhanVien && chuyenMon is not null)
            throw new ValidationException("Chỉ vai trò NhanVien mới có chuyên môn.");

        // Chủ trọ không thể tự khóa/xóa chính mình — ở đây tạo mới nên check trùng tên
        var exists = await _db.ScalarAsync(
            "SELECT COUNT(*) FROM tai_khoan WHERE ten_dang_nhap = @u", new { u = tenDangNhap });
        if (Convert.ToInt64(exists) > 0)
            throw new ValidationException("Tên đăng nhập đã tồn tại.");

        var id = await _db.ExecuteAsync(
            @"INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro, khu_tro_id, chuyen_mon)
              VALUES (@u, @h, @n, @v, @k, @c)",
            new { u = tenDangNhap, h = PasswordHasher.Hash(matKhau), n = hoTen, v = vaiTro, k = khuTroId, c = chuyenMon });
        return new { taiKhoanId = id };
    }

    public async Task<object> UpdateAsync(SessionInfo session, JsonElement data)
    {
        var id = GetInt(data, "taiKhoanId");
        var hoTen = GetString(data, "hoTen");
        if (id == session.TaiKhoanId && hoTen is null)
            throw new ValidationException("Thiếu thông tin cập nhật.");
        await _db.ExecuteAsync(
            "UPDATE tai_khoan SET ho_ten = COALESCE(@n, ho_ten), ngay_cap_nhat = NOW() WHERE id = @id",
            new { n = hoTen, id });
        return new { ok = true };
    }

    public async Task<object> LockAsync(SessionInfo session, JsonElement data)
    {
        var tenDangNhap = GetString(data, "tenDangNhap") ?? throw new ValidationException("Thiếu tên đăng nhập.");
        var lyDo = GetString(data, "lyDo") ?? throw new ValidationException("Thiếu lý do khóa.");
        if (tenDangNhap == session.TenDangNhap)
            throw new BusinessRuleException("Không thể tự khóa tài khoản của chính mình.");
        var n = await _db.ExecuteAsync(
            "UPDATE tai_khoan SET bi_khoa = 1, ly_do_khoa = @l, ngay_cap_nhat = NOW() WHERE ten_dang_nhap = @u AND bi_khoa = 0",
            new { l = lyDo, u = tenDangNhap });
        if (n == 0) throw new NotFoundOrDeniedException();
        return new { ok = true };
    }

    public async Task<object> UnlockAsync(SessionInfo session, JsonElement data)
    {
        var tenDangNhap = GetString(data, "tenDangNhap") ?? throw new ValidationException("Thiếu tên đăng nhập.");
        var n = await _db.ExecuteAsync(
            "UPDATE tai_khoan SET bi_khoa = 0, ly_do_khoa = NULL, ngay_cap_nhat = NOW() WHERE ten_dang_nhap = @u AND bi_khoa = 1",
            new { u = tenDangNhap });
        if (n == 0) throw new NotFoundOrDeniedException();
        return new { ok = true };
    }

    public async Task<object> ResetPasswordAsync(SessionInfo session, JsonElement data)
    {
        var tenDangNhap = GetString(data, "tenDangNhap") ?? throw new ValidationException("Thiếu tên đăng nhập.");
        var matKhauMoi = GetString(data, "matKhauMoi") ?? throw new ValidationException("Thiếu mật khẩu mới.");
        if (matKhauMoi.Length < 6) throw new ValidationException("Mật khẩu mới phải có ít nhất 6 ký tự.");
        var n = await _db.ExecuteAsync(
            "UPDATE tai_khoan SET mat_khau_hash = @h, ngay_cap_nhat = NOW() WHERE ten_dang_nhap = @u",
            new { h = PasswordHasher.Hash(matKhauMoi), u = tenDangNhap });
        if (n == 0) throw new NotFoundOrDeniedException();
        return new { ok = true };
    }

    public async Task<object> DeleteAsync(SessionInfo session, JsonElement data)
    {
        var tenDangNhap = GetString(data, "tenDangNhap") ?? throw new ValidationException("Thiếu tên đăng nhập.");
        if (tenDangNhap == session.TenDangNhap)
            throw new BusinessRuleException("Không thể xóa tài khoản của chính mình.");
        // Không xóa khi còn dữ liệu nghiệp vụ (FK sẽ chặn) — trả lỗi rõ ràng
        try
        {
            var n = await _db.ExecuteAsync("DELETE FROM tai_khoan WHERE ten_dang_nhap = @u", new { u = tenDangNhap });
            if (n == 0) throw new NotFoundOrDeniedException();
        }
        catch (MySqlConnector.MySqlException) when (true)
        {
            throw new BusinessRuleException("Không thể xóa: tài khoản còn dữ liệu liên quan. Dùng khóa tài khoản thay thế.");
        }
        return new { ok = true };
    }

    // ---- Phân quyền ----

    public async Task<object> GetPermissionMatrixAsync(SessionInfo session)
    {
        var seeded = await _db.QueryAsync("SELECT vai_tro, hanh_dong FROM quyen_vai_tro");
        var effective = seeded.Select(r => new { vaiTro = (string)r["vai_tro"]!, hanhDong = (string)r["hanh_dong"]! })
            .ToArray();
        var macDinh = Enum.GetValues<VaiTroNguoiDung>()
            .Select(v => new
            {
                vaiTro = v.ToString(),
                hanhDong = DefaultPermissions.ActionsFor(v).ToArray(),
            })
            .Where(x => x.hanhDong.Length > 0)
            .ToArray();
        return new { macDinh, hieuLuc = effective };
    }

    public async Task<object> UpdateRolePermissionsAsync(SessionInfo session, JsonElement data)
    {
        var vaiTro = GetString(data, "vaiTro") ?? throw new ValidationException("Thiếu vai trò.");
        if (!Enum.TryParse<VaiTroNguoiDung>(vaiTro, out _))
            throw new ValidationException("Vai trò không hợp lệ.");
        if (data.TryGetProperty("thuHoi", out var revokeEl) && revokeEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in revokeEl.EnumerateArray())
            {
                var action = a.GetString();
                if (string.IsNullOrEmpty(action)) continue;
                // Không cho thu hồi quyền nền của ChuTro (vẫn đăng nhập/quản trị được)
                if (vaiTro == VaiTroNguoiDung.ChuTro.ToString() &&
                    (action == ActionNames.DANG_NHAP || action == ActionNames.PHAN_QUYEN_CAP_NHAT_VAI_TRO))
                    throw new BusinessRuleException("Không thể thu hồi quyền nền của Chủ trọ.");
                await _db.ExecuteAsync(
                    "DELETE FROM quyen_vai_tro WHERE vai_tro = @v AND hanh_dong = @a",
                    new { v = vaiTro, a = action });
            }
        }
        if (data.TryGetProperty("capQuyen", out var grantEl) && grantEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in grantEl.EnumerateArray())
            {
                var action = a.GetString();
                if (string.IsNullOrEmpty(action)) continue;
                await _db.ExecuteAsync(
                    "INSERT IGNORE INTO quyen_vai_tro (vai_tro, hanh_dong) VALUES (@v, @a)",
                    new { v = vaiTro, a = action });
            }
        }
        return new { ok = true };
    }

    // ---- Cấu hình nhận tiền ----

    public async Task<object> GetConfigAsync(SessionInfo session, bool withSecret)
    {
        var rows = await _db.QueryAsync("SELECT * FROM cau_hinh_he_thong WHERE id = 1");
        var r = rows.SingleOrDefault();
        return new
        {
            soTaiKhoan = r?["so_tai_khoan"] as string,
            tenNganHang = r?["ten_ngan_hang"] as string,
            chuTaiKhoan = r?["chu_tai_khoan"] as string,
            mauQr = r?["mau_qr"] as string,
            // api_token chỉ trả cho CAU_HINH_DAY_DU của ChuTro (router đã chặn vai trò khác)
            apiToken = withSecret && session.VaiTro == VaiTroNguoiDung.ChuTro ? r?["api_token"] as string : null,
        };
    }

    public async Task<object> UpdateConfigAsync(SessionInfo session, JsonElement data)
    {
        await _db.ExecuteAsync(@"INSERT INTO cau_hinh_he_thong (id, so_tai_khoan, ten_ngan_hang, chu_tai_khoan, mau_qr, api_token, ngay_cap_nhat)
            VALUES (1, @s, @n, @c, @q, @t, NOW())
            ON DUPLICATE KEY UPDATE so_tai_khoan=@s, ten_ngan_hang=@n, chu_tai_khoan=@c, mau_qr=@q, api_token=@t, ngay_cap_nhat=NOW()",
            new
            {
                s = GetString(data, "soTaiKhoan"),
                n = GetString(data, "tenNganHang"),
                c = GetString(data, "chuTaiKhoan"),
                q = GetString(data, "mauQr"),
                t = GetString(data, "apiToken"),
            });
        return new { ok = true };
    }

    // ---- helpers ----

    private static object AccountDto(Dictionary<string, object?> r) => new
    {
        id = Convert.ToInt32(r["id"]),
        tenDangNhap = (string)r["ten_dang_nhap"]!,
        hoTen = (string)r["ho_ten"]!,
        vaiTro = (string)r["vai_tro"]!,
        khuTroId = r["khu_tro_id"] is null or DBNull ? (int?)null : Convert.ToInt32(r["khu_tro_id"]),
        chuyenMon = r["chuyen_mon"] as string,
        biKhoa = Convert.ToInt32(r["bi_khoa"]) == 1,
        lyDoKhoa = r["ly_do_khoa"] as string,
    };

    internal static string? GetString(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object &&
        data.TryGetProperty(name, out var el) &&
        el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    internal static int GetInt(JsonElement data, string name) =>
        data.TryGetProperty(name, out var el) && el.TryGetInt32(out var v)
            ? v : throw new ValidationException($"Thiếu hoặc sai kiểu trường {name}.");

    internal static int? GetOptionalInt(JsonElement data, string name)
    {
        if (data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty(name, out var el) &&
            el.ValueKind == JsonValueKind.Number &&
            el.TryGetInt32(out var v))
            return v;
        return null;
    }
}
