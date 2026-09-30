namespace BeautyBookBackend.Models;
public class MuaFollow
{
    public Guid UserId { get; set; }
    public Guid MuaId { get; set; }
    public DateTime CreatedAt { get; set; }
    public User User { get; set; } = null!;
    public MakeupArtistProfile Mua { get; set; } = null!;
}
