using System.Security.Claims;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers
{
    [Authorize(Roles = nameof(UserRole.MUA))]
    [ApiController]
    [Route("api/mua/earnings")]
    public class MuaEarningsController : ControllerBase
    {
        private readonly IMuaReceivableService _receivables;
        public MuaEarningsController(IMuaReceivableService receivables) => _receivables=receivables;
        private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString());
        [HttpGet]
        public async Task<IActionResult> Get() => Ok(await _receivables.GetEarningsAsync(CurrentUserId));
    }
}
