using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using QuanLyTro.Shared.Protocol;

namespace QuanLyTro.Tests.E2E;

[TestClass]
public class RealServerE2E
{
    private static async Task<ResponsePacket> SendAsync(string action, string? token, object? data)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, 8888);
        var stream = client.GetStream();
        var packet = RequestPacket.Create(action, token, data);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(packet, JsonDefaults.Options);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();

        var line = new MemoryStream();
        var buf = new byte[4096];
        while (true)
        {
            var n = await stream.ReadAsync(buf);
            if (n == 0) throw new EndOfStreamException("server closed");
            for (var i = 0; i < n; i++)
            {
                if (buf[i] == (byte)'\n')
                    return JsonDefaults.Deserialize<ResponsePacket>(Encoding.UTF8.GetString(line.ToArray()))!;
                line.WriteByte(buf[i]);
            }
        }
    }

    private static string Raw(ResponsePacket r) => JsonDefaults.Serialize(r.Data!);

    [TestMethod]
    public async Task E2E_Login_Chutro_TaoKhuPhong_DangKyKhach()
    {
        var login = await SendAsync(ActionNames.DANG_NHAP, null, new { tenDangNhap = "chutro", matKhau = "chutro" });
        Assert.IsTrue(login.Success, "login: " + login.Message);
        var token = JsonDefaults.Deserialize<JsonElement>(Raw(login)).GetProperty("token").GetString()!;
        var khu = await SendAsync(ActionNames.KHU_TRO_THEM, token, new { tenKhuTro = "Khu X1", diaChi = "DN" });
        Assert.IsTrue(khu.Success, "khu: " + khu.Message);
        var khuId = JsonDefaults.Deserialize<JsonElement>(Raw(khu)).GetProperty("khuTroId").GetInt32();
        var phong = await SendAsync(ActionNames.PHONG_THEM, token, new { khuTroId = khuId, soPhong = "X1", giaThue = 2, soNguoiToiDa = 2 });
        Assert.IsTrue(phong.Success, "phong: " + phong.Message);
        var stamp = DateTime.Now.ToString("ffffff");
        var dk = await SendAsync(ActionNames.KHACH_THUE_DANG_KY, null, new
        {
            tenDangNhap = $"e2e{stamp}",
            matKhau = "mk123456",
            hoTen = "Khach E2E",
            cccd = $"9{stamp:D11}",
            ngaySinh = "2001-06-15",
            soDienThoai = "0900000000",
            queQuan = "Da Nang",
        });
        Assert.IsTrue(dk.Success, "dang ky: " + dk.Message);
        var khLogin = await SendAsync(ActionNames.DANG_NHAP, null, new { tenDangNhap = $"e2e{stamp}", matKhau = "mk123456" });
        Assert.IsTrue(khLogin.Success, "login khach: " + khLogin.Message);
        var khToken = JsonDefaults.Deserialize<JsonElement>(Raw(khLogin)).GetProperty("token").GetString()!;
        var profile = await SendAsync(ActionNames.KHACH_THUE_HO_SO_CUA_TOI, khToken, null);
        Assert.IsTrue(profile.Success, "ho so: " + profile.Message);
        StringAssert.Contains(Raw(profile), "Khach E2E");
        var qlLogin = await SendAsync(ActionNames.DANG_NHAP, null, new { tenDangNhap = "quanly", matKhau = "quanly" });
        var qlToken = JsonDefaults.Deserialize<JsonElement>(Raw(qlLogin)).GetProperty("token").GetString()!;
        var denied = await SendAsync(ActionNames.TAI_KHOAN_KHOA, qlToken, new { tenDangNhap = "letan", lyDo = "test" });
        Assert.IsFalse(denied.Success, "QuanLy phai bi tu choi TAI_KHOAN_KHOA");
        var logout = await SendAsync(ActionNames.DANG_XUAT, token, null);
        Assert.IsTrue(logout.Success, "logout: " + logout.Message);
        var afterLogout = await SendAsync(ActionNames.TAI_KHOAN_LAY_TAT_CA, token, null);
        Assert.IsFalse(afterLogout.Success, "token sau logout phai het hieu luc");
    }
}
