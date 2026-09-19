using System.Security.Claims;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers
{
    [ApiController]
    [Route("api/Mua")]
    public sealed class MuaScheduleController : ControllerBase
    {
        private readonly IMuaScheduleService _schedule;
        private readonly IMuaEligibilityService _eligibility;
        private readonly IMuaService _muaService;
        public MuaScheduleController(IMuaScheduleService schedule, IMuaEligibilityService eligibility, IMuaService muaService)
        {
            _schedule = schedule;
            _eligibility = eligibility;
            _muaService = muaService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString());

        [HttpGet("{muaId:guid}/schedule")]
        public async Task<IActionResult> GetPublic(Guid muaId)
        {
            if (await _muaService.GetMuaByIdAsync(muaId) == null) return NotFound();
            return Ok(await _schedule.GetPublicScheduleAsync(muaId));
        }

        [Authorize]
        [HttpGet("schedule/me")]
        public async Task<IActionResult> GetMine()
        {
            var result = await _schedule.GetManagementScheduleAsync(CurrentUserId);
            return result == null ? NotFound() : Ok(result);
        }

        [Authorize]
        [HttpPut("schedule")]
        public async Task<IActionResult> Replace(ReplaceWorkingScheduleRequest request)
        {
            try
            {
                await _schedule.ReplaceWorkingScheduleAsync(CurrentUserId, request.Schedules);
                await _eligibility.EvaluateAsync(CurrentUserId);
                return Ok(await _schedule.GetManagementScheduleAsync(CurrentUserId));
            }
            catch (InvalidOperationException ex) { return BadRequest(new { Code = ex.Message, Message = "Lịch làm việc không hợp lệ." }); }
        }

        [Authorize]
        [HttpPost("time-off")]
        public async Task<IActionResult> AddTimeOff(CreateMuaTimeOffRequest request)
        {
            try { return Ok(await _schedule.AddTimeOffAsync(CurrentUserId, request)); }
            catch (InvalidOperationException ex) { return BadRequest(new { Code = ex.Message, Message = "Khoảng nghỉ không hợp lệ." }); }
        }

        [Authorize]
        [HttpDelete("time-off/{id:guid}")]
        public async Task<IActionResult> DeleteTimeOff(Guid id) =>
            await _schedule.DeleteTimeOffAsync(CurrentUserId, id) ? NoContent() : NotFound();
    }
}
