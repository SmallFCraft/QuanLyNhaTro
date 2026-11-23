using MySqlConnector;
using QuanLyTro.Server.Data;
using QuanLyTro.Shared.Models;

namespace QuanLyTro.Server.Repositories;

public sealed class DatLichRepository
{
    private readonly Database _db;
    public DatLichRepository(Database db) => _db = db;
    public Database Db => _db;

    public Task<List<Dictionary<string, object?>>> VacantRoomsPublicAsync() =>
        _db.QueryAsync(
            @"SELECT p.id AS phong_id, p.so_phong, k.ten_khu_tro AS ten_khu, p.gia_thue
              FROM phong p
              JOIN khu_tro k ON k.id = p.khu_tro_id
              WHERE p.trang_thai = 'Trong' AND k.bi_ngung_hoat_dong = 0
              ORDER BY k.id, p.so_phong");

    public Task<int> CountPendingTodayByPhoneAsync(string soDienThoai) =>
        _db.ScalarT<int>(
            @"SELECT COUNT(*)
              FROM dat_lich_xem_phong
              WHERE so_dien_thoai = @sdt
                AND DATE(tao_luc) = CURDATE()
                AND trang_thai = 'ChoDuyet'",
            new { sdt = soDienThoai });

    /// <summary>
    /// Tạo yêu cầu đặt lịch xem phòng và bản ghi lịch kiểm tra chờ duyệt trong 1 transaction.
    /// </summary>
    public async Task<(int DatLichId, int LichKiemTraId)> CreateBookingAsync(
        int phongId, string hoTen, string soDienThoai, DateTime thoiGian)
    {
        var (conn, tx) = await _db.BeginAsync();
        // MySQL named lock serializes this phone across rooms and server instances.
        var lockName = "qlt-booking-" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(soDienThoai)))[..40];
        var locked = false;
        try
        {
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT GET_LOCK(@name, 10)";
                cmd.Parameters.AddWithValue("@name", lockName);
                locked = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
                if (!locked) throw new BusinessRuleException("Đang xử lý yêu cầu cùng số điện thoại, vui lòng thử lại.");
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT trang_thai FROM phong WHERE id = @p FOR UPDATE";
                cmd.Parameters.AddWithValue("@p", phongId);
                var raw = await cmd.ExecuteScalarAsync();
                if (raw is null or DBNull)
                    throw new NotFoundOrDeniedException();
                if ((string)raw != "Trong")
                    throw new BusinessRuleException("Phòng không còn trống để đặt lịch xem.");
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"SELECT COUNT(*) FROM dat_lich_xem_phong
                                    WHERE so_dien_thoai = @sdt
                                      AND DATE(tao_luc) = CURDATE()
                                      AND trang_thai = 'ChoDuyet'";
                cmd.Parameters.AddWithValue("@sdt", soDienThoai);
                var pendingCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (pendingCount >= 3)
                    throw new BusinessRuleException("Mỗi số điện thoại không được quá 3 yêu cầu chờ duyệt trong một ngày.");
            }

            int datLichId;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO dat_lich_xem_phong (phong_id, ho_ten, so_dien_thoai, thoi_gian, trang_thai)
                                    VALUES (@p, @ht, @sdt, @tg, 'ChoDuyet')";
                cmd.Parameters.AddWithValue("@p", phongId);
                cmd.Parameters.AddWithValue("@ht", hoTen);
                cmd.Parameters.AddWithValue("@sdt", soDienThoai);
                cmd.Parameters.AddWithValue("@tg", thoiGian);
                await cmd.ExecuteNonQueryAsync();
                datLichId = (int)cmd.LastInsertedId;
            }

            int lichKiemTraId;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO lich_kiem_tra (phong_id, dat_lich_id, ho_ten, so_dien_thoai, thoi_gian, trang_thai)
                                    VALUES (@p, @dl, @ht, @sdt, @tg, 'ChoDuyet')";
                cmd.Parameters.AddWithValue("@p", phongId);
                cmd.Parameters.AddWithValue("@dl", datLichId);
                cmd.Parameters.AddWithValue("@ht", hoTen);
                cmd.Parameters.AddWithValue("@sdt", soDienThoai);
                cmd.Parameters.AddWithValue("@tg", thoiGian);
                await cmd.ExecuteNonQueryAsync();
                lichKiemTraId = (int)cmd.LastInsertedId;
            }

            await tx.CommitAsync();
            return (datLichId, lichKiemTraId);
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await tx.RollbackAsync();
            throw new BusinessRuleException("Lịch hẹn xem phòng này vào cùng khung giờ đã tồn tại.");
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
        finally
        {
            try
            {
                if (locked)
                {
                    await using var release = conn.CreateCommand();
                    release.CommandText = "SELECT RELEASE_LOCK(@name)";
                    release.Parameters.AddWithValue("@name", lockName);
                    await release.ExecuteScalarAsync();
                }
            }
            finally
            {
                await conn.DisposeAsync();
            }
        }
    }
}
