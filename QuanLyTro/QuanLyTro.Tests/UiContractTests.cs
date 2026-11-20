namespace QuanLyTro.Tests;

[TestClass]
public class UiContractTests
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
    public void Login_Page_HasRequiredElements()
    {
        var html = File.ReadAllText(Path.Combine(FindWwwroot(), "index.html"));
        StringAssert.Contains(html, "id=\"tenDangNhap\"");
        StringAssert.Contains(html, "id=\"matKhau\"");
        StringAssert.Contains(html, "id=\"btnDangNhap\"");
    }

    [TestMethod]
    public void ApiJs_UsesRequestId_OverBridge()
    {
        var js = File.ReadAllText(Path.Combine(FindWwwroot(), "shared", "js", "api.js"));
        StringAssert.Contains(js, "requestId");
        StringAssert.Contains(js, "postMessage");
    }

    [TestMethod]
    public void All_SixShells_Exist()
    {
        var root = FindWwwroot();
        foreach (var shell in new[] { "chutro", "quanly", "letan", "nhanvien", "khachthue", "congan" })
        {
            var path = Path.Combine(root, shell, "index.html");
            Assert.IsTrue(File.Exists(path), $"thiếu shell {shell}");
            var html = File.ReadAllText(path);
            StringAssert.Contains(html, "shared/js/api.js", $"{shell} phải nạp api.js");
            StringAssert.Contains(html, "shared/js/shell.js", $"{shell} phải nạp shell.js (nút đăng xuất dùng chung)");
        }
    }

    [TestMethod]
    public void ChuTro_Shell_HasKhuListAndPhongForm()
    {
        var html = File.ReadAllText(Path.Combine(FindWwwroot(), "chutro", "index.html"));
        StringAssert.Contains(html, "id=\"khuList\"");
        StringAssert.Contains(html, "id=\"phongTable\"");
        StringAssert.Contains(html, "KHU_TRO_THEM");
        StringAssert.Contains(html, "PHONG_THEM");
    }
}
