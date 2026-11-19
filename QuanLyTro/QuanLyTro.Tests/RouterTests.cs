using QuanLyTro.Server.Data;
using QuanLyTro.Server.Network;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;
using QuanLyTro.Shared.Protocol;
using QuanLyTro.Tests.TestHelpers;

namespace QuanLyTro.Tests;

[TestClass]
public class RouterTests
{
    private Database _db = null!;
    private SessionStore _sessions = null!;
    private RequestRouter _router = null!;

    [TestInitialize]
    public async Task Setup()
    {
        await TestDb.ResetAsync();
        _db = TestDb.Create();
        await new SchemaInitializer(_db).InitializeAsync();
        await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES ('chutro', @h, 'Chu Tro', 'ChuTro')",
            new { h = PasswordHasher.Hash("chutro") });
        await _db.ExecuteAsync(
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES ('letan', @h, 'Le Tan', 'LeTan')",
            new { h = PasswordHasher.Hash("letan") });
        _sessions = new SessionStore();
        var auth = new AuthenticationService(_db, _sessions);
        await auth.LoginAsync("chutro", "chutro"); // session 1: ChuTro
        await auth.LoginAsync("letan", "letan");   // session 2: LeTan
        _router = new RequestRouter(_db, _sessions);
    }

    private static RequestPacket P(string action, string? token = null, object? data = null) =>
        RequestPacket.Create(action, token, data);

    [TestMethod]
    public async Task UnknownAction_ReturnsGenericDeny_NotFound()
    {
        var resp = await _router.HandleAsync(P("HANH_DONG_MAO_HIEM"), new ClientContext("t1"));
        Assert.IsFalse(resp.Success);
        StringAssert.Contains(resp.Message, "Khong tim thay du lieu hoac khong co quyen");
    }

    [TestMethod]
    public async Task LeTan_CallsAccountLock_Denied()
    {
        var token = (await _sessions.GetAsync((await GetTokenAsync("letan")))).Token;
        var resp = await _router.HandleAsync(P(ActionNames.TAI_KHOAN_KHOA, token), new ClientContext("t2"));
        Assert.IsFalse(resp.Success);
    }

    [TestMethod]
    public async Task ChuTro_CallsAccountLock_PassesToHandler()
    {
        var token = await GetTokenAsync("chutro");
        var resp = await _router.HandleAsync(
            P(ActionNames.TAI_KHOAN_KHOA, token, new { tenDangNhap = "letan", lyDo = "test" }),
            new ClientContext("t3"));
        Assert.IsTrue(resp.Success, resp.Message);
    }

    [TestMethod]
    public async Task InvalidToken_Denied()
    {
        var resp = await _router.HandleAsync(P(ActionNames.TAI_KHOAN_LAY_TAT_CA, "token-gia"), new ClientContext("t4"));
        Assert.IsFalse(resp.Success);
    }

    [TestMethod]
    public async Task DangNhap_Public_NoTokenNeeded()
    {
        var resp = await _router.HandleAsync(
            P(ActionNames.DANG_NHAP, null, new { tenDangNhap = "chutro", matKhau = "chutro" }),
            new ClientContext("t5"));
        Assert.IsTrue(resp.Success, resp.Message);
        Assert.IsNotNull(resp.Data);
    }

    private async Task<string> GetTokenAsync(string username)
    {
        var all = await _db.QueryAsync("SELECT 1"); // placeholder để tránh warning unused
        // lấy session theo username từ store: dùng Login lại để có token chắc chắn
        var auth = new AuthenticationService(_db, _sessions);
        var s = await auth.LoginAsync(username, username);
        return s.Token;
    }
}
