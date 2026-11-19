using MySqlConnector;
using QuanLyTro.Server.Data;

namespace QuanLyTro.Tests.TestHelpers;

public static class TestDb
{
    public const string TestDatabaseName = "quanly_nhatro_test";

    public static string ConnectionString =>
        $"Server=127.0.0.1;Port=3306;Database={TestDatabaseName};Uid=root;CharSet=utf8mb4;AllowUserVariables=True;";

    public static Database Create() => new(ConnectionString);

    /// <summary>Xóa và tạo lại DB test. Chỉ được gọi trên DB test.</summary>
    public static async Task ResetAsync()
    {
        await using var conn = new MySqlConnection(
            $"Server=127.0.0.1;Port=3306;Uid=root;CharSet=utf8mb4;");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS {TestDatabaseName}; CREATE DATABASE {TestDatabaseName} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Chặn cứng: connection string phải trỏ đúng DB test, nếu không throw.</summary>
    public static Task EnsureTestDatabaseOnly(string cs)
    {
        if (!cs.Contains(TestDatabaseName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Connection string phải trỏ {TestDatabaseName}, không được dùng DB thật.");
        return Task.CompletedTask;
    }
}
