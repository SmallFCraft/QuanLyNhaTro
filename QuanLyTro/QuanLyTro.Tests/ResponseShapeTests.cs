using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuanLyTro.Server.Data;
using QuanLyTro.Server.Network;
using QuanLyTro.Server.Security;
using QuanLyTro.Shared.Models;
using QuanLyTro.Shared.Protocol;
using QuanLyTro.Tests.TestHelpers;

namespace QuanLyTro.Tests;

[TestClass]
public class ResponseShapeTests
{
    private Database _db = null!;
    private SessionStore _sessions = null!;
    private RequestRouter _router = null!;
    private AuthenticationService _auth = null!;
    private string _congAnToken = null!;

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
            "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES ('congan', @h, 'Cong An', 'CongAn')",
            new { h = PasswordHasher.Hash("congan") });
        _congAnToken = (await _auth.LoginAsync("congan", "congan")).Token;

        var khu = await _db.ExecuteAsync(
            "INSERT INTO khu_tro (ten_khu_tro, dia_chi) VALUES ('Khu A', 'Da Nang')");
        await _db.ExecuteAsync(
            "INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da, trang_thai) VALUES (@k, '101', 2500000, 2, 'Trong')",
            new { k = khu });
        await _db.ExecuteAsync(
            "INSERT INTO phong (khu_tro_id, so_phong, gia_thue, so_nguoi_toi_da, trang_thai) VALUES (@k, '102', 2700000, 2, 'DaThue')",
            new { k = khu });
    }

    private async Task<ResponsePacket> Call(string action, string? token, object? data) =>
        await _router.HandleAsync(RequestPacket.Create(action, token, data), new ClientContext("c"));

    [TestMethod]
    public async Task PHONG_LAY_TAT_CA_ReturnsArray_NotObject()
    {
        var resp = await Call(ActionNames.PHONG_LAY_TAT_CA, _congAnToken, new { });
        Assert.IsTrue(resp.Success, resp.Message);

        // Contract: Data must be an array (JsonValueKind.Array) so that JS for...of works
        var json = JsonDefaults.Serialize(resp.Data);
        var el = JsonDocument.Parse(json).RootElement;

        Assert.AreEqual(JsonValueKind.Array, el.ValueKind,
            "PHONG_LAY_TAT_CA Data must be JSON array, got " + el.ValueKind);

        // At least the seeded rooms should be present
        Assert.IsTrue(el.GetArrayLength() >= 2);
    }

    [TestMethod]
    public async Task KHACH_THUE_LAY_THEO_PHONG_ReturnsArray_NotObject()
    {
        var resp = await Call(ActionNames.KHACH_THUE_LAY_THEO_PHONG, _congAnToken, new { phongId = 0 });
        Assert.IsTrue(resp.Success, resp.Message);

        var json = JsonDefaults.Serialize(resp.Data);
        var el = JsonDocument.Parse(json).RootElement;

        Assert.AreEqual(JsonValueKind.Array, el.ValueKind,
            "KHACH_THUE_LAY_THEO_PHONG Data must be JSON array, got " + el.ValueKind);
    }
}

[TestClass]
public class BridgeFallbackContractTests
{
    private static string FindWwwroot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "..", "Assets", "wwwroot"));
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }
        throw new DirectoryNotFoundException("Không tìm thấy Assets/wwwroot");
    }

    [TestMethod]
    public void BridgeJs_FallbackOutsideWebView2_MustNotFakeSuccess()
    {
        var js = File.ReadAllText(Path.Combine(FindWwwroot(), "shared", "js", "bridge.js"));
        // Fallback khi không có WebView2 không được giả success với data = request
        // (nguồn gốc lỗi "phong is not iterable" trên static preview).
        Assert.IsFalse(js.Contains("success: true, data: data"),
            "bridge.js fallback không được trả success:true với data của request");
        StringAssert.Contains(js, "success === false");
    }
}
