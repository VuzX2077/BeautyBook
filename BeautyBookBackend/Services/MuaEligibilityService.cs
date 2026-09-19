using System.Net.Mail;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services
{
    public sealed class MuaEligibilityService : IMuaEligibilityService
    {
        private readonly ApplicationDbContext _db;
        private readonly IMuaScheduleService _scheduleService;

        public MuaEligibilityService(ApplicationDbContext db, IMuaScheduleService scheduleService)
        {
            _db = db;
            _scheduleService = scheduleService;
        }

        public async Task<MuaEligibilityDto?> EvaluateAsync(Guid muaId, bool updateStatus = true)
        {
            var profile = await _db.MakeupArtistProfiles
                .Include(x => x.User)
                .Include(x => x.Services)
                .Include(x => x.Portfolios)
                .FirstOrDefaultAsync(x => x.MUAId == muaId);
            if (profile?.User == null) return null;

            var specialtyCount = await _db.MUAStyles.CountAsync(x => x.MUAId == muaId);
            var publicImageCount = profile.Portfolios
                .Where(x => !x.IsHidden)
                .SelectMany(x => x.ImageUrls ?? new List<string>())
                .Count(IsValidPublicUrl);
            var activeServiceCount = profile.Services.Count(x => x.IsActive);

            var requirements = new List<MuaEligibilityRequirementDto>
            {
                Requirement("accountActive", "Tài khoản đang hoạt động", profile.User.IsActive && !profile.User.DeletedAt.HasValue),
                Requirement("basicInformation", "Thông tin cơ bản hợp lệ", HasValidBasicInformation(profile.User.FullName, profile.User.Email)),
                Requirement("phoneNumber", "Có số điện thoại", !string.IsNullOrWhiteSpace(profile.User.PhoneNumber)),
                Requirement("city", "Có thành phố/khu vực", !string.IsNullOrWhiteSpace(profile.City)),
                Requirement("bio", "Có phần giới thiệu", !string.IsNullOrWhiteSpace(profile.Bio)),
                Requirement("specialty", "Có chuyên môn hoặc phong cách", !string.IsNullOrWhiteSpace(profile.Specialization) || specialtyCount > 0, !string.IsNullOrWhiteSpace(profile.Specialization) ? 1 : specialtyCount, 1),
                Requirement("activeService", "Có ít nhất 1 dịch vụ đang hoạt động", activeServiceCount >= 1, activeServiceCount, 1),
                Requirement("publicPortfolioImages", "Có ít nhất 3 ảnh portfolio công khai hợp lệ", publicImageCount >= 3, publicImageCount, 3)
            };

            var canPublish = requirements.All(x => x.IsMet);
            var hasValidSchedule = await _scheduleService.HasValidScheduleAsync(muaId);
            requirements.Add(Requirement("workingSchedule", "Có lịch làm việc hợp lệ", hasValidSchedule));
            if (updateStatus && profile.Status != MuaStatus.Suspended)
            {
                if (canPublish && profile.Status == MuaStatus.Draft)
                {
                    profile.Status = MuaStatus.Listed;
                    profile.ListedAt = DateTime.UtcNow;
                }
                else if (!canPublish && profile.Status == MuaStatus.Listed)
                {
                    profile.Status = MuaStatus.Draft;
                }

                profile.ProfileQualityScore = (int)Math.Round(requirements.Count(x => x.IsMet) * 100m / requirements.Count);
                profile.RankScore = (publicImageCount * 2) + (int)(profile.AverageRating * 10) + (profile.TotalBookings * 3);
                await _db.SaveChangesAsync();
            }

            var operational = profile.User.IsActive && !profile.User.DeletedAt.HasValue && profile.Status != MuaStatus.Suspended;
            var hasActiveBankAccount = await _db.MuaBankAccounts.AnyAsync(x => x.MuaId == muaId && x.IsActive);
            var availableBookingIds = await _db.MuaReceivables
                .Where(x => x.MuaId == muaId && x.Status == MuaReceivableStatus.Available)
                .Select(x => x.BookingId)
                .ToListAsync();
            var hasWithdrawableReceivable = availableBookingIds.Count > 0 && await _db.Bookings.AnyAsync(x =>
                availableBookingIds.Contains(x.BookingId)
                && x.Status != BookingStatus.Disputed
                && x.PaymentStatus != PaymentStatus.Frozen
                && x.PaymentStatus != PaymentStatus.RefundPending
                && !_db.Refunds.Any(r => r.BookingId == x.BookingId && r.Status != RefundStatus.Completed));
            return new MuaEligibilityDto
            {
                CompletionPercentage = (int)Math.Round(requirements.Count(x => x.IsMet) * 100m / requirements.Count),
                ProfileStatus = profile.Status.ToString(),
                CanPublishProfile = canPublish && profile.Status != MuaStatus.Suspended,
                CanReceiveBookings = operational && profile.Status == MuaStatus.Listed && canPublish && hasValidSchedule,
                CanWithdraw = operational && hasActiveBankAccount && hasWithdrawableReceivable,
                VerificationStatus = "NOT_SUBMITTED",
                Requirements = requirements,
                MissingRequirements = requirements.Where(x => !x.IsMet).ToList()
            };
        }

        public async Task<bool> SetSuspendedAsync(Guid muaId, bool suspended)
        {
            var profile = await _db.MakeupArtistProfiles.FirstOrDefaultAsync(x => x.MUAId == muaId);
            if (profile == null) return false;
            if (suspended)
            {
                profile.Status = MuaStatus.Suspended;
                await _db.SaveChangesAsync();
                await EvaluateAsync(muaId);
            }
            else if (profile.Status == MuaStatus.Suspended)
            {
                profile.Status = MuaStatus.Draft;
                await _db.SaveChangesAsync();
                await EvaluateAsync(muaId);
            }
            return true;
        }

        public async Task<bool> SetAccountActiveAsync(Guid userId, bool isActive)
        {
            var user = await _db.Users.FirstOrDefaultAsync(x => x.UserId == userId && !x.DeletedAt.HasValue);
            if (user == null) return false;
            user.IsActive = isActive;
            await _db.SaveChangesAsync();
            if (await _db.MakeupArtistProfiles.AnyAsync(x => x.MUAId == userId)) await EvaluateAsync(userId);
            return true;
        }

        private static MuaEligibilityRequirementDto Requirement(string key, string label, bool met, int? current = null, int? required = null) =>
            new() { Key = key, Label = label, IsMet = met, Current = current, Required = required };

        private static bool HasValidBasicInformation(string? fullName, string? email)
        {
            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email)) return false;
            try { _ = new MailAddress(email); return true; }
            catch (FormatException) { return false; }
        }

        internal static bool IsValidPublicUrl(string? value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
