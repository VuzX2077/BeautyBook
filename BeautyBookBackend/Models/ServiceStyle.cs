namespace BeautyBookBackend.Models;

public sealed class ServiceStyle
{
    public Guid ServiceId { get; set; }
    public int StyleId { get; set; }
    public Service Service { get; set; } = null!;
    public MakeupStyle MakeupStyle { get; set; } = null!;
}
