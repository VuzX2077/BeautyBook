namespace BeautyBookBackend.Services;

public sealed class PlayReviewOptions
{
    public bool SimulationEnabled { get; set; }
    public Guid ReviewUserId { get; set; }
    public Guid CounterpartUserId { get; set; }
    public Guid SampleBankAccountId { get; set; }
}
