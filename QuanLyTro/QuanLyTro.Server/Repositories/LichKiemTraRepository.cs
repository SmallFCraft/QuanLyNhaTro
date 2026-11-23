using QuanLyTro.Server.Data;

namespace QuanLyTro.Server.Repositories;

public sealed class LichKiemTraRepository
{
    private readonly Database _db;
    public LichKiemTraRepository(Database db) => _db = db;
    public Database Db => _db;

    public Task<List<Dictionary<string, object?>>> ListAsync(int? khuTroId) =>
        khuTroId is null
            ? _db.QueryAsync(
                @"SELECT l.id, l.phong_id, l.dat_lich_id, l.ho_ten, l.so_dien_thoai, l.thoi_gian,
                         l.trang_thai, l.nhan_vien_id, l.ket_qua, l.ly_do_tu_choi,
                         p.so_phong, p.khu_tro_id, k.ten_khu_tro AS ten_khu,
                         nv.ho_ten AS nhan_vien_ten, nv.chuyen_mon AS nhan_vien_chuyen_mon
                  FROM lich_kiem_tra l
                  JOIN phong p ON p.id = l.phong_id
                  JOIN khu_tro k ON k.id = p.khu_tro_id
                  LEFT JOIN tai_khoan nv ON nv.id = l.nhan_vien_id
                  ORDER BY FIELD(l.trang_thai, 'ChoDuyet', 'DaDuyet', 'DangLam', 'HoanThanh', 'TuChoi'),
                           l.thoi_gian DESC, l.id DESC")
            : _db.QueryAsync(
                @"SELECT l.id, l.phong_id, l.dat_lich_id, l.ho_ten, l.so_dien_thoai, l.thoi_gian,
                         l.trang_thai, l.nhan_vien_id, l.ket_qua, l.ly_do_tu_choi,
                         p.so_phong, p.khu_tro_id, k.ten_khu_tro AS ten_khu,
                         nv.ho_ten AS nhan_vien_ten, nv.chuyen_mon AS nhan_vien_chuyen_mon
                  FROM lich_kiem_tra l
                  JOIN phong p ON p.id = l.phong_id
                  JOIN khu_tro k ON k.id = p.khu_tro_id
                  LEFT JOIN tai_khoan nv ON nv.id = l.nhan_vien_id
                  WHERE p.khu_tro_id = @k
                  ORDER BY FIELD(l.trang_thai, 'ChoDuyet', 'DaDuyet', 'DangLam', 'HoanThanh', 'TuChoi'),
                           l.thoi_gian DESC, l.id DESC",
                new { k = khuTroId });

    public async Task<Dictionary<string, object?>?> FindByIdOrDatLichAsync(int id)
    {
        var rows = await _db.QueryAsync(
            @"SELECT l.id, l.phong_id, l.dat_lich_id, l.ho_ten, l.so_dien_thoai, l.thoi_gian,
                     l.trang_thai, l.nhan_vien_id, l.ket_qua, l.ly_do_tu_choi,
                     p.so_phong, p.khu_tro_id
              FROM lich_kiem_tra l
              JOIN phong p ON p.id = l.phong_id
              WHERE l.id = @id OR l.dat_lich_id = @id
              ORDER BY (l.id = @id) DESC
              LIMIT 1",
            new { id });
        if (rows.Count > 0)
            return rows[0];

        // Trường hợp dat_lich_xem_phong được chèn trực tiếp chưa có bản ghi lich_kiem_tra
        var dlRows = await _db.QueryAsync(
            @"SELECT d.id, d.phong_id, d.ho_ten, d.so_dien_thoai, d.thoi_gian, d.trang_thai,
                     p.so_phong, p.khu_tro_id
              FROM dat_lich_xem_phong d
              JOIN phong p ON p.id = d.phong_id
              WHERE d.id = @id",
            new { id });
        if (dlRows.Count == 0)
            return null;

        var dl = dlRows[0];
        var lktId = await _db.ExecuteAsync(
            @"INSERT INTO lich_kiem_tra (phong_id, dat_lich_id, ho_ten, so_dien_thoai, thoi_gian, trang_thai)
              VALUES (@p, @dl, @ht, @sdt, @tg, @tt)",
            new
            {
                p = Convert.ToInt32(dl["phong_id"]),
                dl = Convert.ToInt32(dl["id"]),
                ht = dl["ho_ten"] as string,
                sdt = dl["so_dien_thoai"] as string,
                tg = Convert.ToDateTime(dl["thoi_gian"]!),
                tt = (string)dl["trang_thai"]!,
            });

        return (await _db.QueryAsync(
            @"SELECT l.id, l.phong_id, l.dat_lich_id, l.ho_ten, l.so_dien_thoai, l.thoi_gian,
                     l.trang_thai, l.nhan_vien_id, l.ket_qua, l.ly_do_tu_choi,
                     p.so_phong, p.khu_tro_id
              FROM lich_kiem_tra l
              JOIN phong p ON p.id = l.phong_id
              WHERE l.id = @lktId",
            new { lktId })).SingleOrDefault();
    }

    public async Task<int> ApproveAsync(int lichKiemTraId, int? datLichId)
    {
        var n = await _db.ExecuteAsync(
            "UPDATE lich_kiem_tra SET trang_thai = 'DaDuyet', ly_do_tu_choi = NULL WHERE id = @id AND trang_thai = 'ChoDuyet'",
            new { id = lichKiemTraId });
        if (n > 0 && datLichId is not null)
        {
            await _db.ExecuteAsync(
                "UPDATE dat_lich_xem_phong SET trang_thai = 'DaDuyet' WHERE id = @dl",
                new { dl = datLichId.Value });
        }
        return n;
    }

    public async Task<int> RejectAsync(int lichKiemTraId, int? datLichId, string? lyDoTuChoi)
    {
        var n = await _db.ExecuteAsync(
            @"UPDATE lich_kiem_tra
              SET trang_thai = 'TuChoi', ly_do_tu_choi = @lyDo
              WHERE id = @id AND trang_thai IN ('ChoDuyet', 'DaDuyet')",
            new { id = lichKiemTraId, lyDo = lyDoTuChoi });
        if (n > 0 && datLichId is not null)
        {
            await _db.ExecuteAsync(
                "UPDATE dat_lich_xem_phong SET trang_thai = 'TuChoi' WHERE id = @dl",
                new { dl = datLichId.Value });
        }
        return n;
    }

    public Task<List<Dictionary<string, object?>>> ListEmployeesAsync() =>
        _db.QueryAsync(
            @"SELECT id, ten_dang_nhap, ho_ten, chuyen_mon
              FROM tai_khoan
              WHERE vai_tro = 'NhanVien' AND bi_khoa = 0
              ORDER BY id");

    public async Task<Dictionary<string, object?>?> GetEmployeeByIdAsync(int nhanVienId) =>
        (await _db.QueryAsync(
            @"SELECT id, ten_dang_nhap, ho_ten, vai_tro, chuyen_mon, bi_khoa
              FROM tai_khoan
              WHERE id = @id AND vai_tro = 'NhanVien' AND bi_khoa = 0",
            new { id = nhanVienId })).SingleOrDefault();

    public async Task<int> AssignEmployeeAsync(int lichKiemTraId, int? datLichId, int nhanVienId)
    {
        var n = await _db.ExecuteAsync(
            @"UPDATE lich_kiem_tra
              SET nhan_vien_id = @nv, trang_thai = 'DaDuyet'
              WHERE id = @id AND trang_thai IN ('ChoDuyet', 'DaDuyet')",
            new { nv = nhanVienId, id = lichKiemTraId });
        if (n > 0 && datLichId is not null)
        {
            await _db.ExecuteAsync(
                "UPDATE dat_lich_xem_phong SET trang_thai = 'DaDuyet' WHERE id = @dl",
                new { dl = datLichId.Value });
        }
        return n;
    }

    public Task<List<Dictionary<string, object?>>> ListByEmployeeAsync(int nhanVienId) =>
        _db.QueryAsync(
            @"SELECT l.id, l.phong_id, l.dat_lich_id, l.ho_ten, l.so_dien_thoai, l.thoi_gian,
                     l.trang_thai, l.nhan_vien_id, l.ket_qua, l.ly_do_tu_choi,
                     p.so_phong, p.khu_tro_id, k.ten_khu_tro AS ten_khu,
                     nv.ho_ten AS nhan_vien_ten
              FROM lich_kiem_tra l
              JOIN phong p ON p.id = l.phong_id
              JOIN khu_tro k ON k.id = p.khu_tro_id
              LEFT JOIN tai_khoan nv ON nv.id = l.nhan_vien_id
              WHERE l.nhan_vien_id = @nv
              ORDER BY FIELD(l.trang_thai, 'DaDuyet', 'DangLam', 'HoanThanh', 'TuChoi'),
                       l.thoi_gian DESC, l.id DESC",
            new { nv = nhanVienId });

    public Task<int> AcceptTaskAsync(int lichKiemTraId, int nhanVienId) =>
        _db.ExecuteAsync(
            @"UPDATE lich_kiem_tra
              SET trang_thai = 'DangLam'
              WHERE id = @id AND nhan_vien_id = @nv AND trang_thai = 'DaDuyet'",
            new { id = lichKiemTraId, nv = nhanVienId });

    public Task<int> CompleteTaskAsync(int lichKiemTraId, int nhanVienId, string ketQua) =>
        _db.ExecuteAsync(
            @"UPDATE lich_kiem_tra
              SET trang_thai = 'HoanThanh', ket_qua = @kq
              WHERE id = @id AND nhan_vien_id = @nv AND trang_thai = 'DangLam'",
            new { id = lichKiemTraId, nv = nhanVienId, kq = ketQua });
}
