using BeautyBookBackend.Controllers;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;

namespace BeautyBookBackend.Tests;

public sealed class DashboardDateRangeTests
{
    [Fact]
    public void VietnamMonthUsesUtcBoundariesAndEqualPreviousPeriod()
    {
        Assert.True(DashboardDateRange.TryCreate(new(2026, 10, 1), new(2026, 10, 31), out var start, out var end, out var previous));
        Assert.Equal(new DateTime(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc), end);
        Assert.Equal(end - start, start - previous);
        Assert.Equal(DateTimeKind.Utc, previous.Kind);
    }
    [Fact]
    public void SingleDayAndLeapDayAreIncluded()
    {
        Assert.True(DashboardDateRange.TryCreate(new(2024, 2, 29), new(2024, 2, 29), out var start, out var end, out _));
        Assert.Equal(TimeSpan.FromDays(1), end - start);
        Assert.True(DashboardDateRange.TryCreate(new(2024, 1, 1), new(2024, 12, 31), out _, out _, out _));
    }
    [Fact]
    public void InvalidAndOverflowingRangesAreRejected()
    {
        Assert.False(DashboardDateRange.TryCreate(default, default, out _, out _, out _));
        Assert.False(DashboardDateRange.TryCreate(new(2026, 10, 2), new(2026, 10, 1), out _, out _, out _));
        Assert.False(DashboardDateRange.TryCreate(new(2024, 1, 1), new(2025, 1, 1), out _, out _, out _));
        Assert.False(DashboardDateRange.TryCreate(DateOnly.MaxValue, DateOnly.MaxValue, out _, out _, out _));
    }
    [Fact]
    public void EndpointRequiresAdmin()
    {
        var auth = Assert.Single(typeof(AdminDashboardController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(nameof(UserRole.Admin), auth.Roles);
    }
}
