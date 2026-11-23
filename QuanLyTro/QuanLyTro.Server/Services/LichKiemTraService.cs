using System.Text.Json;
using QuanLyTro.Server.Repositories;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;

namespace QuanLyTro.Server.Services;

/// <summary>
/// Nghiệp vụ lịch kiểm tra phòng (M5):
/// - Lễ tân / Chủ trọ / Quản lý (scope khu): xem danh sách, duyệt, từ chối, lấy danh sách nhân viên, gán nhân viên.
/// - Nhân viên: xem việc của mình, nhận việc (DaDuyet -> DangLam), hoàn thành ghi kết quả (DangLam -> HoanThanh).
/// </summary>
public sealed class LichKiemTraService
{
    private readonly LichKiemTraRepository _repo;

    public LichKiemTraService(LichKiemTraRepository repo)
    {
        _repo = repo;
    }

    public async Task<object> ListAsync(SessionInfo session)
    {
        RequireCoordinator(session);
        var khuTroId = session.VaiTro == VaiTroNguoiDung.QuanLy ? session.KhuTroId : null;
        var rows = await _repo.ListAsync(khuTroId);
        return rows.Select(MapDto).ToArray();
    }

    public async Task<object> ApproveAsync(SessionInfo session, JsonElement data)
    {
        RequireCoordinator(session);
        var lichId = AccountService.GetInt(data, "lichId");
        var row = await _repo.FindByIdOrDatLichAsync(lichId) ?? throw new NotFoundOrDeniedException();
        RequireRoomScope(session, row);

        var trangThai = (string)row["trang_thai"]!;
        if (trangThai != "ChoDuyet")
            throw new BusinessRuleException("Chỉ có thể duyệt lịch đang ở trạng thái chờ duyệt.");

        var lktId = Convert.ToInt32(row["id"]);
        var dlId = row["dat_lich_id"] is null or DBNull ? (int?)null : Convert.ToInt32(row["dat_lich_id"]);
        var n = await _repo.ApproveAsync(lktId, dlId);
        if (n == 0) throw new BusinessRuleException("Không thể duyệt lịch kiểm tra.");

        return new
        {
            id = lktId,
            lichId = lktId,
            trangThai = "DaDuyet",
        };
    }

    public async Task<object> RejectAsync(SessionInfo session, JsonElement data)
    {
        RequireCoordinator(session);
        var lichId = AccountService.GetInt(data, "lichId");
        var lyDo = (AccountService.GetString(data, "lyDoTuChoi") ?? AccountService.GetString(data, "lyDo"))?.Trim();
        if (lyDo is not null && lyDo.Length > 500)
            throw new ValidationException("Lý do từ chối không được vượt quá 500 ký tự.");

        var row = await _repo.FindByIdOrDatLichAsync(lichId) ?? throw new NotFoundOrDeniedException();
        RequireRoomScope(session, row);

        var trangThai = (string)row["trang_thai"]!;
        if (trangThai is not ("ChoDuyet" or "DaDuyet"))
            throw new BusinessRuleException("Không thể từ chối lịch đang làm hoặc đã hoàn thành.");

        var lktId = Convert.ToInt32(row["id"]);
        var dlId = row["dat_lich_id"] is null or DBNull ? (int?)null : Convert.ToInt32(row["dat_lich_id"]);
        var n = await _repo.RejectAsync(lktId, dlId, lyDo);
        if (n == 0) throw new BusinessRuleException("Không thể từ chối lịch kiểm tra.");

        return new
        {
            id = lktId,
            lichId = lktId,
            trangThai = "TuChoi",
            lyDoTuChoi = lyDo,
        };
    }

    public async Task<object> ListEmployeesAsync(SessionInfo session)
    {
        RequireCoordinator(session);
        var rows = await _repo.ListEmployeesAsync();
        return rows.Select(r => new
        {
            id = Convert.ToInt32(r["id"]),
            tenDangNhap = (string)r["ten_dang_nhap"]!,
            hoTen = (string)r["ho_ten"]!,
            chuyenMon = r["chuyen_mon"] as string ?? "",
        }).ToArray();
    }

    public async Task<object> AssignEmployeeAsync(SessionInfo session, JsonElement data)
    {
        RequireCoordinator(session);
        var lichId = AccountService.GetInt(data, "lichId");
        var nhanVienId = AccountService.GetInt(data, "nhanVienId");

        var row = await _repo.FindByIdOrDatLichAsync(lichId) ?? throw new NotFoundOrDeniedException();
        RequireRoomScope(session, row);

        var trangThai = (string)row["trang_thai"]!;
        if (trangThai is not ("ChoDuyet" or "DaDuyet"))
            throw new BusinessRuleException("Chỉ có thể gán nhân viên cho lịch chờ duyệt hoặc đã duyệt.");

        var nv = await _repo.GetEmployeeByIdAsync(nhanVienId)
            ?? throw new ValidationException("Nhân viên được chọn không hợp lệ.");

        var lktId = Convert.ToInt32(row["id"]);
        var dlId = row["dat_lich_id"] is null or DBNull ? (int?)null : Convert.ToInt32(row["dat_lich_id"]);
        var n = await _repo.AssignEmployeeAsync(lktId, dlId, nhanVienId);
        if (n == 0) throw new BusinessRuleException("Không thể gán nhân viên cho lịch kiểm tra.");

        return new
        {
            id = lktId,
            lichId = lktId,
            nhanVienId,
            nhanVienTen = (string)nv["ho_ten"]!,
            trangThai = "DaDuyet",
        };
    }

