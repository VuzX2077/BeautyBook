using System.ComponentModel.DataAnnotations;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using BeautyBookBackend.Services;

namespace BeautyBookBackend.Tests;

public class NotificationSafetyTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only;Password=model_only").Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public void AdminNotificationController_IsRestrictedToAdminRole()
    {
        var authorize = Assert.Single(typeof(AdminNotificationController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("Admin", authorize.Roles);
    }

    [Fact]
    public void CampaignIdempotencyKey_HasUniqueIndex()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(NotificationCampaign));
        var index = Assert.Single(entity!.GetIndexes(), x => x.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(NotificationCampaign.IdempotencyKey) }));
        Assert.True(index.IsUnique);
    }

    [Fact]
    public void CampaignLinkOnExistingNotification_IsOptional()
    {
        using var context = CreateContext();
        var property = context.Model.FindEntityType(typeof(AppNotification))!.FindProperty(nameof(AppNotification.CampaignId));
        Assert.True(property!.IsNullable);
    }

    [Fact]
    public void ChatNotification_HasUniqueMessageLinkAndUsesChatChannel()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(AppNotification))!;
        var index = Assert.Single(entity.GetIndexes(), x => x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { nameof(AppNotification.MessageId) }));
        Assert.True(index.IsUnique);
        Assert.Equal("chat", NotificationChannelResolver.Resolve("CHAT_MESSAGE"));
    }

    [Fact]
    public void RefundNotification_ExposesStablePublicTypeAndUsesPaymentChannel()
    {
        var storedType = "REFUND_STATUS_CHANGED_PROCESSING_0123456789ABCDEF";

        Assert.Equal("REFUND_STATUS_CHANGED", BookingNotificationService.PublicType(storedType));
        Assert.Equal("payments", NotificationChannelResolver.Resolve(storedType));
    }

    [Theory]
    [InlineData("", "Nội dung")]
    [InlineData("Tiêu đề", "")]
    public void CreateRequest_RequiresTitleAndBody(string title, string body)
    {
        var request = new CreateAdminNotificationRequest { Title = title, Body = body, Audience = "All", IdempotencyKey = Guid.NewGuid() };
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), results, true));
    }
}
