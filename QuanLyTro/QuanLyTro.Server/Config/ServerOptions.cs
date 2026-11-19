namespace QuanLyTro.Server.Config;

public sealed class ServerOptions
{
    public int TcpPort { get; init; } = 8888;
    public int MaxPacketBytes { get; init; } = 262144;
    public string ConnectionString { get; init; } =
        "Server=localhost;Port=3306;Database=quanly_nhatro;Uid=root;CharSet=utf8mb4;";

    public static ServerOptions FromEnvironment() =>
        new()
        {
            TcpPort = int.TryParse(Environment.GetEnvironmentVariable("QUANLYTRO_PORT"), out var p) ? p : 8888,
            MaxPacketBytes = int.TryParse(Environment.GetEnvironmentVariable("QUANLYTRO_MAX_PACKET"), out var m) && m is >= 1024 and <= 3145728 ? m : 262144,
            ConnectionString = Environment.GetEnvironmentVariable("QUANLYTRO_CONN") ??
                "Server=localhost;Port=3306;Database=quanly_nhatro;Uid=root;CharSet=utf8mb4;",
        };
}
