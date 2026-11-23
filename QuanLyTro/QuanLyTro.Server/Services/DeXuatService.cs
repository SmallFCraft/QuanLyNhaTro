using System.Text.Json;
using QuanLyTro.Server.Repositories;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;
using QuanLyTro.Shared.Protocol;

namespace QuanLyTro.Server.Services;

/// <summary>
/// Nghiệp vụ đề xuất chờ duyệt. Scope khu lấy từ session, không tin payload.
/// ChuTro xem/duyệt tất cả; QuanLy xem khu mình + của mình; vai trò khác chỉ xem của mình.
/// </summary>
public sealed class DeXuatService
{
    private static readonly string[] LoaiHopLe =
        ["ChamDutHopDong", "SuaGia", "MuaSam", "SuaChuaLon", "Khac"];

    private readonly DeXuatRepository _repo;
    private readonly RoomRepository _roomRepo;
    private readonly TenantRepository _tenantRepo;

    public DeXuatService(DeXuatRepository repo, RoomRepository roomRepo, TenantRepository tenantRepo)
    {
        _repo = repo;
        _roomRepo = roomRepo;
        _tenantRepo = tenantRepo;
    }

    public async Task<object> TaoAsync(SessionInfo session, JsonElement data)
    {
        // ChuTro không tự gửi đề xuất cho chính mình duyệt
        if (session.VaiTro == VaiTroNguoiDung.ChuTro)
            throw new BusinessRuleException("Chủ trọ không gửi đề xuất — trực tiếp thực hiện thao tác.");

        var loai = AccountService.GetString(data, "loai") ?? throw new ValidationException("Thiếu loại đề xuất.");
        if (!LoaiHopLe.Contains(loai)) throw new ValidationException($"Loại đề xuất không hợp lệ: {loai}.");
        var noiDung = AccountService.GetString(data, "noiDung") ?? throw new ValidationException("Thiếu nội dung đề xuất.");
        noiDung = noiDung.Trim();
        if (string.IsNullOrEmpty(noiDung)) throw new ValidationException("Nội dung đề xuất không được để trống.");
        if (noiDung.Length > 1000) throw new ValidationException("Nội dung đề xuất tối đa 1000 ký tự.");

        // Suy khu_tro_id từ session hoặc phòng đang ở — KHÔNG tin payload
        int? khuTroId = session.VaiTro == VaiTroNguoiDung.QuanLy
            ? session.KhuTroId
            : await LayKhuTuNguoiGuiAsync(session.TaiKhoanId);

        var id = await _repo.InsertAsync(loai, noiDung, session.TaiKhoanId, khuTroId);
        return new { deXuatId = id };
    }

    public async Task<object> LayTatCaAsync(SessionInfo session)
    {
        var rows = await _repo.LayTheoVaiTroAsync(
            session.VaiTro.ToString(), session.TaiKhoanId, session.KhuTroId);

        // Pre-compute badge count if needed
        int? soChoDuyet = null;
        if (session.VaiTro == VaiTroNguoiDung.ChuTro)
        {
            soChoDuyet = await _repo.DemChoDuyetAsync();
        }

        var result = rows.Select(r => new
        {
            id = Convert.ToInt32(r["id"]),
            loai = (string)r["loai"]!,
            noiDung = (string)r["noi_dung"]!,
            nguoiGuiId = Convert.ToInt32(r["nguoi_gui_id"]),
            tenNguoiGui = r["ten_nguoi_gui"] as string,
            khuTroId = r["khu_tro_id"] is null or DBNull ? (int?)null : Convert.ToInt32(r["khu_tro_id"]),
            tenKhuTro = r["ten_khu_tro"] as string,
            trangThai = (string)r["trang_thai"]!,
            nguoiDuyetId = r["nguoi_duyet_id"] is null or DBNull ? (int?)null : Convert.ToInt32(r["nguoi_duyet_id"]),
            tenNguoiDuyet = r["ten_nguoi_duyet"] as string,
            phanHoi = r["phan_hoi"] as string,
            taoLuc = Convert.ToDateTime(r["tao_luc"]).ToString("yyyy-MM-dd HH:mm"),
            // badge: đếm ChoDuyệt cho dashboard ChuTro
            soChoDuyet = soChoDuyet,
        }).ToArray();

        return result;
    }

    public async Task<object> DuyetAsync(SessionInfo session, JsonElement data)
    {
        if (session.VaiTro != VaiTroNguoiDung.ChuTro) throw new NotFoundOrDeniedException();
        return await CapNhatTrangThaiAsync(session, data, "DaDuyet");
    }

    public async Task<object> TuChoiAsync(SessionInfo session, JsonElement data)
    {
        if (session.VaiTro != VaiTroNguoiDung.ChuTro) throw new NotFoundOrDeniedException();
        return await CapNhatTrangThaiAsync(session, data, "TuChoi");
    }

    // ── private helpers ──────────────────────────────────────────────────────

    private async Task<object> CapNhatTrangThaiAsync(SessionInfo session, JsonElement data, string trangThai)
    {
        var id = AccountService.GetInt(data, "deXuatId");
        var phanHoiRaw = AccountService.GetString(data, "phanHoi");
        var phanHoi = string.IsNullOrWhiteSpace(phanHoiRaw) ? null : phanHoiRaw.Trim();
        if (phanHoi is not null && phanHoi.Length > 500)
            throw new ValidationException("Phản hồi tối đa 500 ký tự.");

        var dx = await _repo.GetByIdAsync(id) ?? throw new NotFoundOrDeniedException();
        if ((string)dx["trang_thai"]! != "ChoDuyet")
            throw new BusinessRuleException("Đề xuất đã được xử lý trước đó.");

        var affected = await _repo.CapNhatTrangThaiAsync(id, trangThai, session.TaiKhoanId, phanHoi);
        if (affected == 0) throw new BusinessRuleException("Đề xuất không còn ở trạng thái Chờ duyệt.");
        return new { ok = true, trangThai };
    }

    /// <summary>Lấy khu_tro_id từ phòng khách thuê đang ở (nullable).</summary>
    private async Task<int?> LayKhuTuNguoiGuiAsync(int taiKhoanId)
    {
        var profile = await _tenantRepo.ProfileAsync(taiKhoanId);
        if (profile is null || profile["phong_id"] is null or DBNull) return null;
        var phongId = Convert.ToInt32(profile["phong_id"]);
        var phong = await _roomRepo.RoomByIdAsync(phongId);
        return phong is null ? null : Convert.ToInt32(phong["khu_tro_id"]);
    }
}
