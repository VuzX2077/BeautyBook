using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services;

public interface IAiMakeupCatalogService
{
    Task<IReadOnlyList<ExploreStyle>> StylesAsync(CancellationToken ct);
}
