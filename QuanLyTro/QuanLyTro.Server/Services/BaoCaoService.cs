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
        var doanhThuTheoKhu = await LayDoanhThuTheoKhuAsync(tuThang, denThang);
        var hieuSuatQuanLy = await LayHieuSuatQuanLyAsync(tuThang, denThang);
        var duBaoKyToi = await LayDuBaoKyToiAsync();

        return new
        {
            tongDoanhThu = doanhThuTheoKhu.Sum(x => x.TongThu),
            doanhThuTheoKhu = doanhThuTheoKhu.Select(x => new { khuTroId = x.KhuTroId, tenKhuTro = x.TenKhuTro, tongThu = x.TongThu, tyLeLapDay = x.TyLeLapDay }).ToArray(),
            hieuSuatQuanLy,
            duBaoKyToi,
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

    private async Task<(int KhuTroId, string TenKhuTro, long TongThu, double TyLeLapDay)[]> LayDoanhThuTheoKhuAsync(string? tuThang, string? denThang)
    {
        var where = "hd.trang_thai='DaThu' AND (@tu IS NULL OR hd.ky_cuoc>=@tu) AND (@den IS NULL OR hd.ky_cuoc<=@den)";
        var param = new { tu = string.IsNullOrWhiteSpace(tuThang) ? null : tuThang, den = string.IsNullOrWhiteSpace(denThang) ? null : denThang };
        var khuRows = await _db.QueryAsync(
            @"SELECT k.id, k.ten_khu_tro,
                     COALESCE(SUM(CASE WHEN " + where + @" THEN hd.tong_tien ELSE 0 END),0) AS tong_thu,
                     COUNT(DISTINCT p.id) AS tong_phong,
                     COUNT(DISTINCT CASE WHEN p.trang_thai='DaThue' THEN p.id END) AS da_thue
              FROM khu_tro k
              LEFT JOIN phong p ON p.khu_tro_id=k.id
              LEFT JOIN hoa_don hd ON hd.phong_id=p.id
              GROUP BY k.id, k.ten_khu_tro ORDER BY k.id", param);
        return khuRows.Select(r => (
            Convert.ToInt32(r["id"]), (string)r["ten_khu_tro"]!, Convert.ToInt64(r["tong_thu"]),
            Convert.ToInt32(r["tong_phong"]) > 0
                ? Math.Round(Convert.ToDouble(r["da_thue"]) / Convert.ToInt32(r["tong_phong"]) * 100, 1)
                : 0.0)).ToArray();
    }

    private async Task<object[]> LayHieuSuatQuanLyAsync(string? tuThang, string? denThang)
    {
        var rows = await _db.QueryAsync(
            @"SELECT tk.id, tk.ho_ten,
                     (SELECT COUNT(*) FROM hoa_don hd JOIN phong p ON p.id=hd.phong_id
                      WHERE hd.nguoi_thu_id=tk.id AND hd.trang_thai='DaThu'
                        AND (@tu IS NULL OR hd.ky_cuoc>=@tu) AND (@den IS NULL OR hd.ky_cuoc<=@den)) AS so_thu,
                     (SELECT COUNT(*) FROM chi_so_dien_nuoc cs JOIN phong p ON p.id=cs.phong_id
                      WHERE p.khu_tro_id=tk.khu_tro_id
                        AND (@tu IS NULL OR cs.ky_cuoc>=@tu) AND (@den IS NULL OR cs.ky_cuoc<=@den)) AS so_chi_so,
                     (SELECT COUNT(*) FROM hop_dong hd JOIN phong p ON p.id=hd.phong_id
                      WHERE p.khu_tro_id=tk.khu_tro_id
                        AND (@tu IS NULL OR DATE_FORMAT(hd.ngay_tao,'%Y-%m')>=@tu)
                        AND (@den IS NULL OR DATE_FORMAT(hd.ngay_tao,'%Y-%m')<=@den)) AS so_hop_dong,
                     (SELECT COUNT(*) FROM de_xuat dx WHERE dx.nguoi_gui_id=tk.id AND dx.trang_thai='DaDuyet'
                        AND (@tu IS NULL OR DATE_FORMAT(dx.tao_luc,'%Y-%m')>=@tu)
                        AND (@den IS NULL OR DATE_FORMAT(dx.tao_luc,'%Y-%m')<=@den)) AS so_de_xuat_duyet,
                     (SELECT COUNT(*) FROM de_xuat dx WHERE dx.nguoi_gui_id=tk.id
                        AND (@tu IS NULL OR DATE_FORMAT(dx.tao_luc,'%Y-%m')>=@tu)
                        AND (@den IS NULL OR DATE_FORMAT(dx.tao_luc,'%Y-%m')<=@den)) AS so_de_xuat
              FROM tai_khoan tk WHERE tk.vai_tro='QuanLy' ORDER BY tk.id",
            new { tu = string.IsNullOrWhiteSpace(tuThang) ? null : tuThang, den = string.IsNullOrWhiteSpace(denThang) ? null : denThang });
        return rows.Select(r => (object)new
        {
            quanLyId = Convert.ToInt32(r["id"]),
            hoTen = (string)r["ho_ten"]!,
            soGiaoDichThuTien = Convert.ToInt32(r["so_thu"]),
            soLanGhiChiSo = Convert.ToInt32(r["so_chi_so"]),
            soHopDongDaLap = Convert.ToInt32(r["so_hop_dong"]),
            tyLeDeXuatDuocDuyet = Convert.ToInt32(r["so_de_xuat"]) > 0
                ? Math.Round((double)Convert.ToInt32(r["so_de_xuat_duyet"]) / Convert.ToInt32(r["so_de_xuat"]) * 100, 1)
                : 0.0,
        }).ToArray();
    }

    private async Task<object> LayDuBaoKyToiAsync()
    {
        var rows = await _db.QueryAsync(
            @"SELECT ky_cuoc, SUM(tong_tien) AS tong FROM hoa_don WHERE trang_thai='DaThu'
              GROUP BY ky_cuoc ORDER BY ky_cuoc DESC LIMIT 3");
        var vals = rows.Select(r => Convert.ToInt64(r["tong"])).ToArray();
        var avg = vals.Length > 0 ? vals.Sum() / vals.Length : 0;
        return new { giaTri = avg, congThuc = $"Trung bình {vals.Length} kỳ gần nhất: SUM(tổng thu đã thu) / {vals.Length}" };
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
