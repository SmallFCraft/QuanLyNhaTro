using MySqlConnector;
using QuanLyTro.Server.Data;
using QuanLyTro.Shared.Models;

namespace QuanLyTro.Server.Repositories;

/// <summary>Truy cập dữ liệu hợp đồng, mã nhận phòng QR/PIN, phụ lục (M3).</summary>
public sealed class HopDongRepository
{
    private readonly Database _db;
    public HopDongRepository(Database db) => _db = db;
    public Database Db => _db;

    public Task<int> ScalarCountAsync(string sql, object param) => _db.ScalarT<int>(sql, param);

    public Task<Dictionary<string, object?>?> ByIdAsync(int hopDongId) =>
        _db.QueryAsync(
            @"SELECT hd.*, p.so_phong, p.khu_tro_id, tk.ho_ten AS ten_nguoi_dai_dien, hs.cccd
              FROM hop_dong hd
              JOIN phong p ON p.id = hd.phong_id
              JOIN tai_khoan tk ON tk.id = hd.nguoi_dai_dien_id
              LEFT JOIN ho_so_khach_thue hs ON hs.tai_khoan_id = tk.id
              WHERE hd.id = @id", new { id = hopDongId })
            .ContinueWith(t => (Dictionary<string, object?>?)t.Result.SingleOrDefault());

    public Task<List<Dictionary<string, object?>>> GetAllAsync(int? khuTroId) =>
        _db.QueryAsync(
            @"SELECT hd.id, hd.phong_id, hd.nguoi_dai_dien_id, hd.ngay_bat_dau, hd.ngay_ket_thuc,
                     hd.gia_thue, hd.tien_coc, hd.trang_thai, hd.ghi_chu,
                     p.so_phong, p.khu_tro_id, tk.ho_ten AS ten_nguoi_dai_dien
              FROM hop_dong hd
              JOIN phong p ON p.id = hd.phong_id
              JOIN tai_khoan tk ON tk.id = hd.nguoi_dai_dien_id
              WHERE (@k IS NULL OR p.khu_tro_id = @k)
              ORDER BY hd.id DESC", new { k = khuTroId ?? (object)DBNull.Value });

    public Task<List<Dictionary<string, object?>>> PhuLucByHopDongAsync(int hopDongId) =>
        _db.QueryAsync(
            @"SELECT id, hop_dong_id, loai, noi_dung, ngay_ap_dung, trang_thai
              FROM phu_luc_hop_dong WHERE hop_dong_id = @id ORDER BY id", new { id = hopDongId });

    public Task<int> ThemPhuLucAsync(int hopDongId, string loai, string noiDung, DateOnly? ngayApDung) =>
        _db.ExecuteAsync(
            @"INSERT INTO phu_luc_hop_dong (hop_dong_id, loai, noi_dung, ngay_ap_dung, trang_thai)
              VALUES (@h, @l, @n, @d, 'HieuLuc')",
            new { h = hopDongId, l = loai, n = noiDung, d = ngayApDung });

    /// <summary>Tạo hợp đồng + mã nhận phòng PIN trong 1 transaction, nguyên tử.</summary>
    public async Task<(int HopDongId, string Pin)> TaoHopDongAsync(
        int phongId, int nguoiDaiDienId, DateOnly ngayBatDau, DateOnly ngayKetThuc,
        decimal giaThue, decimal tienCoc, string trangThai, string? ghiChu, DateOnly hetHanMa)
    {
        var (conn, tx) = await _db.BeginAsync();
        try
        {
            int hopDongId;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO hop_dong
                    (phong_id, nguoi_dai_dien_id, ngay_bat_dau, ngay_ket_thuc, gia_thue, tien_coc, trang_thai, ghi_chu)
                    VALUES (@p, @n, @bd, @kt, @g, @c, @t, @gc)";
                cmd.Parameters.AddWithValue("@p", phongId);
                cmd.Parameters.AddWithValue("@n", nguoiDaiDienId);
                cmd.Parameters.AddWithValue("@bd", ngayBatDau.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.AddWithValue("@kt", ngayKetThuc.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.AddWithValue("@g", giaThue);
                cmd.Parameters.AddWithValue("@c", tienCoc);
                cmd.Parameters.AddWithValue("@t", trangThai);
                cmd.Parameters.AddWithValue("@gc", (object?)ghiChu ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
                hopDongId = (int)cmd.LastInsertedId;
            }
            var pin = SinhPin();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO ma_nhan_phong (hop_dong_id, pin, qr_data, het_han)
                                    VALUES (@h, @pin, @qr, @hh)";
                cmd.Parameters.AddWithValue("@h", hopDongId);
                cmd.Parameters.AddWithValue("@pin", pin);
                cmd.Parameters.AddWithValue("@qr", $"QLT|{hopDongId}|{pin}");
                cmd.Parameters.AddWithValue("@hh", hetHanMa.ToDateTime(TimeOnly.MinValue));
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            return (hopDongId, pin);
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

    /// <summary>Nhận phòng (BR-19): khóa mã FOR UPDATE, đánh dấu đã dùng, hợp đồng HieuLuc, phòng DaThue,
    /// gán phong_id cho khách đại diện, ghi lich_su_cu_tru — tất cả trong 1 transaction. Trả về null khi mã sai/hết hạn/đã dùng.</summary>
    public async Task<Dictionary<string, object?>?> NhanPhongAsync(string tokenOrPin, int nguoiThucHienId)
    {
        var (conn, tx) = await _db.BeginAsync();
        try
        {
            int hopDongId, phongId;
            string pin;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"SELECT m.hop_dong_id, m.pin, hd.phong_id
                                    FROM ma_nhan_phong m
                                    JOIN hop_dong hd ON hd.id = m.hop_dong_id
                                    WHERE (m.qr_data = @t OR m.pin = @t)
                                      AND m.da_su_dung = 0
                                      AND m.het_han >= @today
                                      AND hd.nguoi_dai_dien_id = @who
                                    LIMIT 1
                                    FOR UPDATE";
                cmd.Parameters.AddWithValue("@t", tokenOrPin);
                cmd.Parameters.AddWithValue("@today", DateOnly.FromDateTime(DateTime.Today).ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.AddWithValue("@who", nguoiThucHienId);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    await reader.CloseAsync();
                    await tx.RollbackAsync(); // không có dòng khớp — chưa ghi gì, rollback sạch
                    await conn.CloseAsync();
                    return null;
                }
                hopDongId = reader.GetInt32(0);
                pin = reader.GetString(1);
                phongId = reader.GetInt32(2);
                await reader.CloseAsync();
            }

            // BR-19: hợp đồng phải đang chờ nhận phòng
            await using (var chk = conn.CreateCommand())
            {
                chk.Transaction = tx;
                chk.CommandText = "SELECT trang_thai FROM hop_dong WHERE id = @id";
                chk.Parameters.AddWithValue("@id", hopDongId);
                var tt = (string)(await chk.ExecuteScalarAsync())!;
                if (tt != "ChoNhanPhong")
                {
                    await tx.RollbackAsync();
                    throw new BusinessRuleException("Hợp đồng không ở trạng thái chờ nhận phòng.");
                }
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE ma_nhan_phong SET da_su_dung = 1 WHERE hop_dong_id = @h AND pin = @pin";
                cmd.Parameters.AddWithValue("@h", hopDongId);
                cmd.Parameters.AddWithValue("@pin", pin);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE hop_dong SET trang_thai = 'HieuLuc' WHERE id = @h";
                cmd.Parameters.AddWithValue("@h", hopDongId);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE phong SET trang_thai = 'DaThue' WHERE id = @p";
                cmd.Parameters.AddWithValue("@p", phongId);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE ho_so_khach_thue SET phong_id = @p WHERE tai_khoan_id = @t";
                cmd.Parameters.AddWithValue("@p", phongId);
                cmd.Parameters.AddWithValue("@t", nguoiThucHienId);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO lich_su_cu_tru (tai_khoan_khach_id, phong_id, khu_tro_id, loai_bien_dong, nguoi_thuc_hien_id)
                                    SELECT @t, p.id, p.khu_tro_id, 'NhanPhong', @n FROM phong p WHERE p.id = @p";
                cmd.Parameters.AddWithValue("@t", nguoiThucHienId);
                cmd.Parameters.AddWithValue("@p", phongId);
                cmd.Parameters.AddWithValue("@n", nguoiThucHienId);
                await cmd.ExecuteNonQueryAsync();
            }

            var info = await _db.QueryAsync(
                @"SELECT hd.id AS hop_dong_id, hd.ngay_bat_dau, p.so_phong
                  FROM hop_dong hd JOIN phong p ON p.id = hd.phong_id WHERE hd.id = @h", new { h = hopDongId });
            await tx.CommitAsync();
            return info.SingleOrDefault();
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

    /// <summary>Chấm dứt: hợp đồng ChamDut, phòng Trong, vô hiệu mã chưa dùng, xóa phong_id hồ sơ
    /// người đại diện + ghi lịch sử TraPhong — 1 transaction.</summary>
    public async Task ChamDutAsync(int hopDongId, string? ghiChu)
    {
        var (conn, tx) = await _db.BeginAsync();
        try
        {
            int phongId, nguoiDaiDienId;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT phong_id, nguoi_dai_dien_id FROM hop_dong WHERE id = @h FOR UPDATE";
                cmd.Parameters.AddWithValue("@h", hopDongId);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) throw new NotFoundOrDeniedException();
                phongId = reader.GetInt32(0);
                nguoiDaiDienId = reader.GetInt32(1);
                await reader.CloseAsync();
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"UPDATE hop_dong SET trang_thai = 'ChamDut',
                                    ghi_chu = COALESCE(@gc, ghi_chu) WHERE id = @h";
                cmd.Parameters.AddWithValue("@h", hopDongId);
                cmd.Parameters.AddWithValue("@gc", (object?)ghiChu ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE phong SET trang_thai = 'Trong' WHERE id = @p";
                cmd.Parameters.AddWithValue("@p", phongId);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE ma_nhan_phong SET da_su_dung = 1 WHERE hop_dong_id = @h AND da_su_dung = 0";
                cmd.Parameters.AddWithValue("@h", hopDongId);
                await cmd.ExecuteNonQueryAsync();
            }
            // trả phòng khỏi hồ sơ người đại diện (nếu đang trỏ phòng của hợp đồng này)
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE ho_so_khach_thue SET phong_id = NULL WHERE tai_khoan_id = @t AND phong_id = @p";
                cmd.Parameters.AddWithValue("@t", nguoiDaiDienId);
                cmd.Parameters.AddWithValue("@p", phongId);
                await cmd.ExecuteNonQueryAsync();
            }
            // ghi lịch sử trả phòng khớp hành vi TraPhong của M2
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO lich_su_cu_tru (tai_khoan_khach_id, phong_id, khu_tro_id, loai_bien_dong, nguoi_thuc_hien_id)
                                    SELECT @t, p.id, p.khu_tro_id, 'TraPhong', @n FROM phong p WHERE p.id = @p";
                cmd.Parameters.AddWithValue("@t", nguoiDaiDienId);
                cmd.Parameters.AddWithValue("@p", phongId);
                cmd.Parameters.AddWithValue("@n", nguoiDaiDienId);
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
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

    public Task<int> GiaHanAsync(int hopDongId, DateOnly ngayKetThucMoi) =>
        _db.ExecuteAsync("UPDATE hop_dong SET ngay_ket_thuc = @d WHERE id = @h",
            new { d = ngayKetThucMoi.ToDateTime(TimeOnly.MinValue), h = hopDongId });

    /// <summary>Vô hiệu toàn bộ mã chưa dùng của hợp đồng (sinh lại QR).</summary>
    public Task<int> VoHieuMaAsync(int hopDongId) =>
        _db.ExecuteAsync("UPDATE ma_nhan_phong SET da_su_dung = 1 WHERE hop_dong_id = @h AND da_su_dung = 0",
            new { h = hopDongId });

    /// <summary>Phòng đang có hợp đồng chưa chấm dứt (ChoNhanPhong/HieuLuc) — chặn lập trùng.</summary>
    public Task<bool> CoHopDongChuaKetThucAsync(int phongId) =>
        _db.ScalarT<long>(
            "SELECT COUNT(*) FROM hop_dong WHERE phong_id = @p AND trang_thai IN ('ChoNhanPhong','HieuLuc')",
            new { p = phongId })
        .ContinueWith(t => t.Result > 0);

    /// <summary>Vô hiệu mã cũ + thêm mã mới trong 1 transaction (SinhQr nguyên tử).</summary>
    public async Task VoHieuVaThemMaAsync(int hopDongId, string pin, string qrData, DateOnly hetHan)
    {
        var (conn, tx) = await _db.BeginAsync();
        try
        {
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE ma_nhan_phong SET da_su_dung = 1 WHERE hop_dong_id = @h AND da_su_dung = 0";
                cmd.Parameters.AddWithValue("@h", hopDongId);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO ma_nhan_phong (hop_dong_id, pin, qr_data, het_han)
                                    VALUES (@h, @pin, @qr, @hh)";
                cmd.Parameters.AddWithValue("@h", hopDongId);
                cmd.Parameters.AddWithValue("@pin", pin);
                cmd.Parameters.AddWithValue("@qr", qrData);
                cmd.Parameters.AddWithValue("@hh", hetHan.ToDateTime(TimeOnly.MinValue));
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
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

    /// <summary>Thêm mã nhận phòng mới cho hợp đồng (sinh lại QR).</summary>
    public Task<int> ThemMaAsync(int hopDongId, string pin, string qrData, DateOnly? hetHan = null) =>
        _db.ExecuteAsync(
            @"INSERT INTO ma_nhan_phong (hop_dong_id, pin, qr_data, het_han)
              VALUES (@h, @pin, @qr, @hh)",
            new { h = hopDongId, pin = pin, qr = qrData,
                  hh = (hetHan ?? DateOnly.FromDateTime(DateTime.Today).AddDays(7)).ToDateTime(TimeOnly.MinValue) });

    private static string SinhPin()
    {
        // RandomNumberGenerator của BCL — cryptographically secure
        return string.Create(8, (object?)null, static (span, _) =>
        {
            var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
            for (var i = 0; i < 8; i++) span[i] = (char)('0' + bytes[i] % 10);
        });
    }
}
