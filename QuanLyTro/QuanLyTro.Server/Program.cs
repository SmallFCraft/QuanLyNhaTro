using QuanLyTro.Server.Config;

namespace QuanLyTro.Server;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var opts = ServerOptions.FromEnvironment();
        Console.WriteLine($"QuanLyTro Server - TCP {opts.TcpPort} | packet {opts.MaxPacketBytes} bytes");
        // Wire-up thật được lắp ở Task 4-6 (TcpServer + Router + Services).
        Console.WriteLine("Server started. Nhan Ctrl+C de tat.");
        await Task.Delay(Timeout.Infinite);
    }
}
