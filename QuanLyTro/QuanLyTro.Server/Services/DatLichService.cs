using System.Text.Json;
using QuanLyTro.Server.Repositories;
using QuanLyTro.Shared.Models;

namespace QuanLyTro.Server.Services;

/// <summary>
/// Nghiệp vụ đặt lịch xem phòng public (không cần đăng nhập):
/// - PHONG_LAY_TRONG_PUBLIC: chỉ trả thông tin phòng trống an toàn (phongId, soPhong, tenKhu, giaThue).
/// - KHACH_THUE_DAT_LICH: kiểm tra phòng trống, thời gian tương lai, BR tối đa 3 yêu cầu chờ duyệt/ngày/SĐT.
/// </summary>
public sealed class DatLichService
{
    private readonly DatLichRepository _repo;
    private readonly RoomRepository _rooms;

    public DatLichService(DatLichRepository repo, RoomRepository rooms)
    {
        _repo = repo;
        _rooms = rooms;
    }

    public async Task<object> GetVacantRoomsPublicAsync()
    {
        var rows = await _repo.VacantRoomsPublicAsync();
        return rows.Select(r => new
        {
            phongId = Convert.ToInt32(r["phong_id"]),
            soPhong = (string)r["so_phong"]!,
            tenKhu = (string)r["ten_khu"]!,
            giaThue = Convert.ToDecimal(r["gia_thue"]),
        }).ToArray();
    }

    public async Task<object> BookPublicAsync(JsonElement data)
    {
        var phongId = AccountService.GetInt(data, "phongId");
        var hoTen = AccountService.GetString(data, "hoTen")?.Trim()
            ?? throw new ValidationException("Thiếu họ tên người đặt lịch.");
        var soDienThoai = AccountService.GetString(data, "soDienThoai")?.Trim()
            ?? throw new ValidationException("Thiếu số điện thoại.");
        var thoiGianStr = AccountService.GetString(data, "thoiGian")?.Trim()
            ?? throw new ValidationException("Thiếu thời gian xem phòng.");

        if (string.IsNullOrWhiteSpace(hoTen) || hoTen.Length > 100)
            throw new ValidationException("Họ tên không hợp lệ.");
        if (soDienThoai.Length < 9 || soDienThoai.Length > 15 || !soDienThoai.All(c => char.IsAsciiDigit(c) || c == '+'))
            throw new ValidationException("Số điện thoại không hợp lệ.");
        if (!DateTime.TryParse(thoiGianStr, out var thoiGian))
            throw new ValidationException("Thời gian xem phòng không đúng định dạng.");
        if (thoiGian <= DateTime.Now)
            throw new ValidationException("Thời gian hẹn xem phòng phải ở tương lai.");

        var phong = await _rooms.RoomByIdAsync(phongId) ?? throw new NotFoundOrDeniedException();
        if ((string)phong["trang_thai"]! != "Trong")
            throw new BusinessRuleException("Phòng không còn trống để đặt lịch xem.");

        // BR: không quá 3 yêu cầu CHỜ DUYỆT/ngày từ một số điện thoại
        var pendingCount = await _repo.CountPendingTodayByPhoneAsync(soDienThoai);
        if (pendingCount >= 3)
            throw new BusinessRuleException("Mỗi số điện thoại không được quá 3 yêu cầu chờ duyệt trong một ngày.");

        var (datLichId, lichKiemTraId) = await _repo.CreateBookingAsync(phongId, hoTen, soDienThoai, thoiGian);
        return new
        {
            id = lichKiemTraId,
            datLichId,
            lichId = lichKiemTraId,
            phongId,
            trangThai = "ChoDuyet",
        };
    }
}
