using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BeautyBookBackend.Controllers
{
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ApiController]
    [Route("api/admin")]
    public sealed class AdminMuaController : ControllerBase
    {
        private readonly IMuaEligibilityService _eligibility;
        private readonly IMuaService _muaService;
        private readonly IMuaScheduleService _scheduleService;
        public AdminMuaController(IMuaEligibilityService eligibility, IMuaService muaService, IMuaScheduleService scheduleService)
        {
            _eligibility = eligibility;
            _muaService = muaService;
            _scheduleService = scheduleService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString());

        [HttpGet("mua-applications")]
        public async Task<IActionResult> GetApplications([FromQuery] string? status = "PendingReview", [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
            Ok(await _eligibility.GetApplicationsAsync(status, page, pageSize));

        [HttpPost("mua-applications/{muaId:guid}/approve")]
        public async Task<IActionResult> Approve(Guid muaId) =>
            await _eligibility.ReviewAsync(muaId, CurrentUserId, true) ? Ok(await _eligibility.EvaluateAsync(muaId)) : Conflict(new { Message = "Hồ sơ không còn ở trạng thái chờ duyệt." });

        [HttpPost("mua-applications/{muaId:guid}/reject")]
        public async Task<IActionResult> Reject(Guid muaId, RejectMuaApplicationRequest request) =>
            await _eligibility.ReviewAsync(muaId, CurrentUserId, false, request.Reason, request.ReasonCodes, request.Items) ? Ok(await _eligibility.EvaluateAsync(muaId)) : Conflict(new { Message = "Hồ sơ không còn ở trạng thái chờ duyệt." });

        [HttpGet("muas/{muaId:guid}")]
        public async Task<IActionResult> GetMuaForManagement(Guid muaId)
        {
            var profile = await _muaService.GetMuaByIdAsync(muaId, muaId);
            if (profile == null) return NotFound();
            var db = HttpContext.RequestServices.GetRequiredService<BeautyBookBackend.Data.ApplicationDbContext>();
            var privateProfile = await db.MakeupArtistProfiles.AsNoTracking().FirstAsync(x => x.MUAId == muaId);
            var bank = await db.MuaBankAccounts.AsNoTracking().Where(x => x.MuaId == muaId && x.IsActive)
                .OrderByDescending(x => x.IsDefault).Select(x => new { x.BankCode, x.BankName, x.AccountNumber, x.AccountHolderName, x.VerificationStatus }).FirstOrDefaultAsync();
            var verificationDocuments = new { privateProfile.Address, privateProfile.IdentityFrontUrl, privateProfile.IdentityBackUrl, privateProfile.PortraitUrl, privateProfile.CertificateUrls };
            return Ok(new { Profile = profile, VerificationDocuments = verificationDocuments, BankAccount = bank, Schedule = await _scheduleService.GetManagementScheduleAsync(muaId), Eligibility = await _eligibility.EvaluateAsync(muaId) });
        }

        [HttpPatch("muas/{muaId:guid}/suspension")]
        public async Task<IActionResult> SetSuspension(Guid muaId, SetMuaSuspensionRequest request) =>
            await _eligibility.SetSuspendedAsync(muaId, request.Suspended) ? NoContent() : NotFound();

        [HttpPatch("users/{userId:guid}/active")]
        public async Task<IActionResult> SetAccountActive(Guid userId, SetAccountActiveRequest request) =>
            await _eligibility.SetAccountActiveAsync(userId, request.IsActive) ? NoContent() : NotFound();
    }
}
