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
