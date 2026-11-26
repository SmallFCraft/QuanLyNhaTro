using Microsoft.Web.WebView2.Core;
using QuanLyTro.Client.Network;
using QuanLyTro.Shared.Protocol;
using System.Text;
using System.Text.Json;

namespace QuanLyTro.Client;

public sealed partial class Form1 : Form
{
    private readonly TcpClientTransport _transport = new();
    private Microsoft.Web.WebView2.WinForms.WebView2 _web = null!;
    private string? _token;
    private string? _vaiTro;

    public Form1()
    {
        Text = "Quản lý nhà trọ — Ngũ Hành Sơn";
        Width = 1280;
        Height = 800;
        StartPosition = FormStartPosition.CenterScreen;

        _web = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_web);
        Load += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuanLyTro", "WebView2");
        Directory.CreateDirectory(userData);
        await _web.EnsureCoreWebView2Async();

        var wwwroot = FindWwwroot();
        _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "app.quanlytro.local", wwwroot, CoreWebView2HostResourceAccessKind.Allow);
        _web.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);

        // Chặn điều hướng ngoài origin nội bộ
        _web.CoreWebView2.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith("https://app.quanlytro.local", StringComparison.OrdinalIgnoreCase))
                e.Cancel = true;
        };
        _web.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;

        _web.CoreWebView2.WebMessageReceived += OnWebMessage;
        _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _web.CoreWebView2.Settings.AreDevToolsEnabled = false;

        _web.CoreWebView2.Navigate("https://app.quanlytro.local/datlich.html");
    }

    private static string FindWwwroot()
    {
        // chạy từ bin/Debug/net8.0-windows → đi lên 4 cấp tới repo/QuanLyTro
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.GetFullPath(Path.Combine(dir, "Assets", "wwwroot"));
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }
        throw new DirectoryNotFoundException("Không tìm thấy Assets/wwwroot");
    }

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var requestId = "";
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            requestId = doc.RootElement.GetProperty("requestId").GetString()!;
            var action = doc.RootElement.GetProperty("action").GetString()!;
            JsonElement data = doc.RootElement.TryGetProperty("data", out var d)
                ? d.Clone() : JsonSerializer.Deserialize<JsonElement>("{}");

            // Gói tin tới server và trả response về đúng requestId
            var resp = await _transport.SendAsync(action, _token, JsonSerializer.Deserialize<object>(data.GetRawText()));

            var payload = JsonSerializer.Serialize(new
            {
                requestId,
                resp.Success,
                resp.Message,
                Data = resp.Data is null
                    ? (JsonElement?)null
                    : JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(resp.Data, JsonDefaults.Options)),
                vaiTro = _vaiTro,
            }, JsonDefaults.Options);
            _web.CoreWebView2.PostWebMessageAsJson(payload);

            // Đăng nhập thành công → chuyển shell; đăng xuất → về login
            if (action == "DANG_NHAP" && resp.Success)
            {
                using var ok = JsonDocument.Parse(JsonSerializer.Serialize(resp.Data!, JsonDefaults.Options));
                _token = ok.RootElement.GetProperty("token").GetString();
                _vaiTro = ok.RootElement.GetProperty("vaiTro").GetString();
                // JS (auth.js/ui.js) tu dieu huong kem ?u=; Navigate o day se tao loat nap doi ERR_FILE_NOT_FOUND.
            }
            else if (action == "DANG_XUAT" && resp.Success)
            {
                _token = null;
                _vaiTro = null;
            }
        }
        catch (Exception ex)
        {
            _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
            { requestId, Success = false, Message = "Lỗi bridge: " + ex.Message, Data = (object?)null },
            JsonDefaults.Options));
        }
    }

    private static string ShellFolder(string vaiTro) => vaiTro switch
    {
        "ChuTro" => "chutro",
        "QuanLy" => "quanly",
        "LeTan" => "letan",
        "NhanVien" => "nhanvien",
        "KhachThue" => "khachthue",
        "CongAn" => "congan",
        _ => throw new ArgumentOutOfRangeException(nameof(vaiTro)),
    };
}
