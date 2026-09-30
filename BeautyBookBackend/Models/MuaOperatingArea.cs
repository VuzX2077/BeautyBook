namespace BeautyBookBackend.Models;

public class MuaOperatingArea
{
    public Guid MuaId { get; set; }
    public string AreaId { get; set; } = string.Empty;
    public MakeupArtistProfile Profile { get; set; } = null!;
}
