using System.Text.Json;
using QuanLyTro.Server.Data;
using QuanLyTro.Server.Network;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;
using QuanLyTro.Shared.Protocol;
using QuanLyTro.Tests.TestHelpers;

namespace QuanLyTro.Tests;

[TestClass]
public class TenantTests
{
    private Database _db = null!;
    private SessionStore _sessions = null!;
    private RequestRouter _router = null!;
    private string _chuTroToken = null!;
    private int _khuId;
    private int _phong1Id;
    private int _phong2Id;

    [TestInitialize]
    public async Task Setup()
    {
        await TestDb.ResetAsync();
        _db = TestDb.Create();
        await new SchemaInitializer(_db).InitializeAsync();
        _sessions = new SessionStore();
        var auth = new AuthenticationService(_db, _sessions);
        _router = new RequestRouter(_db, _sessions);

        await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES ('chutro', @h, 'Chu Tro', 'ChuTro')",
            new { h = PasswordHasher.Hash("chutro") });
        var s = await auth.LoginAsync("chutro", "chutro");
        _chuTroToken = s.Token;

        _khuId = await _db.ExecuteAsync(
            "INSERT INTO khu_tro (ten_khu_tro, dia_chi) VALUES ('Khu A', 'Da Nang')");
        _phong1Id = await _db.ExecuteAsync(
            "INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da) VALUES (@k, '101', 2500000, 2)",
            new { k = _khuId });
        _phong2Id = await _db.ExecuteAsync(
            "INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da) VALUES (@k, '102', 2500000, 2)",
            new { k = _khuId });
    }

    private async Task<ResponsePacket> Call(string action, string? token, object? data, string ctx = "c") =>
        await _router.HandleAsync(RequestPacket.Create(action, token, data), new ClientContext(ctx));

    private static string Raw(ResponsePacket r) => JsonDefaults.Serialize(r.Data!);

    private async Task<int> DangKyAsync(string cccd, string ten = "Khach Test")
    {
        var resp = await Call(ActionNames.KHACH_THUE_DANG_KY, null, new
        {
            tenDangNhap = cccd,
            matKhau = "mk123456",
            hoTen = ten,
            cccd,
            ngaySinh = "2000-01-01",
            soDienThoai = "0901234567",
            queQuan = "Quang Nam",
        });
        Assert.IsTrue(resp.Success, "DANG_KY: " + resp.Message);
        return GetInt(resp, "taiKhoanId");
    }

    private static int GetInt(ResponsePacket resp, string prop)
    {
        var el = JsonDefaults.Deserialize<JsonElement>(JsonDefaults.Serialize(resp.Data!));
        return el.GetProperty(prop).GetInt32();
    }

    [TestMethod]
    public async Task DangKy_Public_TaoTaiKhoanVaHoSo()
    {
        var id = await DangKyAsync("012345678901");
        var count = await _db.ScalarT<int>("SELECT COUNT(*) FROM ho_so_khach_thue WHERE tai_khoan_id = @id", new { id });
        Assert.AreEqual(1, count);
        // password phải hash PBKDF2, không lưu dạng rõ
        var hash = await _db.ScalarT<string>("SELECT mat_khau_hash FROM tai_khoan WHERE id = @id", new { id });
        StringAssert.StartsWith(hash, "PBKDF2$");
        StringAssert.DoesNotMatch(hash, new System.Text.RegularExpressions.Regex("mk123456"));
    }

    [TestMethod]
    public async Task DangKy_TrungCccd_Rollback_Sach()
    {
        await DangKyAsync("012345678901");
        var resp = await Call(ActionNames.KHACH_THUE_DANG_KY, null, new
        {
            tenDangNhap = "098765432101", // tên khác, CCCD trùng
            matKhau = "mk123456",
            hoTen = "Nguoi 2",
            cccd = "012345678901",
            ngaySinh = "2000-01-02",
            soDienThoai = "0909876543",
            queQuan = "Hoi An",
        });
        Assert.IsFalse(resp.Success);
        // Không để lại rác: chỉ 1 tai_khoan + 1 hồ sơ của lần đăng ký đầu
        var tk = await _db.ScalarT<int>("SELECT COUNT(*) FROM tai_khoan");
        Assert.AreEqual(2, tk); // chutro + khach 1
        var hs = await _db.ScalarT<int>("SELECT COUNT(*) FROM ho_so_khach_thue");
        Assert.AreEqual(1, hs);
    }

    [TestMethod]
    public async Task DangKy_CccdSaiDoDai_BiTuChoi()
    {
        var resp = await Call(ActionNames.KHACH_THUE_DANG_KY, null, new
        {
            tenDangNhap = "12345", matKhau = "mk123456", hoTen = "X",
            cccd = "12345", ngaySinh = "2000-01-01", soDienThoai = "09", queQuan = "Y",
        });
        Assert.IsFalse(resp.Success);
    }

    [TestMethod]
    public async Task DangKyCongKhai_KhongTheDatTamTruThayChuTro()
    {
        var response = await Call(ActionNames.KHACH_THUE_DANG_KY, null, new
        {
            tenDangNhap = "khachtamtru", matKhau = "mk123456", hoTen = "Khach tam tru",
            cccd = "012345678916", ngaySinh = "1999-02-03", soDienThoai = "0902222342",
            queQuan = "Hue", daDangKyTamTru = true,
        });
        Assert.IsTrue(response.Success, response.Message);
        var id = GetInt(response, "taiKhoanId");
        Assert.AreEqual(0, Convert.ToInt32(await _db.ScalarT<object>("SELECT da_dang_ky_tam_tru FROM ho_so_khach_thue WHERE tai_khoan_id=@id", new { id })));
    }

    [TestMethod]
    public async Task DangKyCongKhai_KhongDuocTuGanPhong()
    {
        var response = await Call(ActionNames.KHACH_THUE_DANG_KY, null, new
        {
            tenDangNhap = "khachpublic", matKhau = "mk123456", hoTen = "Khach public",
            cccd = "012345678903", ngaySinh = "1999-02-03", soDienThoai = "0902222333",
            queQuan = "Hue", phongId = _phong1Id, daDangKyTamTru = true,
        });
        Assert.IsTrue(response.Success, response.Message);
        Assert.AreEqual(0, await _db.ScalarT<int>("SELECT COUNT(*) FROM ho_so_khach_thue WHERE tai_khoan_id=(SELECT id FROM tai_khoan WHERE ten_dang_nhap='khachpublic') AND phong_id IS NOT NULL"));
        Assert.AreEqual(0, await _db.ScalarT<int>("SELECT COUNT(*) FROM lich_su_cu_tru WHERE loai_bien_dong='NhanPhong'"));
    }

    [TestMethod]
    public async Task LayKhachTheoPhongIdKhong_PhutKhachChoNhanPhong()
    {
        var id = await DangKyAsync("012345678930", "Khach Cho Nhan");
        var response = await Call(ActionNames.KHACH_THUE_LAY_THEO_PHONG, _chuTroToken, new { phongId = 0 });
        Assert.IsTrue(response.Success, response.Message);
        var data = JsonDefaults.Deserialize<JsonElement>(JsonDefaults.Serialize(response.Data!));
        Assert.IsTrue(data.EnumerateArray().Any(x => x.GetProperty("id").GetInt32() == id));
    }

    [TestMethod]
    public async Task ThemKhach_PhongTrongBaoTri_KhongHienLuaChon()
    {
        var response = await Call(ActionNames.PHONG_LAY_TAT_CA, _chuTroToken, new { });
        Assert.IsTrue(response.Success, response.Message);
        var data = JsonDefaults.Deserialize<JsonElement>(JsonDefaults.Serialize(response.Data!));
        var room = data.EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == _phong1Id);
        Assert.AreEqual("Trong", room.GetProperty("trangThai").GetString());
        await _db.ExecuteAsync("UPDATE phong SET trang_thai='BaoTri' WHERE id=@p", new { p = _phong1Id });
        var updated = await Call(ActionNames.PHONG_LAY_TAT_CA, _chuTroToken, new { });
        var changed = JsonDefaults.Deserialize<JsonElement>(JsonDefaults.Serialize(updated.Data!));
        Assert.AreEqual("BaoTri", changed.EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == _phong1Id).GetProperty("trangThai").GetString());
    }

    [TestMethod]
    public async Task ThemKhach_PhongBaoTri_BiTuChoiVaKhongTaoTaiKhoan()
    {
        await _db.ExecuteAsync("UPDATE phong SET trang_thai='BaoTri' WHERE id=@p", new { p = _phong1Id });
        var response = await Call(ActionNames.KHACH_THUE_THEM, _chuTroToken, new
        {
            tenDangNhap = "khachbaotri", matKhau = "mk123456", hoTen = "Khach bao tri",
            cccd = "012345678905", ngaySinh = "1999-02-03", soDienThoai = "0902222336",
            queQuan = "Hue", phongId = _phong1Id,
        });
        Assert.IsFalse(response.Success);
        Assert.AreEqual(0, await _db.ScalarT<int>("SELECT COUNT(*) FROM tai_khoan WHERE ten_dang_nhap='khachbaotri'"));
    }

    [TestMethod]
    public async Task ThemKhach_PhongChiConMotCho_VanThemDuoc()
    {
        await _db.ExecuteAsync("UPDATE phong SET so_nguoi_toi_da=1 WHERE id=@p", new { p = _phong1Id });
        var response = await Call(ActionNames.KHACH_THUE_THEM, _chuTroToken, new
        {
            tenDangNhap = "khachcuoicho", matKhau = "mk123456", hoTen = "Khach cuoi cho",
            cccd = "012345678904", ngaySinh = "1999-02-03", soDienThoai = "0902222334",
            queQuan = "Hue", phongId = _phong1Id,
        });
        Assert.IsTrue(response.Success, response.Message);
        Assert.AreEqual(1, await _db.ScalarT<int>("SELECT COUNT(*) FROM ho_so_khach_thue WHERE phong_id=@p", new { p = _phong1Id }));
    }

    [TestMethod]
    public async Task ThemKhach_GanPhongVaTamTru_TrongCungGiaoDich()
    {
        var resp = await Call(ActionNames.KHACH_THUE_THEM, _chuTroToken, new
        {
            tenDangNhap = "khachmoi", matKhau = "mk123456", hoTen = "Khach moi",
            cccd = "012345678902", ngaySinh = "1999-02-03", soDienThoai = "0902222333",
            queQuan = "Hue", phongId = _phong1Id, daDangKyTamTru = true,
        });
        Assert.IsTrue(resp.Success, resp.Message);
        var id = GetInt(resp, "taiKhoanId");
        var profile = await _db.QueryAsync("SELECT phong_id, da_dang_ky_tam_tru FROM ho_so_khach_thue WHERE tai_khoan_id=@id", new { id });
        Assert.AreEqual(_phong1Id, Convert.ToInt32(profile[0]["phong_id"]));
        Assert.AreEqual(1, Convert.ToInt32(profile[0]["da_dang_ky_tam_tru"]));
        Assert.AreEqual(1, await _db.ScalarT<int>("SELECT COUNT(*) FROM lich_su_cu_tru WHERE tai_khoan_khach_id=@id AND loai_bien_dong='NhanPhong'", new { id }));
        Assert.AreEqual("DaThue", await _db.ScalarT<string>("SELECT trang_thai FROM phong WHERE id=@p", new { p = _phong1Id }));
    }

    [TestMethod]
    public async Task CapNhat_ChuyenNguoiCuoiRaKhoiPhong_GiaiPhongTrangThaiPhong()
    {
        var id = await DangKyAsync("012345678915");
        await _db.ExecuteAsync("UPDATE ho_so_khach_thue SET phong_id=@p WHERE tai_khoan_id=@id", new { p = _phong1Id, id });
        await _db.ExecuteAsync("UPDATE phong SET trang_thai='DaThue' WHERE id=@p", new { p = _phong1Id });
        var response = await Call(ActionNames.KHACH_THUE_CAP_NHAT, _chuTroToken, new
        {
            taiKhoanId = id, hoTen = "Khach Test", cccd = "012345678915", ngaySinh = "1999-02-03",
            soDienThoai = "0902222341", queQuan = "Hue", phongId = _phong2Id,
        });
        Assert.IsTrue(response.Success, response.Message);
        Assert.AreEqual("Trong", await _db.ScalarT<string>("SELECT trang_thai FROM phong WHERE id=@p", new { p = _phong1Id }));
    }

    [TestMethod]
    public async Task CapNhat_CungPhongBaoTri_VanCapNhatDuocHoSo()
    {
        var id = await DangKyAsync("012345678910");
        await _db.ExecuteAsync("UPDATE ho_so_khach_thue SET phong_id=@p WHERE tai_khoan_id=@id", new { p = _phong1Id, id });
        await _db.ExecuteAsync("UPDATE phong SET trang_thai='BaoTri' WHERE id=@p", new { p = _phong1Id });
        var response = await Call(ActionNames.KHACH_THUE_CAP_NHAT, _chuTroToken, new
        {
            taiKhoanId = id, hoTen = "Thong tin moi", cccd = "012345678910", ngaySinh = "1999-02-03",
            soDienThoai = "0902222338", queQuan = "Hue", noiLamViec = "Cong ty", phongId = _phong1Id,
        });
        Assert.IsTrue(response.Success, response.Message);
        Assert.AreEqual("Thong tin moi", await _db.ScalarT<string>("SELECT ho_ten FROM tai_khoan WHERE id=@id", new { id }));
    }

    [TestMethod]
    public async Task TaoHopDongChoQr_KhachDaGanCungPhong_TuChoiKhongTaoHopDong()
    {
        var id = await DangKyAsync("012345678917");
        await _db.ExecuteAsync("UPDATE ho_so_khach_thue SET phong_id=@p WHERE tai_khoan_id=@id", new { p = _phong1Id, id });
        var response = await Call(ActionNames.HopDongTao, _chuTroToken, new
        {
            phongId = _phong1Id, nguoiDaiDienId = id, ngayBatDau = "2026-12-01",
            ngayKetThuc = "2027-12-01", giaThue = 2500000, trangThai = "ChoNhanPhong",
        });
        Assert.IsFalse(response.Success);
        Assert.AreEqual(0, await _db.ScalarT<int>("SELECT COUNT(*) FROM hop_dong"));
    }

    [TestMethod]
    public async Task TaoHopDongTrucTiep_KhachDaGanCungPhong_GhiNhanThanhCong()
    {
        var id = await DangKyAsync("012345678914");
        await _db.ExecuteAsync("UPDATE ho_so_khach_thue SET phong_id=@p WHERE tai_khoan_id=@id", new { p = _phong1Id, id });
        await _db.ExecuteAsync("UPDATE phong SET trang_thai='DaThue' WHERE id=@p", new { p = _phong1Id });
        var response = await Call(ActionNames.HopDongTao, _chuTroToken, new
        {
            phongId = _phong1Id, nguoiDaiDienId = id, ngayBatDau = "2026-12-01",
            ngayKetThuc = "2027-12-01", giaThue = 2500000, trangThai = "HieuLuc",
        });
        Assert.IsTrue(response.Success, response.Message);
        Assert.AreEqual("HieuLuc", await _db.ScalarT<string>("SELECT trang_thai FROM hop_dong WHERE phong_id=@p", new { p = _phong1Id }));
    }

    [TestMethod]
    public async Task CapNhat_HoSoDayDu_CoTheGhiNhanPhongVaTamTru()
    {
        var tk = await DangKyAsync("012345678901");
        var resp = await Call(ActionNames.KHACH_THUE_CAP_NHAT, _chuTroToken, new
        {
            taiKhoanId = tk, hoTen = "Ten da sua", cccd = "012345678902",
            ngaySinh = "1999-02-03", soDienThoai = "0902222333", queQuan = "Hue",
            noiLamViec = "Cong ty moi", daDangKyTamTru = true, phongId = _phong1Id,
        });
        Assert.IsTrue(resp.Success, resp.Message);
        var profile = await _db.QueryAsync("SELECT * FROM ho_so_khach_thue WHERE tai_khoan_id = @id", new { id = tk });
        var account = await _db.ScalarT<string>("SELECT ho_ten FROM tai_khoan WHERE id = @id", new { id = tk });
        Assert.AreEqual("Ten da sua", account);
        Assert.AreEqual("012345678902", profile[0]["cccd"]);
        Assert.AreEqual(_phong1Id, Convert.ToInt32(profile[0]["phong_id"]));
        Assert.AreEqual(1, Convert.ToInt32(profile[0]["da_dang_ky_tam_tru"]));
        Assert.AreEqual(1, await _db.ScalarT<int>("SELECT COUNT(*) FROM lich_su_cu_tru WHERE tai_khoan_khach_id = @id AND loai_bien_dong = 'NhanPhong'", new { id = tk }));
    }

    [TestMethod]
    public async Task CapNhat_HoSoVaMatKhauSai_KhongDoiThongTinHoSo()
    {
        var id = await DangKyAsync("012345678909");
        var profile = await _db.QueryAsync("SELECT * FROM ho_so_khach_thue WHERE tai_khoan_id=@id", new { id });
        var response = await Call(ActionNames.KHACH_THUE_CAP_NHAT, _chuTroToken, new
        {
            taiKhoanId = id, hoTen = "Khong duoc luu", cccd = "012345678909", ngaySinh = "1999-02-03",
            soDienThoai = "0902222337", queQuan = "Hue", noiLamViec = "Moi",
            matKhauMoi = "abc",
        });
        Assert.IsFalse(response.Success);
        Assert.AreEqual("Khach Test", await _db.ScalarT<string>("SELECT ho_ten FROM tai_khoan WHERE id=@id", new { id }));
        Assert.AreEqual(profile[0]["so_dien_thoai"], (await _db.QueryAsync("SELECT so_dien_thoai FROM ho_so_khach_thue WHERE tai_khoan_id=@id", new { id }))[0]["so_dien_thoai"]);
    }

    [TestMethod]
    public async Task CapNhat_QuanLyKhongDuocDatLaiMatKhauKhach()
    {
        var id = await DangKyAsync("012345678918");
        await _db.ExecuteAsync("INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro, khu_tro_id) VALUES ('qla',@h,'QL','QuanLy',@k)", new { h = PasswordHasher.Hash("qla"), k = _khuId });
        var manager = new AuthenticationService(_db, _sessions);
        var token = (await manager.LoginAsync("qla", "qla")).Token;
        var response = await Call(ActionNames.KHACH_THUE_CAP_NHAT, token, new
        {
            taiKhoanId = id, hoTen = "Khach Test", cccd = "012345678918", ngaySinh = "1999-02-03",
            soDienThoai = "0902222343", queQuan = "Hue", matKhauMoi = "password123",
        });
        Assert.IsFalse(response.Success);
        Assert.IsFalse((await _db.ScalarT<string>("SELECT mat_khau_hash FROM tai_khoan WHERE id=@id", new { id }))!.Contains(PasswordHasher.Hash("password123")));
    }

    [TestMethod]
    public async Task TimKiem_TraThongTinCanThietDeSuaHoSo()
    {
        var id = await DangKyAsync("012345678911");
        await _db.ExecuteAsync("UPDATE ho_so_khach_thue SET phong_id=@p, noi_lam_viec='Cong ty cu' WHERE tai_khoan_id=@id", new { id, p = _phong1Id });
        await _db.ExecuteAsync("INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro, khu_tro_id) VALUES ('qsearch',@h,'QL','QuanLy',@k)", new { h = PasswordHasher.Hash("qsearch"), k = _khuId });
        var manager = new AuthenticationService(_db, _sessions);
        var token = (await manager.LoginAsync("qsearch", "qsearch")).Token;
        var response = await Call(ActionNames.KHACH_THUE_TIM_KIEM, token, new { keyword = "012345678911" });
        Assert.IsTrue(response.Success, response.Message);
        var json = Raw(response);
        StringAssert.Contains(json, "ngaySinh");
        StringAssert.Contains(json, "queQuan");
        StringAssert.Contains(json, "noiLamViec");
    }

    [TestMethod]
    public async Task TimKiem_QuanLyKhongNhanThongTinKhachNgoaiKhu()
    {
        var id = await DangKyAsync("012345678931", "Khach Ngoai Khu");
        var otherSite = await _db.ExecuteAsync("INSERT INTO khu_tro (ten_khu_tro, dia_chi) VALUES ('Khu B','B')");
        var otherRoom = await _db.ExecuteAsync("INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da) VALUES (@k,'201',1,1)", new { k = otherSite });
        await _db.ExecuteAsync("UPDATE ho_so_khach_thue SET phong_id=@p, noi_lam_viec='Noi nhay cam' WHERE tai_khoan_id=@id", new { p = otherRoom, id });
        await _db.ExecuteAsync("INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro, khu_tro_id) VALUES ('qsearch',@h,'QL','QuanLy',@k)", new { h = PasswordHasher.Hash("qsearch"), k = _khuId });
        var token = (await new AuthenticationService(_db, _sessions).LoginAsync("qsearch", "qsearch")).Token;
        var response = await Call(ActionNames.KHACH_THUE_TIM_KIEM, token, new { keyword = "012345678931" });
        Assert.IsTrue(response.Success, response.Message);
        Assert.AreEqual("[]", Raw(response));
    }

    [TestMethod]
    public async Task CapNhat_TuChoiPhongBaoTri_KhongLuuHoSo()
    {
        var id = await DangKyAsync("012345678913");
        await _db.ExecuteAsync("UPDATE phong SET trang_thai='BaoTri' WHERE id=@p", new { p = _phong1Id });
        var response = await Call(ActionNames.KHACH_THUE_CAP_NHAT, _chuTroToken, new
        {
            taiKhoanId = id, hoTen = "Khong luu", cccd = "012345678913", ngaySinh = "1999-02-03",
            soDienThoai = "0902222340", queQuan = "Hue", phongId = _phong1Id,
        });
        Assert.IsFalse(response.Success);
        Assert.AreEqual("Khach Test", await _db.ScalarT<string>("SELECT ho_ten FROM tai_khoan WHERE id=@id", new { id }));
    }

    [TestMethod]
    public async Task CapNhat_MatKhauSai_KhongLuuMotPhanHoSo()
    {
        var id = await DangKyAsync("012345678912");
        var response = await Call(ActionNames.KHACH_THUE_CAP_NHAT, _chuTroToken, new
        {
            taiKhoanId = id, hoTen = "Khong luu", cccd = "012345678912", ngaySinh = "1999-02-03",
            soDienThoai = "0902222339", queQuan = "Hue", noiLamViec = "Moi", matKhauMoi = "abc",
        });
        Assert.IsFalse(response.Success);
        Assert.AreEqual("Khach Test", await _db.ScalarT<string>("SELECT ho_ten FROM tai_khoan WHERE id=@id", new { id }));
    }

    [TestMethod]
    public async Task CapNhat_HoSoPhongBaoTri_VanChoPhepSuaThongTin()
    {
        var id = await DangKyAsync("012345678908");
        await _db.ExecuteAsync("UPDATE ho_so_khach_thue SET phong_id=@p WHERE tai_khoan_id=@id", new { p = _phong1Id, id });
        await _db.ExecuteAsync("UPDATE phong SET trang_thai='BaoTri' WHERE id=@p", new { p = _phong1Id });
        var response = await Call(ActionNames.KHACH_THUE_CAP_NHAT, _chuTroToken, new
        {
            taiKhoanId = id, hoTen = "Thong tin moi", cccd = "012345678908", ngaySinh = "1999-02-03",
            soDienThoai = "0902222335", queQuan = "Hue", noiLamViec = "Cong ty", phongId = _phong1Id,
        });
        Assert.IsTrue(response.Success, response.Message);
        Assert.AreEqual("Thong tin moi", await _db.ScalarT<string>("SELECT ho_ten FROM tai_khoan WHERE id=@id", new { id }));
    }

    [TestMethod]
    public async Task LayTheoPhong_TraDayDuHoSoDeSua()
    {
        var tk = await DangKyAsync("012345678901");
        await _db.ExecuteAsync("UPDATE ho_so_khach_thue SET phong_id=@p, noi_lam_viec='Cong ty' WHERE tai_khoan_id=@id", new { p = _phong1Id, id = tk });
        var resp = await Call(ActionNames.KHACH_THUE_LAY_THEO_PHONG, _chuTroToken, new { phongId = _phong1Id });
        Assert.IsTrue(resp.Success, resp.Message);
        var json = Raw(resp);
        StringAssert.Contains(json, "ngaySinh");
        StringAssert.Contains(json, "queQuan");
        StringAssert.Contains(json, "tenDangNhap");
        StringAssert.Contains(json, "noiLamViec");
    }

    [TestMethod]
    public async Task ChuyenPhong_NguyenTu_GhiLichSu_2BienDong()
    {
        var tk = await DangKyAsync("012345678901");
        // gán vào phòng 1 trực tiếp
        await _db.ExecuteAsync("UPDATE ho_so_khach_thue SET phong_id = @p WHERE tai_khoan_id = @t",
            new { p = _phong1Id, t = tk });
        await _db.ExecuteAsync("UPDATE phong SET trang_thai='DaThue' WHERE id=@p", new { p = _phong1Id });
        await _db.ExecuteAsync("INSERT INTO lich_su_cu_tru (tai_khoan_khach_id, phong_id, khu_tro_id, loai_bien_dong) VALUES (@t, @p, @k, 'NhanPhong')",
            new { t = tk, p = _phong1Id, k = _khuId });

        var resp = await Call(ActionNames.KHACH_THUE_CHUYEN_PHONG, _chuTroToken,
            new { taiKhoanId = tk, phongIdMoi = _phong2Id });
        Assert.IsTrue(resp.Success, resp.Message);

        var phongIdHienTai = await _db.ScalarT<int?>(
            "SELECT phong_id FROM ho_so_khach_thue WHERE tai_khoan_id = @t", new { t = tk });
        Assert.AreEqual(_phong2Id, phongIdHienTai);
        var soBienDong = await _db.ScalarT<int>(
            "SELECT COUNT(*) FROM lich_su_cu_tru WHERE tai_khoan_khach_id = @t", new { t = tk });
        Assert.AreEqual(3, soBienDong); // NhanPhong + ChuyenDi + ChuyenDen
        Assert.AreEqual("Trong", await _db.ScalarT<string>("SELECT trang_thai FROM phong WHERE id=@p", new { p = _phong1Id }));
        Assert.AreEqual("DaThue", await _db.ScalarT<string>("SELECT trang_thai FROM phong WHERE id=@p", new { p = _phong2Id }));
    }

    [TestMethod]
    public async Task ChuyenPhong_PhongDay_BiTuChoi()
    {
        var tk1 = await DangKyAsync("012345678901");
        var tk2 = await DangKyAsync("012345678902");
        // phòng 1 sức chứa 2, đã có 2 người (trực tiếp) — chuyển tk2 vào sẽ đầy rồi
        // chuyển tk1 từ phòng 1 sang phòng 1 (chính nó) → không đổi, nhưng thử sức chứa đầy:
        await _db.ExecuteAsync("UPDATE ho_so_khach_thue SET phong_id = @p WHERE tai_khoan_id IN (@a, @b)",
            new { p = _phong1Id, a = tk1, b = tk2 });
        var tk3 = await DangKyAsync("012345678903");
        var resp = await Call(ActionNames.KHACH_THUE_CHUYEN_PHONG, _chuTroToken,
            new { taiKhoanId = tk3, phongIdMoi = _phong1Id });
        Assert.IsFalse(resp.Success);
        StringAssert.Contains(resp.Message, "đầy");
    }

    [TestMethod]
    public async Task TraPhong_Idempotent_HoSoRong()
    {
        var tk = await DangKyAsync("012345678901");
        await _db.ExecuteAsync("UPDATE ho_so_khach_thue SET phong_id = @p WHERE tai_khoan_id = @t",
            new { p = _phong1Id, t = tk });
        await _db.ExecuteAsync("UPDATE phong SET trang_thai='DaThue' WHERE id=@p", new { p = _phong1Id });
        var r1 = await Call(ActionNames.KHACH_THUE_TRA_PHONG, _chuTroToken, new { taiKhoanId = tk });
        Assert.IsTrue(r1.Success, r1.Message);
        var r2 = await Call(ActionNames.KHACH_THUE_TRA_PHONG, _chuTroToken, new { taiKhoanId = tk });
        Assert.IsTrue(r2.Success, "gọi lần 2 vẫn ok (idempotent): " + r2.Message);
        var phongId = await _db.ScalarT<int?>(
            "SELECT phong_id FROM ho_so_khach_thue WHERE tai_khoan_id = @t", new { t = tk });
        Assert.IsNull(phongId);
        Assert.AreEqual("Trong", await _db.ScalarT<string>("SELECT trang_thai FROM phong WHERE id=@p", new { p = _phong1Id }));
    }

    [TestMethod]
    public async Task TimKiem_Toida50_KeywordRong_Sai()
    {
        for (var i = 0; i < 5; i++)
            await DangKyAsync($"0123456789{i:D2}", $"Khach {i}");
        var resp = await Call(ActionNames.KHACH_THUE_TIM_KIEM, _chuTroToken, new { keyword = "Khach" });
        Assert.IsTrue(resp.Success, resp.Message);
        Assert.IsTrue(Raw(resp).Contains("Khach"));

        var rong = await Call(ActionNames.KHACH_THUE_TIM_KIEM, _chuTroToken, new { keyword = "" });
        Assert.IsFalse(rong.Success);

        var dacbiet = await Call(ActionNames.KHACH_THUE_TIM_KIEM, _chuTroToken, new { keyword = "%" });
        Assert.IsTrue(dacbiet.Success, "ký tự % phải được escape, không trả toàn bộ");
        Assert.AreEqual("[]", Raw(dacbiet), "% không khớp ai cả");
    }

    [TestMethod]
    public async Task KhachThue_XemHoSoCuaToi_KhongThayHoSoNguoiKhac()
    {
        var tk = await DangKyAsync("012345678901");
        var auth = new AuthenticationService(_db, _sessions);
        var s = await auth.LoginAsync("012345678901", "mk123456");
        var resp = await Call(ActionNames.KHACH_THUE_HO_SO_CUA_TOI, s.Token, null);
        Assert.IsTrue(resp.Success, resp.Message);
        var json = Raw(resp);
        StringAssert.Contains(json, "Khach Test");
    }
}
