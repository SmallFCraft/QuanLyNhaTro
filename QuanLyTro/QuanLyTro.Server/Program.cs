using QuanLyTro.Server.Config;
using QuanLyTro.Server.Data;
using QuanLyTro.Server.Network;
using QuanLyTro.Server.Repositories;
using QuanLyTro.Server.Security;
using QuanLyTro.Server.Services;
using QuanLyTro.Shared.Protocol;

namespace QuanLyTro.Server;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var opts = ServerOptions.FromEnvironment();
        Console.WriteLine($"QuanLyTro Server — TCP {opts.TcpPort} | packet {opts.MaxPacketBytes} bytes");

        var db = new Database(opts.ConnectionString);
        await EnsureDatabaseExistsAsync(opts.ConnectionString);
        Console.WriteLine("Khoi tao schema...");
        await new SchemaInitializer(db).InitializeAsync();
        Console.WriteLine("Schema OK.");

        var sessions = new SessionStore();
        var siteRepo = new SiteRepository(db);
        var roomRepo = new RoomRepository(db);
        var tenantRepo = new TenantRepository(db);
        var router = new RequestRouter(db, sessions, siteRepo, roomRepo, tenantRepo);
        await SeedDemoAsync(db);

        var server = new TcpServer(opts, router.HandleAsync);
        await server.StartAsync(CancellationToken.None);
        Console.WriteLine($"Server dang lang nghe tai cong {opts.TcpPort}. Nhan Ctrl+C de tat.");

        var exit = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; exit.TrySetResult(); };
        await exit.Task;
        await server.StopAsync();
    }

    /// <summary>Tạo database nếu chưa có (dev bootstrap); không dùng cho DB test.</summary>
    private static async Task EnsureDatabaseExistsAsync(string connectionString)
    {
        var builder = new MySqlConnector.MySqlConnectionStringBuilder(connectionString);
        var dbName = builder.Database;
        builder.Database = "";
        await using var conn = new MySqlConnector.MySqlConnection(builder.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE IF NOT EXISTS `{dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Gieo tài khoản demo khi DB dev rỗng (chỉ chạy lần đầu).</summary>
    private static async Task SeedDemoAsync(Database db)
    {
        var count = await db.ScalarT<int>("SELECT COUNT(*) FROM tai_khoan");
        if (count > 0) return;
        Console.WriteLine("DB trong — gieo tai khoan demo...");
        foreach (var (user, pass, role) in new[]
        {
            ("chutro", "chutro", "ChuTro"), ("quanly", "quanly", "QuanLy"),
            ("letan", "letan", "LeTan"), ("nhanvien", "nhanvien", "NhanVien"),
            ("congan", "congan", "CongAn"),
        })
        {
            await db.ExecuteAsync(
                "INSERT INTO tai_khoan (ten_dang_nhap, mat_khau_hash, ho_ten, vai_tro) VALUES (@u, @h, @n, @v)",
                new { u = user, h = PasswordHasher.Hash(pass), n = user, v = role });
        }
        Console.WriteLine("Seed demo OK: chutro|chutro, quanly|quanly, letan|letan, nhanvien|nhanvien, congan|congan");
    }
}