    public async Task<object> ListMineAsync(SessionInfo session)
    {
        if (session.VaiTro != VaiTroNguoiDung.NhanVien)
            throw new NotFoundOrDeniedException();
        var rows = await _repo.ListByEmployeeAsync(session.TaiKhoanId);
        return rows.Select(MapDto).ToArray();
    }

    public async Task<object> AcceptTaskAsync(SessionInfo session, JsonElement data)
    {
        if (session.VaiTro != VaiTroNguoiDung.NhanVien)
            throw new NotFoundOrDeniedException();
        var lichId = AccountService.GetInt(data, "lichId");
        var row = await _repo.FindByIdOrDatLichAsync(lichId) ?? throw new NotFoundOrDeniedException();

        var assignedNv = row["nhan_vien_id"] is null or DBNull ? (int?)null : Convert.ToInt32(row["nhan_vien_id"]);
        if (assignedNv != session.TaiKhoanId)
            throw new NotFoundOrDeniedException();

        var trangThai = (string)row["trang_thai"]!;
        if (trangThai != "DaDuyet")
            throw new BusinessRuleException("Chỉ có thể nhận việc đang ở trạng thái đã duyệt.");

        var lktId = Convert.ToInt32(row["id"]);
        var n = await _repo.AcceptTaskAsync(lktId, session.TaiKhoanId);
        if (n == 0) throw new BusinessRuleException("Không thể nhận việc.");

        return new
        {
            id = lktId,
            lichId = lktId,
            trangThai = "DangLam",
        };
    }

    public async Task<object> CompleteTaskAsync(SessionInfo session, JsonElement data)
    {
        if (session.VaiTro != VaiTroNguoiDung.NhanVien)
            throw new NotFoundOrDeniedException();
        var lichId = AccountService.GetInt(data, "lichId");
        var ketQua = AccountService.GetString(data, "ketQua")?.Trim()
            ?? throw new ValidationException("Thiếu kết quả kiểm tra.");
        if (string.IsNullOrWhiteSpace(ketQua))
            throw new ValidationException("Kết quả kiểm tra không được để trống.");
        if (ketQua.Length > 1000)
            throw new ValidationException("Kết quả kiểm tra không được vượt quá 1000 ký tự.");

        var row = await _repo.FindByIdOrDatLichAsync(lichId) ?? throw new NotFoundOrDeniedException();
        var assignedNv = row["nhan_vien_id"] is null or DBNull ? (int?)null : Convert.ToInt32(row["nhan_vien_id"]);
        if (assignedNv != session.TaiKhoanId)
            throw new NotFoundOrDeniedException();

        var trangThai = (string)row["trang_thai"]!;
        if (trangThai != "DangLam")
            throw new BusinessRuleException("Phải nhận việc trước khi hoàn thành.");

        var lktId = Convert.ToInt32(row["id"]);
        var n = await _repo.CompleteTaskAsync(lktId, session.TaiKhoanId, ketQua);
        if (n == 0) throw new BusinessRuleException("Không thể hoàn thành công việc.");

        return new
        {
            id = lktId,
            lichId = lktId,
            trangThai = "HoanThanh",
            ketQua,
        };
    }

    private static void RequireCoordinator(SessionInfo session)
    {
        if (session.VaiTro is not (VaiTroNguoiDung.ChuTro or VaiTroNguoiDung.QuanLy or VaiTroNguoiDung.LeTan))
            throw new NotFoundOrDeniedException();
    }

    private static void RequireRoomScope(SessionInfo session, Dictionary<string, object?> row)
    {
        if (session.VaiTro != VaiTroNguoiDung.QuanLy) return;
        var khuTroId = Convert.ToInt32(row["khu_tro_id"]);
        if (session.KhuTroId != khuTroId)
            throw new NotFoundOrDeniedException();
    }

    private static object MapDto(Dictionary<string, object?> r) => new
    {
        id = Convert.ToInt32(r["id"]),
        phongId = Convert.ToInt32(r["phong_id"]),
        datLichId = r["dat_lich_id"] is null or DBNull ? (int?)null : Convert.ToInt32(r["dat_lich_id"]),
        soPhong = (string)r["so_phong"]!,
        tenKhu = r.TryGetValue("ten_khu", out var tk) && tk is string s ? s : "",
        hoTen = r["ho_ten"] as string ?? "",
        soDienThoai = r["so_dien_thoai"] as string ?? "",
        thoiGian = Convert.ToDateTime(r["thoi_gian"]!).ToString("yyyy-MM-ddTHH:mm:ss"),
        trangThai = (string)r["trang_thai"]!,
        nhanVienId = r["nhan_vien_id"] is null or DBNull ? (int?)null : Convert.ToInt32(r["nhan_vien_id"]),
        nhanVienTen = r.TryGetValue("nhan_vien_ten", out var nvt) ? nvt as string : null,
        ketQua = r["ket_qua"] as string,
        lyDoTuChoi = r["ly_do_tu_choi"] as string,
    };
}
