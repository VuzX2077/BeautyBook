namespace BeautyBookBackend.Services;

public sealed class AiMakeupConcurrency
{
    public SemaphoreSlim Gate { get; } = new(2, 2);
}
