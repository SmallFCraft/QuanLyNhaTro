using QuanLyTro.Server.Data;
using QuanLyTro.Server.Network;
using QuanLyTro.Server.Security;
using QuanLyTro.Server.Services;
using QuanLyTro.Shared.Models;
using QuanLyTro.Shared.Protocol;
using System.Text.Json;
using QuanLyTro.Tests.TestHelpers;

namespace QuanLyTro.Tests;

[TestClass]
public class SiteRoomTests
{
    private Database _db = null!;
    private SessionStore _sessions = null!;
    private RequestRouter _router = null!;
    private string _chuTroToken = null!;
    private string _quanLyAToken = null!;

    [TestInitialize]
    public async Task Setup()
    {
        await TestDb.ResetAsync();
        _db = TestDb.Create();
        await new SchemaInitializer(_db).InitializeAsync();
        _sessions = new SessionStore();
        var auth = new AuthenticationService(_db, _sessions);
        _router = new RequestRouter(_db, _sessions);

        // ChuTro
        await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES ('chutro', @h, 'Chu Tro', 'ChuTro')",
            new { h = PasswordHasher.Hash("chutro") });
        // QuanLy khu A (khu tạo sau, gán bằng PHAN_CONG)
        await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES ('qla', @h, 'QL A', 'QuanLy')",
            new { h = PasswordHasher.Hash("qla") });
        await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES ('letan', @h, 'LT', 'LeTan')",
            new { h = PasswordHasher.Hash("letan") });

        var s1 = await auth.LoginAsync("chutro", "chutro");
        _chuTroToken = s1.Token;
        var s2 = await auth.LoginAsync("qla", "qla");
        _quanLyAToken = s2.Token; // khu_tro_id cập nhật sau khi gán; session scope sẽ đọc lại từ DB
        await auth.LoginAsync("letan", "letan");
    }


    private static int GetInt(ResponsePacket resp, string prop)
    {
        var json = JsonDefaults.Serialize(resp.Data!);
        var el = JsonDefaults.Deserialize<System.Text.Json.JsonElement>(json);
        return el.GetProperty(prop).GetInt32();
    }

    private static string GetRaw(ResponsePacket resp) => JsonDefaults.Serialize(resp.Data!);

    private async Task<ResponsePacket> Call(string action, string? token, object? data, string ctx = "c") =>
        await _router.HandleAsync(RequestPacket.Create(action, token, data), new ClientContext(ctx));

    [TestMethod]
    public async Task ChuTro_TaoKhuVaPhong_Ok()
    {
        var khu = await Call(ActionNames.KHU_TRO_THEM, _chuTroToken,
            new { tenKhuTro = "Khu A", diaChi = "Da Nang" });
        Assert.IsTrue(khu.Success, khu.Message);
        var khuId = GetInt(khu, "khuTroId");

        var phong = await Call(ActionNames.PHONG_THEM, _chuTroToken,
            new { khuTroId = khuId, soPhong = "101", giaThue = 2500000m, soNguoiToiDa = 3 });
        Assert.IsTrue(phong.Success, phong.Message);

        // Số phòng trùng trong cùng khu bị UNIQUE chặn → BusinessRule
        var dup = await Call(ActionNames.PHONG_THEM, _chuTroToken,
            new { khuTroId = khuId, soPhong = "101", giaThue = 2500000m, soNguoiToiDa = 3 });
        Assert.IsFalse(dup.Success);
    }

    [TestMethod]
    public async Task ChuTro_GanPhanQuyen_DoiTacDongTaiRuntime()
    {
        var khu = GetInt(await Call(ActionNames.KHU_TRO_THEM, _chuTroToken,
            new { tenKhuTro = "A", diaChi = "x" }), "khuTroId");
        var room = GetInt(await Call(ActionNames.PHONG_THEM, _chuTroToken,
            new { khuTroId = khu, soPhong = "101", giaThue = 1000000m, soNguoiToiDa = 2 }), "phongId");
        await _db.ExecuteAsync("INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro, khu_tro_id) VALUES ('qlrights',@h,'QL','QuanLy',@k)", new { h = PasswordHasher.Hash("qlrights"), k = khu });
        var manager = new AuthenticationService(_db, _sessions);
        var token = (await manager.LoginAsync("qlrights", "qlrights")).Token;
        var saved = await Call(ActionNames.PHAN_QUYEN_CAP_NHAT_VAI_TRO, _chuTroToken, new
        {
            vaiTro = "QuanLy", thuHoi = new[] { ActionNames.PHONG_LAY_TAT_CA }, capQuyen = Array.Empty<string>()
        });
        Assert.IsTrue(saved.Success, saved.Message);
        var response = await Call(ActionNames.PHONG_LAY_TAT_CA, token, new { });
        Assert.IsFalse(response.Success);
    }

    [TestMethod]
    public async Task ChuTro_TuChoiThuHoiQuyenNen_KhongDanhDauVaiTroTuyChinh()
    {
        var denied = await Call(ActionNames.PHAN_QUYEN_CAP_NHAT_VAI_TRO, _chuTroToken, new
        {
            vaiTro = "ChuTro", thuHoi = new[] { ActionNames.DANG_NHAP }, capQuyen = Array.Empty<string>()
        });
        Assert.IsFalse(denied.Success);
        Assert.AreEqual(0, await _db.ScalarT<int>("SELECT COUNT(*) FROM quyen_vai_tro_tuy_chinh WHERE vai_tro='ChuTro'"));
        Assert.IsTrue((await Call(ActionNames.PHONG_LAY_TAT_CA, _chuTroToken, new { })).Success);
    }

    [TestMethod]
    public async Task ChuTro_LanDauTuyChinhKhongCapLaiQuyenDaThieu()
    {
        await _db.ExecuteAsync("INSERT INTO quyen_vai_tro (vai_tro, hanh_dong) VALUES ('QuanLy', @a)", new { a = ActionNames.PHONG_LAY_CHI_TIET });
        var saved = await Call(ActionNames.PHAN_QUYEN_CAP_NHAT_VAI_TRO, _chuTroToken, new
        {
            vaiTro = "QuanLy", thuHoi = Array.Empty<string>(), capQuyen = Array.Empty<string>()
        });
        Assert.IsTrue(saved.Success, saved.Message);
        Assert.AreEqual(0, await _db.ScalarT<int>("SELECT COUNT(*) FROM quyen_vai_tro WHERE vai_tro='QuanLy' AND hanh_dong=@a", new { a = ActionNames.PHONG_LAY_TAT_CA }));
        Assert.AreEqual(1, await _db.ScalarT<int>("SELECT COUNT(*) FROM quyen_vai_tro WHERE vai_tro='QuanLy' AND hanh_dong=@a", new { a = ActionNames.PHONG_LAY_CHI_TIET }));
    }

    [TestMethod]
    public async Task ChuTro_CapNhatPhong_ThayDoiSoPhongVaGia()
    {
        var khu = GetInt(await Call(ActionNames.KHU_TRO_THEM, _chuTroToken,
            new { tenKhuTro = "A", diaChi = "x" }), "khuTroId");
        var phong = GetInt(await Call(ActionNames.PHONG_THEM, _chuTroToken,
            new { khuTroId = khu, soPhong = "101", giaThue = 1000000m, soNguoiToiDa = 2 }), "phongId");
        var resp = await Call(ActionNames.PHONG_CAP_NHAT, _chuTroToken,
            new { phongId = phong, soPhong = "A-101", giaThue = 1500000m, soNguoiToiDa = 3, moTa = "da sua" });
        Assert.IsTrue(resp.Success, resp.Message);
        var rows = await _db.QueryAsync("SELECT so_phong, gia_thue, so_nguoi_toi_da FROM phong WHERE id=@id", new { id = phong });
        Assert.AreEqual("A-101", rows[0]["so_phong"]);
        Assert.AreEqual(1500000m, Convert.ToDecimal(rows[0]["gia_thue"]));
        Assert.AreEqual(3, Convert.ToInt32(rows[0]["so_nguoi_toi_da"]));
    }

    [TestMethod]
    public async Task QuanLy_ScopeKhu_KhongThayKhuKhac()
    {
        // ChuTro tạo khu A + B, phòng A1 và B1
        var khuA = GetInt(await Call(ActionNames.KHU_TRO_THEM, _chuTroToken,
            new { tenKhuTro = "A", diaChi = "x" }), "khuTroId");
        var khuB = GetInt(await Call(ActionNames.KHU_TRO_THEM, _chuTroToken,
            new { tenKhuTro = "B", diaChi = "y" }), "khuTroId");
        await Call(ActionNames.PHONG_THEM, _chuTroToken, new { khuTroId = khuA, soPhong = "A1", giaThue = 1m, soNguoiToiDa = 1 });
        await Call(ActionNames.PHONG_THEM, _chuTroToken, new { khuTroId = khuB, soPhong = "B1", giaThue = 1m, soNguoiToiDa = 1 });
        Assert.AreNotEqual(khuA, khuB, "khuA=" + khuA + " khuB=" + khuB);

        // Gán QuanLy (tài khoản 'qla') vào khu A
        var gans = await Call(ActionNames.KHU_TRO_PHAN_CONG_QUAN_LY, _chuTroToken,
            new { khuTroId = khuA, tenDangNhapQuanLy = "qla" });
        Assert.IsTrue(gans.Success, "PHAN_CONG: " + gans.Message);

        // Session 'qla' đã có trước khi gán → cần đăng nhập lại để nhận khu mới
        var auth = new AuthenticationService(_db, _sessions);
        var fresh = await auth.LoginAsync("qla", "qla");
        Assert.AreEqual(khuA, fresh.KhuTroId, "session mới phải mang khu_tro_id vừa gán");

        // QuanLy khu A thấy danh sách phòng khu A, KHÔNG thấy B1
        var list = await Call(ActionNames.PHONG_LAY_TAT_CA, fresh.Token, null);
        Assert.IsTrue(list.Success, "PHONG_LIST: " + list.Message);
        var json = GetRaw(list);
        StringAssert.Contains(json, "A1");
        Assert.IsFalse(json.Contains("B1"), "json=" + json);
    }

    [TestMethod]
    public async Task QuanLy_GuiKhuTroIdKhac_BiTuChoi_CungMessage()
    {
        var khuA = GetInt(await Call(ActionNames.KHU_TRO_THEM, _chuTroToken,
            new { tenKhuTro = "A", diaChi = "x" }), "khuTroId");
        await Call(ActionNames.KHU_TRO_PHAN_CONG_QUAN_LY, _chuTroToken,
            new { khuTroId = khuA, tenDangNhapQuanLy = "qla" });
        var auth = new AuthenticationService(_db, _sessions);
        var fresh = await auth.LoginAsync("qla", "qla");

        // QuanLy khu A cố thêm phòng vào khu B (không tồn tại khu B ở đây → cùng thông báo từ chối)
        var resp = await Call(ActionNames.PHONG_THEM, fresh.Token,
            new { khuTroId = khuA + 999, soPhong = "X1", giaThue = 1m, soNguoiToiDa = 1 });
        Assert.IsFalse(resp.Success);
        StringAssert.Contains(resp.Message, "Khong tim thay du lieu hoac khong co quyen");
    }

    [TestMethod]
    public async Task LeTan_PhongThem_BiTuChoi()
    {
        var auth = new AuthenticationService(_db, _sessions);
        var lt = await auth.LoginAsync("letan", "letan");
        var resp = await Call(ActionNames.PHONG_THEM, lt.Token,
            new { khuTroId = 1, soPhong = "X", giaThue = 1m, soNguoiToiDa = 1 });
        Assert.IsFalse(resp.Success);
    }

    [TestMethod]
    public async Task XoaKhu_ConPhong_BiCham()
    {
        var khu = await Call(ActionNames.KHU_TRO_THEM, _chuTroToken, new { tenKhuTro = "K", diaChi = "z" });
        var khuId = GetInt(khu, "khuTroId");
        await Call(ActionNames.PHONG_THEM, _chuTroToken, new { khuTroId = khuId, soPhong = "1", giaThue = 1m, soNguoiToiDa = 1 });
        var del = await Call(ActionNames.KHU_TRO_XOA, _chuTroToken, new { khuTroId = khuId });
        Assert.IsFalse(del.Success);
        StringAssert.Contains(del.Message, "còn phòng");
    }
}
