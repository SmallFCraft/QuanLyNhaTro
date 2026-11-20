using System.Text.Json;
using QuanLyTro.Server.Network;
using QuanLyTro.Server.Repositories;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;
using QuanLyTro.Shared.Protocol;

namespace QuanLyTro.Server.Services;

/// <summary>
/// Nghiệp vụ người thuê: đăng ký public, quản lý hồ sơ (scope khu), chuyển/trả phòng nguyên tử,
/// tìm kiếm ≤50 kết quả với escape ký tự LIKE.
/// </summary>
public sealed class TenantService
{
    private readonly TenantRepository _repo;
    private readonly RoomRepository _rooms;
    private readonly SiteRepository _sites;

    public TenantService(TenantRepository repo, RoomRepository rooms, SiteRepository sites)
    {
        _repo = repo;
        _rooms = rooms;
        _sites = sites;
    }

    public async Task<object> RegisterAsync(JsonElement data)
    {
        var tenDangNhap = AccountService.GetString(data, "tenDangNhap") ?? throw new ValidationException("Thiếu tên đăng nhập.");
        var matKhau = AccountService.GetString(data, "matKhau") ?? throw new ValidationException("Thiếu mật khẩu.");
        var hoTen = AccountService.GetString(data, "hoTen") ?? throw new ValidationException("Thiếu họ tên.");
        var cccd = AccountService.GetString(data, "cccd") ?? throw new ValidationException("Thiếu CCCD.");
        var ngaySinhStr = AccountService.GetString(data, "ngaySinh") ?? throw new ValidationException("Thiếu ngày sinh.");
        var soDienThoai = AccountService.GetString(data, "soDienThoai") ?? throw new ValidationException("Thiếu số điện thoại.");
        var queQuan = AccountService.GetString(data, "queQuan") ?? throw new ValidationException("Thiếu quê quán.");
        var noiLamViec = AccountService.GetString(data, "noiLamViec");

        if (matKhau.Length < 6) throw new ValidationException("Mật khẩu phải có ít nhất 6 ký tự.");
        if (cccd.Length != 12 || !cccd.All(char.IsAsciiDigit))
            throw new ValidationException("CCCD phải gồm đúng 12 chữ số.");
        if (!DateOnly.TryParse(ngaySinhStr, out var ngaySinh) || ngaySinh >= DateOnly.FromDateTime(DateTime.Today))
            throw new ValidationException("Ngày sinh phải là ngày trong quá khứ.");
        if (soDienThoai.Length < 9 || soDienThoai.Length > 15 || !soDienThoai.All(c => char.IsAsciiDigit(c) || c == '+'))
            throw new ValidationException("Số điện thoại không hợp lệ.");

        // trùng tên đăng nhập hoặc CCCD → validation rõ ràng, transaction rollback sạch
        var tk = await _repo.ScalarCountAsync("SELECT COUNT(*) FROM tai_khoan WHERE ten_dang_nhap = @u", new { u = tenDangNhap });
        if (tk > 0) throw new ValidationException("Tên đăng nhập đã tồn tại.");
        var hs = await _repo.ScalarCountAsync("SELECT COUNT(*) FROM ho_so_khach_thue WHERE cccd = @c", new { c = cccd });
        if (hs > 0) throw new ValidationException("Số CCCD đã được đăng ký.");

        var id = await _repo.RegisterAsync(tenDangNhap, PasswordHasher.Hash(matKhau), hoTen.Trim(),
            cccd, ngaySinh, soDienThoai, queQuan.Trim(), noiLamViec);
        return new { taiKhoanId = id };
    }

    public async Task<object> AddAsync(SessionInfo session, JsonElement data)
    {
        // ChuTro + QuanLy thêm hồ sơ người thuê (chưa có tài khoản — tạm lưu hồ sơ thuần? Không:
        // model 1-1 bắt buộc tài khoản. Do đó "thêm" tạo cả tài khoản với tên = CCCD.
        if (session.VaiTro is not (VaiTroNguoiDung.ChuTro or VaiTroNguoiDung.QuanLy))
            throw new NotFoundOrDeniedException();
        // đăng ký giống public nhưng do quản lý thao tác
        return await RegisterAsync(data);
    }

    public async Task<object> UpdateAsync(SessionInfo session, JsonElement data)
    {
        if (session.VaiTro is not (VaiTroNguoiDung.ChuTro or VaiTroNguoiDung.QuanLy))
            throw new NotFoundOrDeniedException();
        var id = AccountService.GetInt(data, "taiKhoanId");
        await RequireScopeAsync(session, id);
        var hoTen = AccountService.GetString(data, "hoTen");
        if (hoTen is not null)
            await _repo.UpdateHoTenAsync(id, hoTen);
        return new { ok = true };
    }

