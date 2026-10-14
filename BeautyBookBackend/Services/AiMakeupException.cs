namespace BeautyBookBackend.Services;

public sealed class AiMakeupException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
