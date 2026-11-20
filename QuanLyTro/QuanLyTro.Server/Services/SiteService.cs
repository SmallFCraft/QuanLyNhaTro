using QuanLyTro.Server.Network;
using QuanLyTro.Server.Repositories;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;
using QuanLyTro.Shared.Protocol;

using System.Text.Json;

namespace QuanLyTro.Server.Services;

/// <summary>
/// Nghiệp vụ khu trọ + phòng. Scope: ChuTro thấy hết; QuanLy bị giới hạn khu trong session;
/// mọi vi phạm phạm vi trả NotFoundOrDeniedException (không tiết lộ bản ghi có tồn tại).
/// </summary>
public sealed class SiteService
{
    private readonly SiteRepository _repo;
    private readonly RoomRepository _rooms;
    private readonly SessionStore _sessions;

    public SiteService(SiteRepository repo, RoomRepository rooms, SessionStore sessions)
    {
        _repo = repo;
        _rooms = rooms;
        _sessions = sessions;
    }

    private static int? ScopeKhuTroId(SessionInfo session) =>
        session.VaiTro == VaiTroNguoiDung.QuanLy ? session.KhuTroId : null;

    public async Task<object> ListAsync(SessionInfo session, JsonElement data)
    {
        var khuId = ScopeKhuTroId(session);
        var khuRows = (await _repo.ListAsync())
            .Where(r => khuId is null || Convert.ToInt32(r["id"]) == khuId)
            .ToList();
        return khuRows.Select(KhuDto).ToArray();
    }

    public async Task<object> CreateAsync(SessionInfo session, JsonElement data)
    {
        RequireChuTro(session);
        var ten = AccountService.GetString(data, "tenKhuTro") ?? throw new ValidationException("Thiếu tên khu trọ.");
        var diaChi = AccountService.GetString(data, "diaChi") ?? throw new ValidationException("Thiếu địa chỉ.");
        if (string.IsNullOrWhiteSpace(ten) || string.IsNullOrWhiteSpace(diaChi))
            throw new ValidationException("Tên khu và địa chỉ không được rỗng.");
        var id = await _repo.InsertKhuAsync(ten.Trim(), diaChi.Trim(), null);
        return new { khuTroId = id };
    }

    public async Task<object> UpdateAsync(SessionInfo session, JsonElement data)
    {
        RequireChuTro(session);
        var id = AccountService.GetInt(data, "khuTroId");
        var ten = AccountService.GetString(data, "tenKhuTro") ?? throw new ValidationException("Thiếu tên khu trọ.");
        var diaChi = AccountService.GetString(data, "diaChi") ?? throw new ValidationException("Thiếu địa chỉ.");
        var n = await _repo.UpdateKhuAsync(id, ten.Trim(), diaChi.Trim());
        if (n == 0) throw new NotFoundOrDeniedException();
        return new { ok = true };
    }

    public async Task<object> AssignManagerAsync(SessionInfo session, JsonElement data)
    {
        RequireChuTro(session);
        var khuTroId = AccountService.GetInt(data, "khuTroId");
        var tenQuanLy = AccountService.GetString(data, "tenDangNhapQuanLy") ?? throw new ValidationException("Thiếu tên đăng nhập quản lý.");

        var qlId = await _repo.TryQuanLyAccountIdByTenDangNhapAsync(tenQuanLy);
        if (qlId is null) throw new ValidationException("Tài khoản không tồn tại hoặc không phải vai trò QuanLy.");

        // Một quản lý tối đa một khu: gỡ gán cũ ở cả khu_tro lẫn tai_khoan trước khi gán mới
        await _repo.ClearManagerOfAsync(qlId.Value);
        await _repo.AssignManagerAsync(khuTroId, qlId.Value);
        await _repo.SetAccountKhuTroAsync(qlId.Value, khuTroId);
        return new { ok = true };
    }

    public async Task<object> SuspendAsync(SessionInfo session, JsonElement data)
    {
        RequireChuTro(session);
        var id = AccountService.GetInt(data, "khuTroId");
        await _repo.SuspendAsync(id);
        return new { ok = true };
    }

    public async Task<object> DeleteAsync(SessionInfo session, JsonElement data)
    {
        RequireChuTro(session);
        var id = AccountService.GetInt(data, "khuTroId");
        var rooms = await _repo.CountRoomsAsync(id);
        if (rooms > 0)
            throw new BusinessRuleException("Khu trọ còn phòng, không thể xóa. Ngừng hoạt động thay thế.");
        var n = await _repo.DeleteKhuAsync(id);
        if (n == 0) throw new NotFoundOrDeniedException();
        return new { ok = true };
    }

    private static void RequireChuTro(SessionInfo session)
    {
        if (session.VaiTro != VaiTroNguoiDung.ChuTro)
            throw new NotFoundOrDeniedException();
    }

    private static object KhuDto(Dictionary<string, object?> r) => new
    {
        id = Convert.ToInt32(r["id"]),
        tenKhuTro = (string)r["ten_khu_tro"]!,
        diaChi = (string)r["dia_chi"]!,
        quanLyTaiKhoanId = r["quan_ly_tai_khoan_id"] is null or DBNull ? (int?)null : Convert.ToInt32(r["quan_ly_tai_khoan_id"]),
        biNgungHoatDong = Convert.ToInt32(r["bi_ngung_hoat_dong"]) == 1,
    };
}
