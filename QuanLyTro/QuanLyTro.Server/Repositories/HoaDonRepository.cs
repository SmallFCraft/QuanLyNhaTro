using QuanLyTro.Server.Data;

namespace QuanLyTro.Server.Repositories;

/// <summary>M4a: hóa đơn theo (phòng, kỳ), BIGINT đồng VND.</summary>
public sealed class HoaDonRepository
{
    private readonly Database _db;

    public HoaDonRepository(Database db) => _db = db;

    public Database Db => _db;

    public Task<int> InsertAsync(int phongId, string kyCuoc, long tienPhong, long tienDien,
        long tienNuoc, long phiKhac, long tongTien, string? ghiChu) => _db.ExecuteAsync(
        @"INSERT INTO hoa_don (phong_id, ky_cuoc, tien_phong, tien_dien, tien_nuoc, phi_khac, tong_tien, ghi_chu)
          VALUES (@p, @ky, @tp, @td, @tn, @pk, @tt, @gc)",
        new { p = phongId, ky = kyCuoc, tp = tienPhong, td = tienDien, tn = tienNuoc, pk = phiKhac, tt = tongTien, gc = ghiChu });

    public Task<int> CountTrungAsync(int phongId, string kyCuoc) => _db.ScalarT<int>(
        "SELECT COUNT(*) FROM hoa_don WHERE phong_id = @p AND ky_cuoc = @ky",
        new { p = phongId, ky = kyCuoc });

    public Task<Dictionary<string, object?>?> ByIdAsync(int id) => _db.QueryAsync(
        @"SELECT hd.id, hd.phong_id, hd.ky_cuoc, hd.tien_phong, hd.tien_dien, hd.tien_nuoc,
                 hd.phi_khac, hd.tong_tien, hd.trang_thai, hd.ngay_dong, hd.nguoi_thu_id, hd.ghi_chu,
                 p.so_phong, p.khu_tro_id
          FROM hoa_don hd JOIN phong p ON p.id = hd.phong_id
          WHERE hd.id = @id", new { id })
        .ContinueWith(t => (Dictionary<string, object?>?)t.Result.SingleOrDefault());

    public Task<List<Dictionary<string, object?>>> ListAsync(string? kyCuoc, int? phongId) => _db.QueryAsync(
        @"SELECT hd.id, hd.phong_id, hd.ky_cuoc, hd.tien_phong, hd.tien_dien, hd.tien_nuoc,
                 hd.phi_khac, hd.tong_tien, hd.trang_thai, hd.ngay_dong, hd.ghi_chu, p.so_phong, p.khu_tro_id
          FROM hoa_don hd JOIN phong p ON p.id = hd.phong_id
          WHERE (@ky IS NULL OR hd.ky_cuoc = @ky) AND (@p IS NULL OR hd.phong_id = @p)
          ORDER BY hd.ky_cuoc DESC, p.so_phong", new { ky = kyCuoc, p = phongId });

    public Task<int> MarkPaidAsync(int id, int nguoiThuId) => _db.ExecuteAsync(
        @"UPDATE hoa_don SET trang_thai = 'DaThu', ngay_dong = NOW(), nguoi_thu_id = @n
          WHERE id = @id AND trang_thai = 'ChuaThu'",
        new { n = nguoiThuId, id });

    public Task<List<Dictionary<string, object?>>> ByPhongAsync(int phongId, int page, int soLuongMoiTrang) =>
        _db.QueryAsync(
            @"SELECT id, ky_cuoc, tien_phong, tien_dien, tien_nuoc, phi_khac, tong_tien,
                     trang_thai, ngay_dong, ghi_chu
              FROM hoa_don
              WHERE phong_id = @p
              ORDER BY ky_cuoc DESC
              LIMIT @limit OFFSET @offset",
            new { p = phongId, limit = soLuongMoiTrang, offset = (page - 1) * soLuongMoiTrang });

    public Task<int> CountByPhongAsync(int phongId) => _db.ScalarT<int>(
        "SELECT COUNT(*) FROM hoa_don WHERE phong_id = @p", new { p = phongId });

    /// <summary>Hóa đơn chưa thu có nội dung khớp (soPhong + kyCuoc) — dùng bởi đối soát SePay.</summary>
    public Task<Dictionary<string, object?>?> TimChuaThuTheoNoiDungAsync(string soPhong, string kyCuoc) =>
        _db.QueryAsync(
            @"SELECT hd.id, hd.tong_tien, hd.ky_cuoc, p.so_phong
              FROM hoa_don hd JOIN phong p ON p.id = hd.phong_id
              WHERE hd.trang_thai = 'ChuaThu' AND p.so_phong = @sp AND hd.ky_cuoc = @ky
              LIMIT 1", new { sp = soPhong, ky = kyCuoc })
        .ContinueWith(t => (Dictionary<string, object?>?)t.Result.SingleOrDefault());
}
