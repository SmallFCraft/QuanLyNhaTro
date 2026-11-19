namespace QuanLyTro.Shared.Protocol;

public sealed record ResponsePacket(string RequestId, bool Success, string Message, object? Data)
{
    public static ResponsePacket Ok(string requestId, object? data = null, string message = "") =>
        new(requestId, true, message, data);

    public static ResponsePacket Fail(string requestId, string message) =>
        new(requestId, false, message, null);
}
