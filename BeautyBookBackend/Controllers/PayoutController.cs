using System.Security.Claims;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers
{
    [Authorize(Roles=nameof(UserRole.MUA))]
    [ApiController]
    [Route("api/mua")]
    public class PayoutController : ControllerBase
    {
        private readonly IPayoutService _service;private readonly IAuthService _auth;public PayoutController(IPayoutService service,IAuthService auth){_service=service;_auth=auth;}
        private Guid CurrentUserId=>Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value??Guid.Empty.ToString());
        [HttpGet("bank-accounts")]public async Task<IActionResult> Banks()=>Ok(await _service.GetBankAccountsAsync(CurrentUserId));
        [HttpPost("bank-accounts")]public async Task<IActionResult> AddBank(UpsertMuaBankAccountRequest r){if(!await _auth.VerifyPasswordAsync(CurrentUserId,r.CurrentPassword))return StatusCode(403,new{Code="SENSITIVE_AUTH_REQUIRED",Message="Mật khẩu xác nhận không đúng."});try{return Ok(await _service.AddBankAccountAsync(CurrentUserId,r));}catch(InvalidOperationException e){return BadRequest(new{Message=e.Message});}}
        [HttpPut("bank-accounts/{id:guid}")]public async Task<IActionResult> UpdateBank(Guid id,UpsertMuaBankAccountRequest r){if(!await _auth.VerifyPasswordAsync(CurrentUserId,r.CurrentPassword))return StatusCode(403,new{Code="SENSITIVE_AUTH_REQUIRED",Message="Mật khẩu xác nhận không đúng."});try{var x=await _service.UpdateBankAccountAsync(CurrentUserId,id,r);return x==null?NotFound():Ok(x);}catch(InvalidOperationException e){return BadRequest(new{Message=e.Message});}}
        [HttpDelete("bank-accounts/{id:guid}")]public async Task<IActionResult> DeleteBank(Guid id)=>await _service.DeactivateBankAccountAsync(CurrentUserId,id)?NoContent():NotFound();
        [HttpPost("payouts")]public async Task<IActionResult> Create(CreatePayoutRequest r){try{return Ok(await _service.CreateAsync(CurrentUserId,r));}catch(InvalidOperationException e){return Conflict(new{Code="PAYOUT_NOT_ALLOWED",Message=e.Message});}}
        [HttpGet("payouts")]public async Task<IActionResult> Mine()=>Ok(await _service.GetOwnAsync(CurrentUserId));
        [HttpGet("payouts/{id:guid}")]public async Task<IActionResult> MineById(Guid id){var payout=await _service.GetOwnByIdAsync(CurrentUserId,id);return payout==null?NotFound():Ok(payout);}
    }

    [Authorize(Roles=nameof(UserRole.Admin))]
    [ApiController]
    [Route("api/admin/payouts")]
    public class AdminPayoutController : ControllerBase
    {
        private readonly IPayoutService _service;public AdminPayoutController(IPayoutService service)=>_service=service;
        private Guid CurrentUserId=>Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value??Guid.Empty.ToString());
        [HttpGet]public async Task<IActionResult> Pending()=>Ok(await _service.GetPendingAdminAsync());
        [HttpGet("{id:guid}")]public async Task<IActionResult> ById(Guid id){var payout=await _service.GetAdminByIdAsync(id);return payout==null?NotFound():Ok(payout);}
        [HttpPost("{id:guid}/start-processing")]public async Task<IActionResult> Start(Guid id,PayoutActionRequest r){var x=await _service.StartProcessingAsync(id,CurrentUserId,r.Reference);return x==null?Conflict():Ok(x);}
        [HttpPost("{id:guid}/complete")]public async Task<IActionResult> Complete(Guid id,PayoutCompleteRequest r){var x=await _service.CompleteAsync(id,CurrentUserId,r.Reference);return x==null?Conflict():Ok(x);}
        [HttpPost("{id:guid}/fail")]public async Task<IActionResult> Fail(Guid id,PayoutFailRequest r){var x=await _service.FailAsync(id,CurrentUserId,r.FailureCode,r.FailureMessage,r.ConfirmedFundsNotSent);return x==null?Conflict():Ok(x);}
    }
}