    public async Task<object> UpdateTamTruAsync(SessionInfo session, JsonElement data)
    {
        if (session.VaiTro is not (VaiTroNguoiDung.ChuTro or VaiTroNguoiDung.QuanLy))
            throw new NotFoundOrDeniedException();
        var id = AccountService.GetInt(data, "taiKhoanId");
        await RequireScopeAsync(session, id);
        var daDangKy = data.TryGetProperty("daDangKyTamTru", out var el) && el.GetBoolean();
        await _repo.UpdateTamTruAsync(id, daDangKy);
        return new { ok = true };
    }

    public async Task<object> MoveRoomAsync(SessionInfo session, JsonElement data)
    {
        if (session.VaiTro is not (VaiTroNguoiDung.ChuTro or VaiTroNguoiDung.QuanLy))
            throw new NotFoundOrDeniedException();
        var taiKhoanId = AccountService.GetInt(data, "taiKhoanId");
        var phongIdMoi = AccountService.GetInt(data, "phongIdMoi");
        await RequireScopeAsync(session, taiKhoanId);

        var phongMoi = await _rooms.RoomByIdAsync(phongIdMoi) ?? throw new NotFoundOrDeniedException();
        if (session.VaiTro == VaiTroNguoiDung.QuanLy && session.KhuTroId != Convert.ToInt32(phongMoi["khu_tro_id"]))
            throw new NotFoundOrDeniedException();
        if ((string)phongMoi["trang_thai"]! == "BaoTri")
            throw new BusinessRuleException("Phòng đang bảo trì, không thể chuyển vào.");

        var khuTroIdMoi = Convert.ToInt32(phongMoi["khu_tro_id"]);
        await TransactionHelper.MoveRoomAsync(_repo.Db, _repo, taiKhoanId, phongIdMoi, khuTroIdMoi, session.TaiKhoanId);
        return new { ok = true };
    }

    public async Task<object> CheckOutAsync(SessionInfo session, JsonElement data)
    {
        if (session.VaiTro is not (VaiTroNguoiDung.ChuTro or VaiTroNguoiDung.QuanLy))
            throw new NotFoundOrDeniedException();
        var taiKhoanId = AccountService.GetInt(data, "taiKhoanId");
        await RequireScopeAsync(session, taiKhoanId);
        await TransactionHelper.CheckOutAsync(_repo.Db, taiKhoanId, session.TaiKhoanId);
        return new { ok = true };
    }

    public async Task<object> DeleteAsync(SessionInfo session, JsonElement data)
    {
        if (session.VaiTro != VaiTroNguoiDung.ChuTro)
            throw new NotFoundOrDeniedException();
        var id = AccountService.GetInt(data, "taiKhoanId");
        var profile = await _repo.ProfileAsync(id) ?? throw new NotFoundOrDeniedException();
        if (profile["phong_id"] is not (null or DBNull))
            throw new BusinessRuleException("Người thuê đang ở phòng, phải trả phòng trước khi xóa.");
        var n = await _repo.DeleteAsync(id);
        if (n == 0) throw new NotFoundOrDeniedException();
        return new { ok = true };
    }

    public async Task<object> SearchAsync(SessionInfo session, JsonElement data)
    {
        var keyword = AccountService.GetString(data, "keyword") ?? throw new ValidationException("Thiếu từ khóa tìm kiếm.");
        if (string.IsNullOrWhiteSpace(keyword))
            throw new ValidationException("Từ khóa tìm kiếm không được rỗng.");
        if (keyword.Length > 50) throw new ValidationException("Từ khóa quá dài.");

        // Escape LIKE wildcard để '%' không biến thành bộ lọc toàn bộ
        var escaped = keyword
            .Replace("!", "!!").Replace("%", "!%").Replace("_", "!_").Replace("[", "![");
        var rows = await _repo.SearchAsync(escaped, limit: 50);
        return rows.Select(r => new
        {
            id = Convert.ToInt32(r["id"]),
            hoTen = (string)r["ho_ten"]!,
            tenDangNhap = (string)r["ten_dang_nhap"]!,
            cccd = (string)r["cccd"]!,
            soDienThoai = (string)r["so_dien_thoai"]!,
            phongId = r["phong_id"] is null or DBNull ? (int?)null : Convert.ToInt32(r["phong_id"]),
            daDangKyTamTru = Convert.ToInt32(r["da_dang_ky_tam_tru"]) == 1,
        }).ToArray();
    }

