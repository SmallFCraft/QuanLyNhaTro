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

        return null;
    }

    public Task<int> ApproveAsync(int lichKiemTraId, int? datLichId) =>
        UpdateStatusAsync(lichKiemTraId, datLichId, "DaDuyet", "ChoDuyet", null, null);

    public Task<int> RejectAsync(int lichKiemTraId, int? datLichId, string? lyDoTuChoi) =>
        UpdateStatusAsync(lichKiemTraId, datLichId, "TuChoi", null, lyDoTuChoi, null);

    private async Task<int> UpdateStatusAsync(int id, int? datLichId, string status,
        string? requiredStatus, string? reason, int? employeeId)
    {
        var (conn, tx) = await _db.BeginAsync();
        await using var connection = conn;
        await using var transaction = tx;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"UPDATE lich_kiem_tra
                SET trang_thai = @status, ly_do_tu_choi = @reason,
                    nhan_vien_id = COALESCE(@employee, nhan_vien_id)
                WHERE id = @id AND trang_thai IN ('ChoDuyet', 'DaDuyet')
                  AND (@required IS NULL OR trang_thai = @required)";
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@reason", (object?)reason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@employee", (object?)employeeId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@required", (object?)requiredStatus ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", id);
            var n = await cmd.ExecuteNonQueryAsync();
            if (n > 0 && datLichId is not null)
            {
                cmd.CommandText = "UPDATE dat_lich_xem_phong SET trang_thai = @status WHERE id = @dl";
                cmd.Parameters.AddWithValue("@dl", datLichId.Value);
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            return n;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
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

    public Task<int> AssignEmployeeAsync(int lichKiemTraId, int? datLichId, int nhanVienId) =>
        UpdateStatusAsync(lichKiemTraId, datLichId, "DaDuyet", null, null, nhanVienId);

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
