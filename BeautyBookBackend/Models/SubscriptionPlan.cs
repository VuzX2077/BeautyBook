namespace BeautyBookBackend.Models;

public sealed class SubscriptionPlan
{
    public string Id { get; set; } = "";
    public string Role { get; set; } = "";
    public string Name { get; set; } = "";
    public int AmountVnd { get; set; }
    public int? Days { get; set; }
    public string Description { get; set; } = "";
    public string State { get; set; } = "Draft";
    public int Version { get; set; } = 1;
}

public sealed class SubscriptionPlanChange
{
    public Guid Id { get; set; }
    public string PlanId { get; set; } = "";
    public Guid ActorId { get; set; }
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public string BeforeJson { get; set; } = "";
    public string AfterJson { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
