using System.Text.Json;
using System.Text.RegularExpressions;
using QuanLyTro.Server.Data;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;
using QuanLyTro.Shared.Protocol;

namespace QuanLyTro.Server.Services;

/// <summary>
/// Báo cáo tổng quan hệ thống — CHỈ ChuTro thấy số tiền doanh thu.
/// QuanLy gọi action này bị từ chối tại RBAC (không trong BuildMatrix).
/// </summary>
public sealed class BaoCaoService
{
    private static readonly Regex KyCuocRegex = new(@"^\d{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);
    private readonly Database _db;

    public BaoCaoService(Database db) => _db = db;

    public async Task<object> TongQuanAsync(SessionInfo session, JsonElement data)
    {
        if (session.VaiTro != VaiTroNguoiDung.ChuTro)
            throw new NotFoundOrDeniedException(); // fail-safe thêm tầng 2

        var tuThang = AccountService.GetString(data, "tuThang");
        var denThang = AccountService.GetString(data, "denThang");
        var kyCuoc = AccountService.GetString(data, "kyCuoc");

        if (!string.IsNullOrWhiteSpace(tuThang) && !KyCuocRegex.IsMatch(tuThang))
            throw new ValidationException("tuThang phải theo định dạng yyyy-MM.");
        if (!string.IsNullOrWhiteSpace(denThang) && !KyCuocRegex.IsMatch(denThang))
            throw new ValidationException("denThang phải theo định dạng yyyy-MM.");
        if (!string.IsNullOrWhiteSpace(kyCuoc) && !KyCuocRegex.IsMatch(kyCuoc))
            throw new ValidationException("kyCuoc phải theo định dạng yyyy-MM.");

        // KPI phòng
        var phongRows = await _db.QueryAsync(
            @"SELECT trang_thai, COUNT(*) AS so_luong FROM phong GROUP BY trang_thai");
        int tongPhong = 0, phongTrong = 0, phongDangThue = 0, phongBaoTri = 0;
        foreach (var r in phongRows)
        {
            var sl = Convert.ToInt32(r["so_luong"]);
            tongPhong += sl;
            switch ((string)r["trang_thai"]!)
            {
                case "Trong": phongTrong = sl; break;
                case "DaThue": phongDangThue = sl; break;
                case "BaoTri": phongBaoTri = sl; break;
            }
        }

        // KPI khu & khách hiện tại
        var tongKhu = await _db.ScalarT<int>(
            "SELECT COUNT(*) FROM khu_tro WHERE bi_ngung_hoat_dong = 0");
        var khachHienTai = await _db.ScalarT<int>(
            "SELECT COUNT(*) FROM ho_so_khach_thue WHERE phong_id IS NOT NULL");

        // Tỷ lệ lấp đầy
        var tyLe = tongPhong > 0
            ? Math.Round((double)phongDangThue / tongPhong * 100, 1)
            : 0.0;

        // Phòng thủ: bảng hoa_don có thể chưa merge hoặc chưa có dữ liệu → không crash
        var (tongHoaDonChuaThu, soTienDaThu, soTienChuaThu) = await LayThongKeHoaDonAsync(kyCuoc);
        var doanhThu6Thang = await LayDoanhThu6ThangAsync(tuThang, denThang);

        return new
        {
            tongKhu,
            tongPhong,
            phongTrong,
            phongDangThue,
            phongBaoTri,
            tyLeLapDay = tyLe,
            tongHoaDonChuaThu,
            doanhThu6Thang,
            // Trường bổ sung tương thích shell legacy (dash.js / reports.js)
            tongSoPhong = tongPhong,
            phongDaThue = phongDangThue,
            khachHienTai,
            soTienDaThu,
            soTienChuaThu,
        };
    }

    private async Task<(int TongChuaThu, long SoTienDaThu, long SoTienChuaThu)> LayThongKeHoaDonAsync(string? kyCuoc)
    {
        try
        {
            var tongChuaThu = await _db.ScalarT<int>(
                "SELECT COUNT(*) FROM hoa_don WHERE trang_thai = 'ChuaThu'");

            var ky = string.IsNullOrWhiteSpace(kyCuoc)
                ? DateTime.Now.ToString("yyyy-MM")
                : kyCuoc;

            var rows = await _db.QueryAsync(
                @"SELECT trang_thai, COALESCE(SUM(tong_tien), 0) AS tong
                  FROM hoa_don
                  WHERE ky_cuoc = @ky
                  GROUP BY trang_thai",
                new { ky });

            long daThu = 0, chuaThu = 0;
            foreach (var r in rows)
            {
                var st = (string)r["trang_thai"]!;
                var val = Convert.ToInt64(r["tong"]);
                if (st == "DaThu") daThu = val;
                else if (st == "ChuaThu") chuaThu = val;
            }
            return (tongChuaThu, daThu, chuaThu);
        }
        catch
        {
            return (0, 0, 0);
        }
    }

    private async Task<object[]> LayDoanhThu6ThangAsync(string? tuThang, string? denThang)
    {
        try
        {
            List<Dictionary<string, object?>> rows;
            if (!string.IsNullOrWhiteSpace(tuThang) && !string.IsNullOrWhiteSpace(denThang))
            {
                rows = await _db.QueryAsync(
                    @"SELECT ky_cuoc, COALESCE(SUM(tong_tien), 0) AS tong_thu
                      FROM hoa_don
                      WHERE trang_thai = 'DaThu'
                        AND ky_cuoc >= @tu AND ky_cuoc <= @den
                      GROUP BY ky_cuoc
                      ORDER BY ky_cuoc ASC",
                    new { tu = tuThang, den = denThang });
            }
            else if (!string.IsNullOrWhiteSpace(tuThang))
            {
                rows = await _db.QueryAsync(
                    @"SELECT ky_cuoc, COALESCE(SUM(tong_tien), 0) AS tong_thu
                      FROM hoa_don
                      WHERE trang_thai = 'DaThu'
                        AND ky_cuoc >= @tu
                      GROUP BY ky_cuoc
                      ORDER BY ky_cuoc ASC",
                    new { tu = tuThang });
            }
            else if (!string.IsNullOrWhiteSpace(denThang))
            {
                rows = await _db.QueryAsync(
                    @"SELECT ky_cuoc, COALESCE(SUM(tong_tien), 0) AS tong_thu
                      FROM hoa_don
                      WHERE trang_thai = 'DaThu'
                        AND ky_cuoc <= @den
                      GROUP BY ky_cuoc
                      ORDER BY ky_cuoc ASC",
                    new { den = denThang });
            }
            else
            {
                rows = await _db.QueryAsync(
                    @"SELECT ky_cuoc, COALESCE(SUM(tong_tien), 0) AS tong_thu
                      FROM hoa_don
                      WHERE trang_thai = 'DaThu'
                        AND ky_cuoc >= DATE_FORMAT(DATE_SUB(NOW(), INTERVAL 5 MONTH), '%Y-%m')
                      GROUP BY ky_cuoc
                      ORDER BY ky_cuoc ASC");
            }

            return rows.Select(r => (object)new
            {
                kyCuoc = (string)r["ky_cuoc"]!,
                tongThu = Convert.ToInt64(r["tong_thu"]),
            }).ToArray();
        }
        catch
        {
            // bảng hoa_don của thành viên khác chưa merge → trả mảng rỗng, không crash
            return [];
        }
    }
}
