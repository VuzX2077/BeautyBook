namespace BeautyBookBackend.Repositories;

public interface IAiMakeupQuotaRepository
{
    Task ReserveAsync(Guid userId, int userLimit, int globalLimit, CancellationToken ct);
}
