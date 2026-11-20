using MySqlConnector;

namespace QuanLyTro.Server.Data;

/// <summary>
/// Truy cập MySQL parameterized. Mọi query đi qua đây để bảo đảm không ghép chuỗi SQL.
/// </summary>
public sealed class Database
{
    private readonly string _connectionString;

    public Database(string connectionString) => _connectionString = connectionString;

    private MySqlConnection Open()
    {
        var conn = new MySqlConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public async Task<int> ExecuteAsync(string sql, object? param = null)
    {
        await using var conn = Open();
        await using var cmd = Bind(conn.CreateCommand(), sql, param);
        var affected = await cmd.ExecuteNonQueryAsync();
        // INSERT trả về last insert id; UPDATE/DELETE trả số dòng affected
        return cmd.CommandText.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
            ? (int)cmd.LastInsertedId
            : affected;
    }

    public async Task<object?> ScalarAsync(string sql, object? param = null)
    {
        await using var conn = Open();
        await using var cmd = Bind(conn.CreateCommand(), sql, param);
        return await cmd.ExecuteScalarAsync();
    }

    public async Task<T?> ScalarT<T>(string sql, object? param = null)
    {
        var raw = await ScalarAsync(sql, param);
        if (raw is null) return default;
        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(raw, targetType);
    }

    public async Task<List<Dictionary<string, object?>>> QueryAsync(string sql, object? param = null)
    {
        await using var conn = Open();
        await using var cmd = Bind(conn.CreateCommand(), sql, param);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    public async Task<(MySqlConnection Conn, MySqlTransaction Tx)> BeginAsync()
    {
        var conn = Open();
        var tx = await conn.BeginTransactionAsync();
        return (conn, tx);
    }

    private static MySqlCommand Bind(MySqlCommand cmd, string sql, object? param)
    {
        cmd.CommandText = sql;
        if (param is null) return cmd;
        foreach (var prop in param.GetType().GetProperties())
            cmd.Parameters.AddWithValue("@" + prop.Name, prop.GetValue(param) ?? DBNull.Value);
        return cmd;
    }
}
