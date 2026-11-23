using System.Text.Json;
using QuanLyTro.Server.Data;
using QuanLyTro.Server.Network;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;
using QuanLyTro.Shared.Protocol;
using QuanLyTro.Tests.TestHelpers;

namespace QuanLyTro.Tests;

[TestClass]
public class DatLichLuuTruTests
{
    private Database _db = null!;
    private SessionStore _sessions = null!;
    private RequestRouter _router = null!;
    private AuthenticationService _auth = null!;

    private string _chuTroToken = null!;
    private string _leTanToken = null!;
    private string _nhanVienToken = null!;
    private string _congAnToken = null!;

    private int _nhanVienId;
    private int _khuId;
    private int _phongTrongId;
    private int _phongDaThueId;

    [TestInitialize]
    public async Task Setup()
    {
        await TestDb.ResetAsync();
        _db = TestDb.Create();
        await new SchemaInitializer(_db).InitializeAsync();
        _sessions = new SessionStore();
        _auth = new AuthenticationService(_db, _sessions);
        _router = new RequestRouter(_db, _sessions);

        await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES ('chutro', @h, 'Chu Tro', 'ChuTro')",
            new { h = PasswordHasher.Hash("chutro") });
        await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES ('letan', @h, 'Le Tan', 'LeTan')",
            new { h = PasswordHasher.Hash("letan") });
        _nhanVienId = await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro, chuyen_mon) VALUES ('nhanvien', @h, 'Nhan Vien Don Phong', 'NhanVien', 'DonPhong')",
            new { h = PasswordHasher.Hash("nhanvien") });
        await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES ('congan', @h, 'Cong An Phuong', 'CongAn')",
            new { h = PasswordHasher.Hash("congan") });

        _chuTroToken = (await _auth.LoginAsync("chutro", "chutro")).Token;
        _leTanToken = (await _auth.LoginAsync("letan", "letan")).Token;
        _nhanVienToken = (await _auth.LoginAsync("nhanvien", "nhanvien")).Token;
        _congAnToken = (await _auth.LoginAsync("congan", "congan")).Token;

        _khuId = await _db.ExecuteAsync(
            "INSERT INTO khu_tro (ten_khu_tro, dia_chi) VALUES ('Khu A', 'Ngu Hanh Son, Da Nang')");
        _phongTrongId = await _db.ExecuteAsync(
            "INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da, trang_thai) VALUES (@k, '101', 2500000, 2, 'Trong')",
            new { k = _khuId });
        _phongDaThueId = await _db.ExecuteAsync(
            "INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da, trang_thai) VALUES (@k, '102', 2700000, 2, 'DaThue')",
            new { k = _khuId });
    }

    private async Task<ResponsePacket> Call(string action, string? token, object? data, string ctx = "c") =>
        await _router.HandleAsync(RequestPacket.Create(action, token, data), new ClientContext(ctx));

    private static string Raw(ResponsePacket r) => JsonDefaults.Serialize(r.Data!);

    private static int GetInt(ResponsePacket resp, string prop)
    {
        var el = JsonDefaults.Deserialize<JsonElement>(JsonDefaults.Serialize(resp.Data!));
        return el.GetProperty(prop).GetInt32();
    }

    [TestMethod]
    public async Task PhongLayTrongPublic_KhongCanToken_ChiTraPhongTrongAnToan()
    {
        var resp = await Call(ActionNames.PhongLayTrongPublic, null, new { });
        Assert.IsTrue(resp.Success, resp.Message);
        var json = Raw(resp);
        StringAssert.Contains(json, "101");
        Assert.IsFalse(json.Contains("102"), "Phòng DaThue không được xuất hiện ở danh sách trống public");
        Assert.IsFalse(json.Contains("cccd"), "Không được rò rỉ dữ liệu nhạy cảm");
    }

    [TestMethod]
    public async Task DatLich_Public_PhongTrong_ThanhCong_PhongDangThue_BiTuChoi()
    {
        var futureTime = DateTime.Now.AddDays(2).ToString("yyyy-MM-ddTHH:mm:00");

        var ok = await Call(ActionNames.KhachThueDatLich, null, new
        {
            phongId = _phongTrongId,
            hoTen = "Nguyen Van Khach",
            soDienThoai = "0905111222",
            thoiGian = futureTime,
        });
        Assert.IsTrue(ok.Success, ok.Message);

        var fail = await Call(ActionNames.KhachThueDatLich, null, new
        {
            phongId = _phongDaThueId,
            hoTen = "Nguyen Van Khach",
            soDienThoai = "0905111222",
            thoiGian = futureTime,
        });
        Assert.IsFalse(fail.Success, "Phòng đang thuê phải bị từ chối đặt lịch");
    }

    [TestMethod]
    public async Task DatLich_Qua3YeuCauChoDuyetTrongNgayCungSoDienThoai_BiTuChoi()
    {
        var sdt = "0905888999";
        for (var i = 1; i <= 3; i++)
        {
            var r = await Call(ActionNames.KhachThueDatLich, null, new
            {
                phongId = _phongTrongId,
                hoTen = $"Khach {i}",
                soDienThoai = sdt,
                thoiGian = DateTime.Now.AddDays(i).ToString("yyyy-MM-ddTHH:mm:00"),
            });
            Assert.IsTrue(r.Success, $"Yêu cầu thứ {i} phải thành công: {r.Message}");
        }

        var r4 = await Call(ActionNames.KhachThueDatLich, null, new
        {
            phongId = _phongTrongId,
            hoTen = "Khach 4",
            soDienThoai = sdt,
            thoiGian = DateTime.Now.AddDays(5).ToString("yyyy-MM-ddTHH:mm:00"),
        });
        Assert.IsFalse(r4.Success, "Yêu cầu chờ duyệt thứ 4 trong ngày từ cùng SĐT phải bị từ chối (BR)");
    }

    [TestMethod]
    public async Task LuongLichKiemTra_Duyet_GanNhanVien_NhanViec_HoanThanh_LuuKetQuaDung()
    {
        var datLich = await Call(ActionNames.KhachThueDatLich, null, new
        {
            phongId = _phongTrongId,
            hoTen = "Tran Thi Xem Phong",
            soDienThoai = "0912345678",
            thoiGian = DateTime.Now.AddDays(1).ToString("yyyy-MM-ddTHH:mm:00"),
        });
        Assert.IsTrue(datLich.Success, datLich.Message);
        var lichId = GetInt(datLich, "lichId");

        // 1. Lễ tân duyệt lịch -> DaDuyet
        var duyet = await Call(ActionNames.LichKiemTraDuyet, _leTanToken, new { lichId });
        Assert.IsTrue(duyet.Success, duyet.Message);
        var stSauDuyet = await _db.ScalarT<string>("SELECT trang_thai FROM lich_kiem_tra WHERE id = @id", new { id = lichId });
        Assert.AreEqual("DaDuyet", stSauDuyet);

        // 2. Lễ tân lấy danh sách nhân viên và gán việc
        var dsNv = await Call(ActionNames.LichKiemTraNhanVien, _leTanToken, new { });
        Assert.IsTrue(dsNv.Success, dsNv.Message);
        StringAssert.Contains(Raw(dsNv), "DonPhong");

        var gan = await Call(ActionNames.LichKiemTraGanNv, _leTanToken, new
        {
            lichId,
            nhanVienId = _nhanVienId,
        });
        Assert.IsTrue(gan.Success, gan.Message);

        // 3. Nhân viên thấy việc ở LICH_KIEM_TRA_CUA_TOI
        var cuaToi = await Call(ActionNames.LichKiemTraCuaToi, _nhanVienToken, new { });
        Assert.IsTrue(cuaToi.Success, cuaToi.Message);
        StringAssert.Contains(Raw(cuaToi), "Tran Thi Xem Phong");

        // 4. Nhân viên nhận việc -> DangLam
        var nhanViec = await Call(ActionNames.LichKiemTraNhanViec, _nhanVienToken, new { lichId });
        Assert.IsTrue(nhanViec.Success, nhanViec.Message);
        var stDangLam = await _db.ScalarT<string>("SELECT trang_thai FROM lich_kiem_tra WHERE id = @id", new { id = lichId });
        Assert.AreEqual("DangLam", stDangLam);

        // 5. Nhân viên hoàn thành -> HoanThanh + ketQua lưu đúng
        const string ketQuaKiemTra = "Da don phong sach se, khach dong y dat coc";
        var hoanThanh = await Call(ActionNames.LichKiemTraHoanThanh, _nhanVienToken, new
        {
            lichId,
            ketQua = ketQuaKiemTra,
        });
        Assert.IsTrue(hoanThanh.Success, hoanThanh.Message);

        var stHoanThanh = await _db.ScalarT<string>("SELECT trang_thai FROM lich_kiem_tra WHERE id = @id", new { id = lichId });
        var kqDb = await _db.ScalarT<string>("SELECT ket_qua FROM lich_kiem_tra WHERE id = @id", new { id = lichId });
        Assert.AreEqual("HoanThanh", stHoanThanh);
        Assert.AreEqual(ketQuaKiemTra, kqDb);
    }

    [TestMethod]
    public async Task NhanVien_GoiLichKiemTraDuyet_BiTuChoi()
    {
        var datLich = await Call(ActionNames.KhachThueDatLich, null, new
        {
            phongId = _phongTrongId,
            hoTen = "Khach A",
            soDienThoai = "0905123123",
            thoiGian = DateTime.Now.AddDays(1).ToString("yyyy-MM-ddTHH:mm:00"),
        });
        var lichId = GetInt(datLich, "lichId");

        var resp = await Call(ActionNames.LichKiemTraDuyet, _nhanVienToken, new { lichId });
        Assert.IsFalse(resp.Success, "NhanVien không được phép duyệt lịch kiểm tra");
    }

    [TestMethod]
    public async Task CongAn_GoiActionGhi_BiTuChoi()
    {
        var r1 = await Call(ActionNames.LichKiemTraDuyet, _congAnToken, new { lichId = 1 });
        Assert.IsFalse(r1.Success, "CongAn không được phép gọi LICH_KIEM_TRA_DUYET");

        var r2 = await Call("SU_CO_TAO", _congAnToken, new { tieuDe = "Test" });
        Assert.IsFalse(r2.Success, "CongAn không được phép gọi SU_CO_TAO");

        var r3 = await Call(ActionNames.PHONG_THEM, _congAnToken, new { khuTroId = _khuId, soPhong = "999", giaThue = 2000000, soNguoiToiDa = 2 });
        Assert.IsFalse(r3.Success, "CongAn không được phép gọi bất kỳ action ghi nào");
    }

    [TestMethod]
    public async Task CongAn_TraCuuXuatHoSoTamTru_CccdNganHon4_BiTuChoi_Du4TroLen_TraDungHoSo()
    {
        // Tạo khách thuê và gán vào phòng 102
        var dk = await Call(ActionNames.KHACH_THUE_DANG_KY, null, new
        {
            tenDangNhap = "048203001234",
            matKhau = "mk123456",
            hoTen = "Le Hoang Cong Dan",
            cccd = "048203001234",
            ngaySinh = "2001-05-20",
            soDienThoai = "0905666777",
            queQuan = "Da Nang",
        });
        Assert.IsTrue(dk.Success, dk.Message);
        var khachId = GetInt(dk, "taiKhoanId");

        await _db.ExecuteAsync(
            "UPDATE ho_so_khach_thue SET phong_id = @p, da_dang_ky_tam_tru = 1 WHERE tai_khoan_id = @t",
            new { p = _phongDaThueId, t = khachId });
        await _db.ExecuteAsync(
            "INSERT INTO lich_su_cu_tru (tai_khoan_khach_id, phong_id, khu_tro_id, loai_bien_dong) VALUES (@t, @p, @k, 'NhanPhong')",
            new { t = khachId, p = _phongDaThueId, k = _khuId });

        // CCCD < 4 ký tự -> bị từ chối
        var ngan = await Call(ActionNames.XuatHoSoTamTru, _congAnToken, new { cccd = "048" });
        Assert.IsFalse(ngan.Success, "CCCD dưới 4 ký tự phải bị từ chối");

        // CCCD >= 4 ký tự -> trả đúng hồ sơ
        var du = await Call(ActionNames.XuatHoSoTamTru, _congAnToken, new { cccd = "0482" });
        Assert.IsTrue(du.Success, du.Message);
        var json = Raw(du);
        StringAssert.Contains(json, "Le Hoang Cong Dan");
        StringAssert.Contains(json, "048203001234");
        StringAssert.Contains(json, "102");

        // Lịch sử cư trú cũng tra cứu được với vai trò Công an
        var ls = await Call(ActionNames.LichSuCuTruLay, _congAnToken, new { khachThueId = khachId });
        Assert.IsTrue(ls.Success, ls.Message);
        StringAssert.Contains(Raw(ls), "NhanPhong");
    }

    [TestMethod]
    public async Task LeTan_GoiXuatHoSoTamTru_BiTuChoi()
    {
        var resp = await Call(ActionNames.XuatHoSoTamTru, _leTanToken, new { cccd = "0482" });
        Assert.IsFalse(resp.Success, "Lễ tân không được phép gọi XUAT_HO_SO_TAM_TRU");
    }

    [TestMethod]
    public async Task QuanLyChuaGanKhu_KhongDuocXemLich()
    {
        var session = new SessionInfo(100, "ql", VaiTroNguoiDung.QuanLy, null, null, "ql-token");
        await _sessions.AddAsync(session);
        Assert.IsFalse((await Call(ActionNames.LichKiemTraLay, session.Token, new { })).Success);
    }

    [TestMethod]
    public async Task TimLichKhongTonTai_KhongTuGhiDuLieu()
    {
        var id = await _db.ExecuteAsync(@"INSERT INTO dat_lich_xem_phong
            (phong_id, ho_ten, so_dien_thoai, thoi_gian) VALUES (@p, 'Test', '0901234567', @time)",
            new { p = _phongTrongId, time = DateTime.Now.AddDays(1) });
        var session = new SessionInfo(101, "ql", VaiTroNguoiDung.QuanLy, _khuId + 1, null, "wrong-khu");
        await _sessions.AddAsync(session);
        Assert.IsFalse((await Call(ActionNames.LichKiemTraDuyet, session.Token, new { lichId = id })).Success);
        Assert.AreEqual(0, await _db.ScalarT<int>("SELECT COUNT(*) FROM lich_kiem_tra"));
    }

    [TestMethod]
    public async Task DatLichDongThoiKhacPhong_KhongQuaBaYeuCau()
    {
        var repo = new QuanLyTro.Server.Repositories.DatLichRepository(_db);
        var rooms = new List<int> { _phongTrongId };
        for (var i = 0; i < 3; i++)
            rooms.Add(await _db.ExecuteAsync(@"INSERT INTO phong
                (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da) VALUES (@k, @room, 1000000, 2)",
                new { k = _khuId, room = "C" + i }));
        var results = await Task.WhenAll(rooms.Select(async room =>
        {
            try { await repo.CreateBookingAsync(room, "Concurrent", "0909999999", DateTime.Now.AddDays(1)); return true; }
            catch (BusinessRuleException) { return false; }
        }));
        Assert.AreEqual(3, results.Count(ok => ok));
        Assert.AreEqual(3, await _db.ScalarT<int>("SELECT COUNT(*) FROM dat_lich_xem_phong"));
    }

    [TestMethod]
    public async Task CapNhatHaiBang_LoiBuocHaiRollbackBuocMot()
    {
        var booking = await new QuanLyTro.Server.Repositories.DatLichRepository(_db)
            .CreateBookingAsync(_phongTrongId, "Rollback", "0903333333", DateTime.Now.AddDays(1));
        await _db.ExecuteAsync(@"CREATE TRIGGER fail_booking_update BEFORE UPDATE ON dat_lich_xem_phong
            FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'test rollback'");
        try
        {
            var repo = new QuanLyTro.Server.Repositories.LichKiemTraRepository(_db);
            foreach (var update in new Func<Task<int>>[] {
                () => repo.ApproveAsync(booking.LichKiemTraId, booking.DatLichId),
                () => repo.RejectAsync(booking.LichKiemTraId, booking.DatLichId, "No"),
                () => repo.AssignEmployeeAsync(booking.LichKiemTraId, booking.DatLichId, _nhanVienId) })
            {
                await Assert.ThrowsExceptionAsync<MySqlConnector.MySqlException>(async () => { await update(); });
                Assert.AreEqual("ChoDuyet", await _db.ScalarT<string>(
                    "SELECT trang_thai FROM lich_kiem_tra WHERE id = @id", new { id = booking.LichKiemTraId }));
                Assert.IsNull(await _db.ScalarT<int?>(
                    "SELECT nhan_vien_id FROM lich_kiem_tra WHERE id = @id", new { id = booking.LichKiemTraId }));
            }
        }
        finally { await _db.ExecuteAsync("DROP TRIGGER fail_booking_update"); }
    }
}
