using System.ComponentModel.DataAnnotations;

namespace BeautyBookBackend.DTOs;

public sealed class ExploreQuery : IValidatableObject
{
    [RegularExpression("^(portfolio|artists|services)$")] public string Kind { get; set; } = "portfolio";
    [StringLength(100)] public string? Q { get; set; }
    [Range(1, 100)] public int? ProvinceCode { get; set; }
    [Range(1, int.MaxValue)] public int? StyleId { get; set; }
    [Range(typeof(decimal), "0", "1000000000")] public decimal? MinPrice { get; set; }
    [Range(typeof(decimal), "0", "1000000000")] public decimal? MaxPrice { get; set; }
    [Range(1, 24)] public int Limit { get; set; } = 12;
    [StringLength(3000)] public string? Cursor { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinPrice.HasValue && MaxPrice.HasValue && MinPrice > MaxPrice)
            yield return new("Giá tối thiểu phải nhỏ hơn hoặc bằng giá tối đa.", new[] { nameof(MinPrice), nameof(MaxPrice) });
        if (ProvinceCode.HasValue && OperatingAreas.Province(ProvinceCode.Value) == null)
            yield return new("Tỉnh/thành phố không hợp lệ.", new[] { nameof(ProvinceCode) });
    }
}

public sealed record ExplorePage<T>(IReadOnlyList<T> Items, string? NextCursor);
public sealed record ExploreStyle(int Id, string Name, int ArtistCount);
public sealed record ExploreProvince(int Code, string Name);
public sealed record ExploreHome(IReadOnlyList<ExploreStyle> Styles, IReadOnlyList<ExploreProvince> Provinces,
    IReadOnlyList<ExplorePost> FeaturedPosts, IReadOnlyList<ExploreArtist> FeaturedArtists,
    IReadOnlyList<ExploreServiceItem> FeaturedServices);

public sealed class ExplorePost
{
    public Guid Id { get; set; }
    public Guid MuaId { get; set; }
    public string Title { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string? AuthorAvatar { get; set; }
    public string? City { get; set; }
    public List<string> ImageUrls { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public int LikesCount { get; set; }
    public int SavesCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
public sealed class ExploreArtist
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string? City { get; set; }
    public decimal Rating { get; set; }
    public int ReviewCount { get; set; }
    public decimal? MinPrice { get; set; }
    public List<string> Styles { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore] public double Score { get; set; }
}
public sealed class ExploreServiceItem
{
    public Guid Id { get; set; }
    public Guid MuaId { get; set; }
    public string Name { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string? City { get; set; }
    public decimal Price { get; set; }
    public int DurationMinutes { get; set; }
    public string? ImageUrl { get; set; }
    public List<string> ImageUrls { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}
