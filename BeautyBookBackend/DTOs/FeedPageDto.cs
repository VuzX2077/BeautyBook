namespace BeautyBookBackend.DTOs;
public class FeedPageDto
{
    public List<FeedItemDto> Items { get; set; } = new();
    public string? NextCursor { get; set; }
}