    public async Task<object> GetMyProfileAsync(SessionInfo session)
    {
        if (session.VaiTro != VaiTroNguoiDung.KhachThue)
            throw new NotFoundOrDeniedException();
        var profile = await _repo.ProfileAsync(session.TaiKhoanId) ?? throw new NotFoundOrDeniedException();
        return new
        {
            id = Convert.ToInt32(profile["id"]),
            hoTen = (string)profile["ho_ten"]!,
            tenDangNhap = (string)profile["ten_dang_nhap"]!,
            cccd = (string)profile["cccd"]!,
            ngaySinh = Convert.ToDateTime(profile["ngay_sinh"]!).ToString("yyyy-MM-dd"),
            soDienThoai = (string)profile["so_dien_thoai"]!,
            queQuan = (string)profile["que_quan"]!,
            noiLamViec = profile["noi_lam_viec"] as string,
            phongId = profile["phong_id"] is null or DBNull ? (int?)null : Convert.ToInt32(profile["phong_id"]),
            daDangKyTamTru = Convert.ToInt32(profile["da_dang_ky_tam_tru"]) == 1,
        };
    }

    public async Task<object> GetByPhongAsync(SessionInfo session, JsonElement data)
    {
        var phongId = AccountService.GetInt(data, "phongId");
        // KhachThue chỉ xem phòng của mình; QuanLy trong scope; ChuTro/CongAn/LêTan xem hết (chỉ đọc)
        if (session.VaiTro == VaiTroNguoiDung.KhachThue)
        {
            var mine = await _repo.ProfileAsync(session.TaiKhoanId);
            int? myPhong = mine?["phong_id"] is null or DBNull ? (int?)null : Convert.ToInt32(mine["phong_id"]);
            if (myPhong != phongId) throw new NotFoundOrDeniedException();
        }
        else if (session.VaiTro == VaiTroNguoiDung.QuanLy)
        {
            var phong = await _rooms.RoomByIdAsync(phongId) ?? throw new NotFoundOrDeniedException();
            if (session.KhuTroId != Convert.ToInt32(phong["khu_tro_id"]))
                throw new NotFoundOrDeniedException();
        }
        var rows = await _repo.ByPhongAsync(phongId);
        return rows.Select(r => new
        {
            id = Convert.ToInt32(r["id"]),
            hoTen = (string)r["ho_ten"]!,
            cccd = (string)r["cccd"]!,
            soDienThoai = (string)r["so_dien_thoai"]!,
            daDangKyTamTru = Convert.ToInt32(r["da_dang_ky_tam_tru"]) == 1,
        }).ToArray();
    }

    public async Task<object> UpdateLienHeAsync(SessionInfo session, JsonElement data)
    {
        // KhachThue tự cập nhật liên hệ của mình; QuanLy/ChuTro cập nhật theo taiKhoanId trong scope
        var target = session.VaiTro == VaiTroNguoiDung.KhachThue
            ? session.TaiKhoanId
            : AccountService.GetInt(data, "taiKhoanId");
        if (session.VaiTro == VaiTroNguoiDung.QuanLy)
            await RequireScopeAsync(session, target);
        var sdt = AccountService.GetString(data, "soDienThoai");
        var nlv = AccountService.GetString(data, "noiLamViec");
        if (sdt is null && nlv is null)
            throw new ValidationException("Thiếu thông tin cần cập nhật.");
        if (sdt is not null && (sdt.Length < 9 || sdt.Length > 15))
            throw new ValidationException("Số điện thoại không hợp lệ.");
        await _repo.UpdateLienHeAsync(target, sdt, nlv);
        return new { ok = true };
    }

    /// <summary>QuanLy chỉ được thao tác người thuê đang ở trong khu của mình (hoặc chưa có phòng).</summary>
    private async Task RequireScopeAsync(SessionInfo session, int taiKhoanId)
    {
        if (session.VaiTro != VaiTroNguoiDung.QuanLy) return;
        var profile = await _repo.ProfileAsync(taiKhoanId) ?? throw new NotFoundOrDeniedException();
        if (profile["phong_id"] is null or DBNull) return; // chưa ở phòng nào — cho phép (tiếp nhận mới)
        var phongId = Convert.ToInt32(profile["phong_id"]);
        var phong = await _rooms.RoomByIdAsync(phongId) ?? throw new NotFoundOrDeniedException();
        if (session.KhuTroId != Convert.ToInt32(phong["khu_tro_id"]))
            throw new NotFoundOrDeniedException();
    }
}
