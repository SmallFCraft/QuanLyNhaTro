namespace QuanLyTro.Tests;

[TestClass]
[DoNotParallelize]
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
        StringAssert.Contains(html, "id=\"demoAccountSelect\"", "Chọn tài khoản demo phải là dropdown");
        Assert.IsFalse(html.Contains("demo-role-link"), "Không được giữ dãy nút demo cũ");
        var js = File.ReadAllText(Path.Combine(FindWwwroot(), "auth", "auth.js"));
        StringAssert.Contains(js, "demoAccountSelect");
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
    public void ChuTro_HeaderAndSidebar_OmitDuplicateRoleAndTransportLabels()
    {
        var html = File.ReadAllText(Path.Combine(FindWwwroot(), "chutro", "index.html"));
        Assert.IsFalse(html.Contains("TCP 8888"));
        Assert.IsFalse(html.Contains("class=\"userbar-context\">Shell Chủ trọ"));
        Assert.IsFalse(html.Contains(">Trực tuyến</span>"));
        Assert.IsFalse(html.Contains("class=\"role-pill\">Chủ trọ"));
        StringAssert.Contains(html, "class=\"user-name\" id=\"uname\"");
        StringAssert.Contains(html, "Doanh thu gần nhất");
        Assert.IsFalse(html.Contains("độc quyền"));
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
    public void ChuTro_Shell_UsesGlassHeaderAndScrollablePane()
    {
        var css = File.ReadAllText(Path.Combine(FindWwwroot(), "chutro", "chutro.css"));
        StringAssert.Contains(css, ".shell-main > .userbar");
        StringAssert.Contains(css, ".shell-main > .legacy-panes");
        StringAssert.Contains(css, ".legacy-panes {");
        StringAssert.Contains(css, "--card: var(--glass-bg)");
        StringAssert.Contains(css, ".legacy-panes .card");
        StringAssert.Contains(css, ".legacy-panes .calc");
        StringAssert.Contains(css, ".legacy-panes .tbl");
        StringAssert.Contains(css, ".legacy-panes .tag");
        StringAssert.Contains(css, ".legacy-panes .btn-outline");
        StringAssert.Contains(css, ".legacy-panes .btn-danger");
        StringAssert.Contains(css, ".legacy-panes .inp");
        StringAssert.Contains(css, ".legacy-panes .fas::before");
        StringAssert.Contains(css, ".fa-plus::before");
        StringAssert.Contains(css, ".legacy-panes .content { overflow: visible; overscroll-behavior: auto;");
        StringAssert.Contains(css, "body:has(> .screen-view)");
        StringAssert.Contains(css, ".screen-view.active");
        StringAssert.Contains(css, ".screen-view.active > .app-shell");
        StringAssert.Contains(css, "height: 100vh");
        StringAssert.Contains(css, "overflow: hidden");
    }

    [TestMethod]
    public void ChuTro_Sidebar_DoesNotExposePlaceholderKhuTro()
    {
        var html = File.ReadAllText(Path.Combine(FindWwwroot(), "chutro", "index.html"));
        Assert.IsFalse(html.Contains("data-toast=\"Danh sách 3 khu trọ (Khu A, B, C)\""));
        Assert.IsFalse(html.Contains(">Khu trọ</span>"));
    }

    [TestMethod]
    public void ChuTro_Sidebar_UsesExistingTabPanes()
    {
        var html = File.ReadAllText(Path.Combine(FindWwwroot(), "chutro", "index.html"));
        foreach (var tab in new[] { "phong", "hop_dong", "utils", "hoa_don", "perms", "cai_dat" })
        {
            StringAssert.Contains(html, $"data-tab=\"{tab}\"", $"sidebar phải mở tab {tab}");
            StringAssert.Contains(html, $"id=\"tab-{tab}\"", $"thiếu pane tab-{tab}");
        }
    }

    [TestMethod]
    public void OtherRoleShells_UseGlassCompatibility()
    {
        var root = FindWwwroot();
        foreach (var shell in new[] { "congan", "khachthue" })
        {
            var css = File.ReadAllText(Path.Combine(root, shell, shell + ".css"));
            StringAssert.Contains(css, ".glass-role-compat .userbar");
            StringAssert.Contains(css, "var(--glass-bg)");
            StringAssert.Contains(css, ".glass-role-compat .fas::before");
            StringAssert.Contains(File.ReadAllText(Path.Combine(root, shell, "index.html")), "glass-role-compat");
        }
        StringAssert.Contains(File.ReadAllText(Path.Combine(root, "quanly", "index.html")), "quanly.css", "Quản lý shell phải liên kết quanly.css để không mất CSS");
        StringAssert.Contains(File.ReadAllText(Path.Combine(root, "shared", "css", "base.css")), ".glass-role-compat .legacy-panes .tbl");
    }

    [TestMethod]
    public void ChuTro_Sidebar_RevenueAndProposalItems_OpenVisiblePanes()
    {
        var html = File.ReadAllText(Path.Combine(FindWwwroot(), "chutro", "index.html"));
        Assert.IsFalse(html.Contains("data-scroll=\"revenueChartCard\""), "Thống kê phải mở tab-reports thay vì cuộn phần tử trong tab-dash");
        Assert.IsFalse(html.Contains("data-scroll=\"landlordProposalsCard\""), "Đề xuất phải là tab riêng, không cuộn trong tab-dash trộn lẫn KPI tổng quan");
        StringAssert.Contains(html, "data-tab=\"reports\"");
        StringAssert.Contains(html, "data-tab=\"de_xuat\"", "Đề xuất cần tab riêng, không dùng chung tab-dash với Tổng quan");
        StringAssert.Contains(html, "id=\"tab-de_xuat\"");
        var proposalInline = html.IndexOf("window.loadLandlordProposals = loadLandlordProposals", StringComparison.Ordinal);
        var mainScript = html.IndexOf("src=\"js/main.js", StringComparison.Ordinal);
        Assert.IsTrue(proposalInline >= 0 && mainScript > proposalInline, "IIFE phải expose loader trước khi tải main.js");
        StringAssert.Contains(html, "src=\"js/phong.js");
        StringAssert.Contains(html, "src=\"js/khach_thue.js");
        var main = File.ReadAllText(Path.Combine(FindWwwroot(), "chutro", "js", "main.js"));
        StringAssert.Contains(main, "reports: () => loadReports()");
        StringAssert.Contains(main, "de_xuat: () => window.loadLandlordProposals()");
        StringAssert.Contains(html, "window.loadLandlordProposals = loadLandlordProposals");
    }

    [TestMethod]
    public void ChuTro_ProposalPane_IsOutsideDashboardPane()
    {
        var html = File.ReadAllText(Path.Combine(FindWwwroot(), "chutro", "index.html"));
        var dashboardStart = html.IndexOf("<div id=\"tab-dash\">", StringComparison.Ordinal);
        var proposalsStart = html.IndexOf("<div id=\"tab-de_xuat\"", StringComparison.Ordinal);
        Assert.IsTrue(dashboardStart >= 0 && proposalsStart > dashboardStart, "thiếu các pane tổng quan hoặc đề xuất");
        var dashboardEnd = html[dashboardStart..proposalsStart];
        var opens = System.Text.RegularExpressions.Regex.Matches(dashboardEnd, "<div(?:\\s|>)").Count;
        var closes = System.Text.RegularExpressions.Regex.Matches(dashboardEnd, "</div>").Count;
        Assert.AreEqual(opens, closes, "tab-dash phải được đóng trước tab-de_xuat để ẩn tổng quan không ẩn đề xuất");
    }

    [TestMethod]
    public void ChuTro_IncidentSidebar_UsesWorkingIncidentPane()
    {
        var root = FindWwwroot();
        var html = File.ReadAllText(Path.Combine(root, "chutro", "index.html"));
        StringAssert.Contains(html, "data-tab=\"su_co\"");
        StringAssert.Contains(html, "id=\"tab-su_co\"");
        StringAssert.Contains(html, "id=\"incident-body\"");
        StringAssert.Contains(html, "js/su_co.js");
        Assert.IsFalse(html.Contains("data-toast=\"Quản lý sự cố toàn hệ thống\""));
        var main = File.ReadAllText(Path.Combine(root, "chutro", "js", "main.js"));
        StringAssert.Contains(main, "su_co: () => loadIncidents()");
        var js = File.ReadAllText(Path.Combine(root, "chutro", "js", "su_co.js"));
        StringAssert.Contains(js, "SU_CO_LAY_THEO_KHU");
        StringAssert.Contains(js, "SU_CO_CAP_NHAT_TRANG_THAI");
        StringAssert.Contains(js, "KHU_TRO_LAY_TAT_CA");
    }

    [TestMethod]
    public void ChuTro_AccountSidebar_UsesWorkingAccountPane()
    {
        var root = FindWwwroot();
        var html = File.ReadAllText(Path.Combine(root, "chutro", "index.html"));
        StringAssert.Contains(html, "data-tab=\"tai_khoan\"");
        StringAssert.Contains(html, "id=\"tab-tai_khoan\"");
        StringAssert.Contains(html, "js/tai_khoan.js");
        var main = File.ReadAllText(Path.Combine(root, "chutro", "js", "main.js"));
        StringAssert.Contains(main, "tai_khoan: () => loadAccounts()");
        var accounts = File.ReadAllText(Path.Combine(root, "chutro", "js", "tai_khoan.js"));
        StringAssert.Contains(accounts, "TAI_KHOAN_LAY_TAT_CA");
        StringAssert.Contains(accounts, "TAI_KHOAN_CAP_NHAT");
        StringAssert.Contains(accounts, "TAI_KHOAN_DAT_LAI_MAT_KHAU");
        Assert.IsFalse(html.Contains("data-toast=\"Quản lý tài khoản"));
    }

    [TestMethod]
    public void ChuTro_Permissions_MatchesServerMatrixContract()
    {
        var js = File.ReadAllText(Path.Combine(FindWwwroot(), "chutro", "js", "perms.js"));
        StringAssert.Contains(js, "macDinh");
        StringAssert.Contains(js, "hieuLuc");
        StringAssert.Contains(js, "const currentRole = _currentRole;");
        StringAssert.Contains(js, "thuHoi");
        StringAssert.Contains(js, "capQuyen");
        foreach (var role in new[] { "ChuTro", "QuanLy", "LeTan", "NhanVien", "KhachThue", "CongAn" })
            StringAssert.Contains(js, role + ":", $"Ma trận quyền phải liệt kê đủ 6 vai trò, thiếu {role}");
    }

    [TestMethod]
    public void SharedChrome_LogoutAndTabNavigation_AreWired()
    {
        var root = FindWwwroot();
        var chrome = File.ReadAllText(Path.Combine(root, "shared", "js", "chrome.js"));
        StringAssert.Contains(chrome, "../auth/index.html");
        StringAssert.Contains(chrome, "data-tab");
        StringAssert.Contains(chrome, "loadLandlordTab");
        var main = File.ReadAllText(Path.Combine(root, "chutro", "js", "main.js"));
        StringAssert.Contains(main, "loadLandlordTab('dash')");
    }

    [TestMethod]
    public void ManagerScreens_KeepSidebarNavigation()
    {
        var html = File.ReadAllText(Path.Combine(FindWwwroot(), "quanly", "index.html"));
        foreach (var screen in new[] { "s03", "s04", "s05" })
        {
            var start = html.IndexOf($"<section id=\"{screen}\"", StringComparison.Ordinal);
            var end = html.IndexOf("</section>", start, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0 && end > start, $"thiếu màn {screen}");
            StringAssert.Contains(html[start..end], "class=\"sidebar\"");
            StringAssert.Contains(html[start..end], "data-goto=\"s03\"");
        }
    }

    [TestMethod]
    public void ManagerRole_UsesLiveServerDataNotHardcoded()
    {
        var html = File.ReadAllText(Path.Combine(FindWwwroot(), "quanly", "index.html"));
        Assert.IsFalse(html.Contains("P101"), "bảng mẫu P101 không còn dùng");
        Assert.IsFalse(html.Contains("P102"));
        Assert.IsFalse(html.Contains("Trần Văn B"));
        Assert.IsFalse(html.Contains("Ngô Thị E"));
        Assert.IsFalse(html.Contains("0482 0301 2XXX"));
        StringAssert.Contains(html, "oninput=\"renderManagerRooms()\"");
        StringAssert.Contains(html, "oninput=\"renderManagerTenants()\"");
        Assert.IsFalse(html.Contains("onclick=\"addRoom()\""), "nút Thêm phòng phải nhắc về Chủ trọ");
        Assert.IsFalse(html.Contains("onclick=\"addTenant()\""), "nút Thêm khách phải nhắc về Chủ trọ");
        var main = File.ReadAllText(Path.Combine(FindWwwroot(), "quanly", "main.js"));
        StringAssert.Contains(main, "renderManagerRooms");
        StringAssert.Contains(main, "renderManagerTenants");
        StringAssert.Contains(main, "PHONG_LAY_TAT_CA");
        StringAssert.Contains(main, "KHACH_THUE_LAY_THEO_PHONG");
        StringAssert.Contains(main, "PHONG_CAP_NHAT");
        StringAssert.Contains(main, "TAI_KHOAN_DAT_LAI_MAT_KHAU");
    }

    [TestMethod]
    public void PoliceSidebar_SearchItem_OpensCitizensPane()
    {
        var html = File.ReadAllText(Path.Combine(FindWwwroot(), "congan", "index.html"));
        var search = System.Text.RegularExpressions.Regex.Match(
            html, "<button[^>]*data-police-tab=\"citizens\"[^>]*>\\s*<span>Tra cứu công dân</span>");
        Assert.IsTrue(search.Success, "mục Tra cứu công dân phải có data-police-tab=\"citizens\" để mở pane");
    }

    [TestMethod]
    public void PoliceShell_SearchFormAndResultTable_AreWired()
    {
        var root = FindWwwroot();
        var html = File.ReadAllText(Path.Combine(root, "congan", "index.html"));
        Assert.IsFalse(html.Contains("class=\"role-pill\""));
        var js = File.ReadAllText(Path.Combine(root, "congan", "js", "main.js"));
        StringAssert.Contains(js, "policeSearchForm");
        StringAssert.Contains(js, "policeTableBody");
        StringAssert.Contains(js, "btnPoliceExportExcel");
        StringAssert.Contains(js, "XUAT_HO_SO_TAM_TRU");
        StringAssert.Contains(js, "function renderPoliceResults");
        Assert.IsFalse(html.Contains("class=\"kpi warn\""), "không để lại KPI legacy nền đen cạnh giao diện tra cứu mới");
        StringAssert.Contains(html, "class=\"police-kpi-grid\"");
        Assert.IsFalse(html.Contains("<div class=\"legacy-panes\""), "shell Công an phải có duy nhất một vùng nội dung");
        var css = File.ReadAllText(Path.Combine(root, "congan", "congan.css"));
        StringAssert.Contains(css, ".glass-role-compat .police-content .tbl");
        StringAssert.Contains(css, ".glass-role-compat .police-content .toolbar");
        Assert.IsFalse(css.Contains(".glass-role-compat .legacy-panes .tbl"),
            "Công an không còn .legacy-panes nên style bảng phải bám .police-content");
    }

    [TestMethod]
    public void TenantShell_ActionButtons_AreWired()
    {
        var root = FindWwwroot();
        var html = File.ReadAllText(Path.Combine(root, "khachthue", "index.html"));
        var js = File.ReadAllText(Path.Combine(root, "khachthue", "js", "khach_thue.js"));
        Assert.IsFalse(html.Contains("BR-19 QR/PIN"));
        Assert.IsFalse(html.Contains("data-tenant-tab=\"current\""));
        StringAssert.Contains(html, "id=\"btnShowCheckin\"");
        StringAssert.Contains(js, "btnShowCheckin");
        StringAssert.Contains(js, "querySelectorAll('#tenantTabs [data-tab]')");
    }

    [TestMethod]
    public void PoliceShell_HasLogoutControl()
    {
        var html = File.ReadAllText(Path.Combine(FindWwwroot(), "congan", "index.html"));
        StringAssert.Contains(html, "aria-label=\"Đăng xuất\"");
        StringAssert.Contains(html, "data-goto=\"s01\"");
    }

    [TestMethod]
    public void WebViewBridge_ErrorResponse_MatchesPendingRequest()
    {
        var host = File.ReadAllText(Path.Combine(FindWwwroot(), "..", "..", "QuanLyTro.Client", "Form1.cs"));
        StringAssert.Contains(host, "requestId, Success = false");
    }

    [TestMethod]
    public void LeTanAndNhanVien_ExposeScopedIncidentWorkflows()
    {
        var root = FindWwwroot();
        var leTanHtml = File.ReadAllText(Path.Combine(root, "letan", "index.html"));
        var staffHtml = File.ReadAllText(Path.Combine(root, "nhanvien", "index.html"));
        foreach (var html in new[] { leTanHtml, staffHtml })
        {
            Assert.IsFalse(html.Contains("TCP 8888"));
            Assert.IsFalse(html.Contains("class=\"role-pill\""));
            Assert.IsFalse(html.Contains("<script src=\"main.js?v=3\""));
            StringAssert.Contains(html, "data-tab=\"incidents\"");
            StringAssert.Contains(html, "id=\"incident-body\"");
        }
        StringAssert.Contains(leTanHtml, "data-tab=\"schedule\"");
        StringAssert.Contains(staffHtml, "data-tab=\"tasks\"");
        var leTanJs = File.ReadAllText(Path.Combine(root, "letan", "main.js"));
        var staffJs = File.ReadAllText(Path.Combine(root, "nhanvien", "main.js"));
        StringAssert.Contains(leTanJs, "SU_CO_LAY_THEO_KHU");
        StringAssert.Contains(staffJs, "SU_CO_TAO");
        StringAssert.Contains(leTanJs, "document.querySelectorAll('.nav-btn[data-tab]')");
        StringAssert.Contains(staffJs, "document.querySelectorAll('.nav-btn[data-tab]')");
        StringAssert.Contains(leTanJs, "b.addEventListener('click', () => loadLeTanTab(b.dataset.tab))");
        StringAssert.Contains(staffJs, "b.addEventListener('click', () => loadNhanVienTab(b.dataset.tab))");
    }

    [TestMethod]
    public void LeTanAndNhanVien_RefreshTheirActiveQueues()
    {
        var root = FindWwwroot();
        var letAn = File.ReadAllText(Path.Combine(root, "letan", "main.js"));
        var nhanVien = File.ReadAllText(Path.Combine(root, "nhanvien", "main.js"));
        StringAssert.Contains(letAn, "startAutoRefresh(() => loadLeTanTab(_activeTab)");
        StringAssert.Contains(nhanVien, "startAutoRefresh(() => loadNhanVienTab(_activeTab)");
    }

    [TestMethod]
    public void SharedModalHelpers_CreateRootWithoutPriorToast()
    {
        var ui = File.ReadAllText(Path.Combine(FindWwwroot(), "shared", "js", "ui.js"));
        StringAssert.Contains(ui, "function getModalRoot()");
        StringAssert.Contains(ui, "const root = getModalRoot();");
        StringAssert.Contains(ui, "function openModal({ title, fields, onSubmit })");
        StringAssert.Contains(ui, "function confirmDialog(msg, title, kind)");
    }

    [TestMethod]
    public void ChuTro_ModalHelpers_HaveDarkThemeStyles()
    {
        var shared = File.ReadAllText(Path.Combine(FindWwwroot(), "shared", "css", "base.css"));
        StringAssert.Contains(shared, "#modal-root .modal-back");
        StringAssert.Contains(shared, "#modal-root .dlg-body");
        StringAssert.Contains(shared, "#modal-root > .toast.err");
    }

    [TestMethod]
    public void Owner_RoomActions_UseServerPayloadFields()
    {
        var root = FindWwwroot();
        var rooms = File.ReadAllText(Path.Combine(root, "chutro", "js", "phong.js"));
        StringAssert.Contains(rooms, "phongId: r.id");
        StringAssert.Contains(rooms, "KHU_TRO_LAY_TAT_CA");
        StringAssert.Contains(rooms, "khuTroId:");
        StringAssert.Contains(rooms, "phongId: r.id");
        StringAssert.Contains(rooms, "window.bridge.call('PHONG_CAP_NHAT'");
        StringAssert.Contains(rooms, "soPhong: v.soPhong");
    }

    [TestMethod]
    public void Owner_TenantMutations_UseSupportedActionsAndIds()
    {
        var tenants = File.ReadAllText(Path.Combine(FindWwwroot(), "chutro", "js", "khach_thue.js"));
        StringAssert.Contains(tenants, "'KHACH_THUE_CHUYEN_PHONG'");
        StringAssert.Contains(tenants, "'KHACH_THUE_THEM', payload");
        StringAssert.Contains(tenants, "r.trangThai !== 'BaoTri'");
        StringAssert.Contains(tenants, "taiKhoanId: t.id");
        StringAssert.Contains(tenants, "'TAI_KHOAN_DAT_LAI_MAT_KHAU'");
        StringAssert.Contains(tenants, "'KHACH_THUE_XOA', { taiKhoanId: t.id }");
        StringAssert.Contains(tenants, "'KHACH_THUE_TRA_PHONG', { taiKhoanId: t.id }");
        StringAssert.Contains(tenants, "'KHACH_THUE_TIM_KIEM', { keyword: q }");
        StringAssert.Contains(tenants, "tenDangNhap: v.tenDangNhap");
        StringAssert.Contains(tenants, "replace(/\\D/g, '')");
        StringAssert.Contains(tenants, "taiKhoanId: t.id");
        StringAssert.Contains(tenants, "matKhauMoi: pwd");
        StringAssert.Contains(tenants, "if (pwd && pwd.length < 6)");
        StringAssert.Contains(tenants, "fetchTenantProfile");
        StringAssert.Contains(tenants, "async function editTenant(id)");
        StringAssert.Contains(tenants, "KHACH_THUE_CAP_NHAT', { taiKhoanId: t.id");
        StringAssert.Contains(tenants, "'TAI_KHOAN_DAT_LAI_MAT_KHAU'");
        Assert.IsFalse(tenants.Contains("{ tenant: tenantPayload("));
    }

    [TestMethod]
    public void Owner_SettingsAndPermissions_MapServerFieldNames()
    {
        var root = FindWwwroot();
        var chrome = File.ReadAllText(Path.Combine(root, "shared", "js", "chrome.js"));
        StringAssert.Contains(chrome, "ThuHoi: 'thuHoi'");
        StringAssert.Contains(chrome, "CapQuyen: 'capQuyen'");
        var settings = File.ReadAllText(Path.Combine(root, "chutro", "js", "cai_dat.js"));
        StringAssert.Contains(settings, "tenNganHang:");
        StringAssert.Contains(settings, "mauQr:");
        StringAssert.Contains(settings, "_cauHinh.tenNganHang");
        StringAssert.Contains(settings, "_cauHinh.mauQr");
        StringAssert.Contains(settings, "tenNganHang: form.nganHang");
        StringAssert.Contains(settings, "mauQr: form.templateQr");
        StringAssert.Contains(settings, "apiToken");
        StringAssert.Contains(chrome, "ThuHoi: 'thuHoi'");
        StringAssert.Contains(chrome, "CapQuyen: 'capQuyen'");
    }

    [TestMethod]
    public void KhachThue_CheckinModal_ButtonsAreWired()
    {
        var root = FindWwwroot();
        var html = File.ReadAllText(Path.Combine(root, "khachthue", "index.html"));
        var js = File.ReadAllText(Path.Combine(root, "khachthue", "js", "khach_thue.js"));
        StringAssert.Contains(html, "id=\"btnTenantCheckinSubmit\"");
        StringAssert.Contains(html, "id=\"btnBackToOverview\"");
        StringAssert.Contains(js, "btnTenantCheckinSubmit");
        StringAssert.Contains(js, "btnBackToOverview");
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
