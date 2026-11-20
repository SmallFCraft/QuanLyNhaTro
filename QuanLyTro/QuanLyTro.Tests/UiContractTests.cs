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
        var html = File.ReadAllText(Path.Combine(FindWwwroot(), "auth", "index.html"));
        StringAssert.Contains(html, "id=\"loginUsername\"");
        StringAssert.Contains(html, "id=\"loginPassword\"");
        StringAssert.Contains(html, "id=\"loginForm\"");
        StringAssert.Contains(html, "id=\"formRegister\"", "BR-17: đăng ký khách thuê phải có mặt");
    }

    [TestMethod]
    public void BridgeJs_UsesRequestId_OverWebView2()
    {
        var js = File.ReadAllText(Path.Combine(FindWwwroot(), "shared", "js", "bridge.js"));
        StringAssert.Contains(js, "requestId");
        StringAssert.Contains(js, "postMessage");
        // shim tương thích cho JS nghiệp vụ gọi window.bridge.call
        var chrome = File.ReadAllText(Path.Combine(FindWwwroot(), "shared", "js", "chrome.js"));
        StringAssert.Contains(chrome, "window.bridge");
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
            StringAssert.Contains(html, "shared/js/bridge.js", $"{shell} phải nạp bridge.js");
            StringAssert.Contains(html, "shared/js/chrome.js", $"{shell} phải nạp chrome.js (chrome + shim đăng xuất)");
        }
    }

    [TestMethod]
    public void ChuTro_Shell_HasLegacyBusinessPanes()
    {
        var html = File.ReadAllText(Path.Combine(FindWwwroot(), "chutro", "index.html"));
        StringAssert.Contains(html, "id=\"tab-dash\"");
        StringAssert.Contains(html, "id=\"tab-phong\"");
        StringAssert.Contains(html, "id=\"phong-body\"");
        StringAssert.Contains(html, "js/main.js");
        StringAssert.Contains(html, "js/phong.js");
    }

    [TestMethod]
    public void KhachThue_Shell_KeepsContractIds()
    {
        var html = File.ReadAllText(Path.Combine(FindWwwroot(), "khachthue", "index.html"));
        StringAssert.Contains(html, "id=\"tenantTabs\"");
        StringAssert.Contains(html, "id=\"tab-nhan-phong\"");
        StringAssert.Contains(html, "id=\"checkinTabs\"");
    }
}
