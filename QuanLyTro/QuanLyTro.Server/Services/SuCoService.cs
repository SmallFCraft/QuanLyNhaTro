using System.Text.Json;
using QuanLyTro.Server.Repositories;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;
using QuanLyTro.Shared.Protocol;

namespace QuanLyTro.Server.Services;

/// <summary>
/// Nghiệp vụ sự cố: báo / xem / cập nhật trạng thái.
/// KhachThue chỉ báo phòng mình đang ở. Scope khu lấy từ session.
/// </summary>
public sealed class SuCoService
{
    private static readonly string[] MucDoHopLe = ["Thap", "TrungBinh", "Cao", "KhanCap"];
    private static readonly string[] TrangThaiHopLe = ["Moi", "DangXuLy", "DaXuLy"];

    private readonly SuCoRepository _repo;
    private readonly RoomRepository _roomRepo;
    private readonly TenantRepository _tenantRepo;

    public SuCoService(SuCoRepository repo, RoomRepository roomRepo, TenantRepository tenantRepo)
    {
        _repo = repo;
        _roomRepo = roomRepo;
        _tenantRepo = tenantRepo;
    }

    public async Task<object> TaoAsync(SessionInfo session, JsonElement data)
    {
        var moTa = AccountService.GetString(data, "moTa") ?? throw new ValidationException("Thiếu mô tả sự cố.");
        moTa = moTa.Trim();
        if (string.IsNullOrEmpty(moTa)) throw new ValidationException("Mô tả sự cố không được để trống.");
        if (moTa.Length > 1000) throw new ValidationException("Mô tả tối đa 1000 ký tự.");
        var mucDo = AccountService.GetString(data, "mucDo") ?? "TrungBinh";
        if (!MucDoHopLe.Contains(mucDo)) throw new ValidationException($"Mức độ không hợp lệ: {mucDo}.");

        // Xác định phòng báo cáo
        int phongId;
        if (session.VaiTro == VaiTroNguoiDung.KhachThue)
        {
            // KhachThue chỉ báo phòng mình đang ở; nếu gửi phongId phòng khác → từ chối
            var profile = await _tenantRepo.ProfileAsync(session.TaiKhoanId)
                ?? throw new NotFoundOrDeniedException();
            if (profile["phong_id"] is null or DBNull)
                throw new BusinessRuleException("Bạn chưa ở phòng nào, không thể báo sự cố.");
            var myPhongId = Convert.ToInt32(profile["phong_id"]);
            var requestedPhongId = AccountService.GetOptionalInt(data, "phongId");
            if (requestedPhongId.HasValue && requestedPhongId.Value != myPhongId)
                throw new NotFoundOrDeniedException();
            phongId = myPhongId;
        }
        else
        {
            // Các vai trò khác nhận phongId từ payload và kiểm tra scope QuanLy
            phongId = AccountService.GetInt(data, "phongId");
            var phong = await _roomRepo.RoomByIdAsync(phongId) ?? throw new NotFoundOrDeniedException();
            if (session.VaiTro == VaiTroNguoiDung.QuanLy &&
                session.KhuTroId != Convert.ToInt32(phong["khu_tro_id"]))
                throw new NotFoundOrDeniedException();
        }

        var id = await _repo.InsertAsync(phongId, session.TaiKhoanId, mucDo, moTa);
        return new { suCoId = id };
    }

    public async Task<object> LayTheoKhuAsync(SessionInfo session, JsonElement data)
    {
        // QuanLy chưa được gán khu → không xem được khu nào
        if (session.VaiTro == VaiTroNguoiDung.QuanLy && session.KhuTroId is null)
            return Array.Empty<object>();

        // QuanLy chỉ xem khu của session; ChuTro/LeTan/NhanVien xem tất cả hoặc lọc theo khuTroId
        int? khuId = session.VaiTro == VaiTroNguoiDung.QuanLy
            ? session.KhuTroId
            : AccountService.GetOptionalInt(data, "khuTroId");

        var rows = await _repo.LayTheoKhuAsync(khuId);
        return rows.Select(r => new
        {
            id = Convert.ToInt32(r["id"]),
            phongId = Convert.ToInt32(r["phong_id"]),
            soPhong = r["so_phong"] as string,
            khuTroId = Convert.ToInt32(r["khu_tro_id"]),
            tenKhuTro = r["ten_khu_tro"] as string,
            mucDo = (string)r["muc_do"]!,
            trangThai = (string)r["trang_thai"]!,
            moTa = (string)r["mo_ta"]!,
            ketQua = r["ket_qua"] as string,
            tenNguoiBao = r["ten_nguoi_bao"] as string,
            taoLuc = Convert.ToDateTime(r["tao_luc"]).ToString("yyyy-MM-dd HH:mm"),
            xuLyXongLuc = r["xu_ly_xong_luc"] is null or DBNull
                ? null
                : Convert.ToDateTime(r["xu_ly_xong_luc"]).ToString("yyyy-MM-dd HH:mm"),
        }).ToArray();
    }

    public async Task<object> CapNhatTrangThaiAsync(SessionInfo session, JsonElement data)
    {
        if (session.VaiTro is not (VaiTroNguoiDung.ChuTro or VaiTroNguoiDung.QuanLy))
            throw new NotFoundOrDeniedException();

        var suCoId = AccountService.GetInt(data, "suCoId");
        var trangThai = AccountService.GetString(data, "trangThai")
            ?? throw new ValidationException("Thiếu trạng thái.");
        if (!TrangThaiHopLe.Contains(trangThai))
            throw new ValidationException($"Trạng thái không hợp lệ: {trangThai}.");

        var sc = await _repo.GetByIdAsync(suCoId) ?? throw new NotFoundOrDeniedException();

        // QuanLy chỉ cập nhật sự cố trong khu mình
        if (session.VaiTro == VaiTroNguoiDung.QuanLy)
        {
            var khuTroId = sc["khu_tro_id"] is null or DBNull
                ? (int?)null
                : Convert.ToInt32(sc["khu_tro_id"]);
            if (khuTroId != session.KhuTroId) throw new NotFoundOrDeniedException();
        }

        var ketQuaRaw = AccountService.GetString(data, "ketQua");
        var ketQua = string.IsNullOrWhiteSpace(ketQuaRaw) ? null : ketQuaRaw.Trim();
        if (ketQua is not null && ketQua.Length > 1000)
            throw new ValidationException("Kết quả xử lý tối đa 1000 ký tự.");
        var affected = await _repo.CapNhatTrangThaiAsync(suCoId, trangThai, ketQua);
        if (affected == 0) throw new BusinessRuleException("Không thể cập nhật trạng thái sự cố.");
        return new { ok = true };
    }
}
