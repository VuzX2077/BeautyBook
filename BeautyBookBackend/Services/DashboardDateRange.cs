namespace BeautyBookBackend.Services;

public static class DashboardDateRange
{
    public static bool TryCreate(DateOnly from, DateOnly to, out DateTime start, out DateTime end, out DateTime previousStart)
    {
        start = end = previousStart = default;
        if (from.Year < 1970 || to < from || to.DayNumber - from.DayNumber > 365 || to == DateOnly.MaxValue) return false;
        start = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue).AddHours(-7), DateTimeKind.Utc);
        end = DateTime.SpecifyKind(to.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-7), DateTimeKind.Utc);
        previousStart = start.Subtract(end - start);
        return true;
    }
}
