using QuanLyTro.Server.Data;
using System.Text.Json;
using System.Globalization;
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
    private readonly HopDongRepository _hopDongRepo;
    private readonly Database _db;

    public DeXuatService(DeXuatRepository repo, RoomRepository roomRepo, TenantRepository tenantRepo, HopDongRepository hopDongRepo)
    {
        _repo = repo;
        _roomRepo = roomRepo;
        _tenantRepo = tenantRepo;
        _hopDongRepo = hopDongRepo;
        _db = hopDongRepo.Db;
    }

    public async Task<object> TaoAsync(SessionInfo session, JsonElement data)
    {
        // ChuTro không tự gửi đề xuất cho chính mình duyệt
        if (session.VaiTro == VaiTroNguoiDung.ChuTro)
            throw new BusinessRuleException("Chủ trọ không gửi đề xuất — trực tiếp thực hiện thao tác.");

        var loai = AccountService.GetString(data, "loai") ?? throw new ValidationException("Thiếu loại đề xuất.");
        if (loai == "XoaHoaDon")
        {
            var hoaDonId = AccountService.GetOptionalInt(data, "hoaDonId")
                ?? throw new ValidationException("Thiếu hóa đơn cần xóa.");
            var trangThai = await _db.ScalarT<string>("SELECT trang_thai FROM hoa_don WHERE id=@id", new { id = hoaDonId });
            if (trangThai is null) throw new NotFoundOrDeniedException();
            if (trangThai == "DaThu")
                throw new BusinessRuleException("Hóa đơn đã thu là dữ liệu bất biến, không thể lập đề xuất xóa.");
            throw new BusinessRuleException("Hóa đơn chưa thu cần Chủ trọ duyệt; thao tác xóa đang được bổ sung.");
        }
        if (!LoaiHopLe.Contains(loai)) throw new ValidationException($"Loại đề xuất không hợp lệ: {loai}.");
        var noiDung = AccountService.GetString(data, "noiDung") ?? throw new ValidationException("Thiếu nội dung đề xuất.");
        noiDung = noiDung.Trim();
        if (string.IsNullOrEmpty(noiDung)) throw new ValidationException("Nội dung đề xuất không được để trống.");
        if (noiDung.Length > 1000) throw new ValidationException("Nội dung đề xuất tối đa 1000 ký tự.");

        // Suy khu_tro_id từ session hoặc phòng đang ở — KHÔNG tin payload
        int? khuTroId = session.VaiTro == VaiTroNguoiDung.QuanLy
            ? session.KhuTroId
            : await LayKhuTuNguoiGuiAsync(session.TaiKhoanId);

        int? doiTuongId = null;
        string? duLieu = null;
        if (loai == "ChamDutHopDong")
        {
            doiTuongId = AccountService.GetOptionalInt(data, "hopDongId")
                ?? throw new ValidationException("Thiếu hợp đồng cần chấm dứt.");
            var ngayStr = AccountService.GetString(data, "ngayBanGiao")
                ?? throw new ValidationException("Thiếu ngày bàn giao dự kiến.");
            if (!DateOnly.TryParse(ngayStr, out var ngayBanGiao) ||
                ngayBanGiao < DateOnly.FromDateTime(DateTime.Today).AddDays(30))
                throw new ValidationException("Ngày bàn giao dự kiến phải ít nhất 30 ngày kể từ hôm nay.");
            if (!await _hopDongRepo.ExistsInScopeAsync(doiTuongId.Value, khuTroId))
                throw new NotFoundOrDeniedException();
            duLieu = JsonSerializer.Serialize(new { ngayBanGiao = ngayBanGiao.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
        }
        // Ràng buộc đặc tả MOD-04-06: không cho hai đề xuất chờ duyệt trùng loại + đối tượng
        if (await _repo.DemChoDuyetTrungAsync(loai, doiTuongId) > 0)
            throw new BusinessRuleException("Đã có đề xuất chờ duyệt cho đối tượng này. Vui lòng chờ đề xuất cũ được xử lý.");
        var id = await _repo.InsertAsync(loai, noiDung, session.TaiKhoanId, khuTroId, doiTuongId, duLieu);
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
        var id = AccountService.GetInt(data, "deXuatId");
        var phanHoiRaw = AccountService.GetString(data, "phanHoi");
        var phanHoi = string.IsNullOrWhiteSpace(phanHoiRaw) ? null : phanHoiRaw.Trim();
        if (phanHoi is not null && phanHoi.Length > 500)
            throw new ValidationException("Phản hồi tối đa 500 ký tự.");

        var (conn, tx) = await _db.BeginAsync();
        try
        {
            var dx = await _repo.GetByIdForUpdateAsync(conn, tx, id) ?? throw new NotFoundOrDeniedException();
            if ((string)dx["trang_thai"]! != "ChoDuyet")
                throw new BusinessRuleException("Đề xuất đã được xử lý trước đó.");

            // Duyệt ChamDutHopDong phải gọi đúng nghiệp vụ gốc trong cùng transaction
            if ((string)dx["loai"]! == "ChamDutHopDong")
                await ThucThiChamDutAsync(conn, tx, dx, session);

            var affected = await _repo.CapNhatTrangThaiAsync(conn, tx, id, "DaDuyet", session.TaiKhoanId, phanHoi);
            if (affected == 0) throw new BusinessRuleException("Đề xuất không còn ở trạng thái Chờ duyệt.");
            await tx.CommitAsync();
            return new { ok = true, trangThai = "DaDuyet" };
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    private async Task ThucThiChamDutAsync(MySqlConnector.MySqlConnection conn, MySqlConnector.MySqlTransaction tx,
        Dictionary<string, object?> dx, SessionInfo session)
    {
        var hopDongId = dx["doi_tuong_id"] is null or DBNull
            ? throw new BusinessRuleException("Đề xuất thiếu hợp đồng đích, không thể thực thi.")
            : Convert.ToInt32(dx["doi_tuong_id"]);
        var ngayBanGiao = DateOnly.FromDateTime(DateTime.Today).AddDays(30);
        if (dx["du_lieu"] is string json)
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ngayBanGiao", out var el) && el.ValueKind == JsonValueKind.String &&
                DateOnly.TryParse(el.GetString(), out var parsed))
                ngayBanGiao = parsed;
        }
        if (ngayBanGiao < DateOnly.FromDateTime(DateTime.Today).AddDays(30))
            throw new BusinessRuleException("Ngày bàn giao dự kiến không còn đủ 30 ngày kể từ hôm nay.");
        await _hopDongRepo.ChamDutTrongTxAsync(conn, tx, hopDongId,
            $"Theo đề xuất #{dx["id"]} đã duyệt", ngayBanGiao, session.TaiKhoanId);
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
