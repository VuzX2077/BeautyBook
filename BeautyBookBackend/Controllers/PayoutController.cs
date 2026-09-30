using System.Security.Claims;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers
{
    // Payout authorization is enforced against the persisted MUA profile and
    // receivables in PayoutService, so a MUA can use the feature in either UI mode.
    [Authorize]
    [ApiController]
    [Route("api/mua")]
    public class PayoutController : ControllerBase
    {
        private readonly IPayoutService _service;private readonly IBankAccountService _banks;private readonly IAuthService _auth;public PayoutController(IPayoutService service,IAuthService auth,IBankAccountService banks){_service=service;_auth=auth;_banks=banks;}
        private Guid CurrentUserId=>Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value??Guid.Empty.ToString());
        // Legacy compatibility adapters. New clients use /api/bank-accounts.
        [HttpGet("bank-accounts")]public async Task<IActionResult> Banks()=>Ok(await _banks.GetAsync(CurrentUserId));
        [HttpPost("bank-accounts/request-otp")]public Task<IActionResult> RequestBankOtp(BankAccountDraftRequest r)=>BankErrors(()=>_banks.RequestAddOtpAsync(CurrentUserId,r),true);
        [HttpPost("bank-accounts/{id:guid}/request-otp")]public Task<IActionResult> RequestUpdateBankOtp(Guid id,BankAccountDraftRequest r)=>BankErrors(()=>_banks.RequestUpdateOtpAsync(CurrentUserId,id,r),true);
        [HttpPost("bank-accounts")]public Task<IActionResult> AddBank(UpsertBankAccountRequest r)=>BankErrors(()=>_banks.AddAsync(CurrentUserId,r));
        [HttpPut("bank-accounts/{id:guid}")]public Task<IActionResult> UpdateBank(Guid id,UpsertBankAccountRequest r)=>BankErrors(()=>_banks.UpdateAsync(CurrentUserId,id,r),true);
        [HttpPost("bank-accounts/{id:guid}/set-default")]public async Task<IActionResult> SetDefaultBank(Guid id,SetBankAccountDefaultRequest r)=>await BankPassword(r.CurrentPassword,()=>_banks.SetDefaultAsync(CurrentUserId,id),true);
        [HttpDelete("bank-accounts/{id:guid}")]public async Task<IActionResult> DeleteBank(Guid id)=>await _banks.DeactivateAsync(CurrentUserId,id)?NoContent():NotFound();
        [HttpPost("payouts")]public async Task<IActionResult> Create(CreatePayoutRequest r){try{return Ok(await _service.CreateAsync(CurrentUserId,r));}catch(BookingRuleException e){return StatusCode(e.StatusCode,new{e.Code,Message=e.Message});}catch(InvalidOperationException e){return Conflict(new{Code="PAYOUT_NOT_ALLOWED",Message=e.Message});}}
        [HttpGet("payouts")]public async Task<IActionResult> Mine()=>Ok(await _service.GetOwnAsync(CurrentUserId));
        [HttpGet("payouts/{id:guid}")]public async Task<IActionResult> MineById(Guid id){var payout=await _service.GetOwnByIdAsync(CurrentUserId,id);return payout==null?NotFound():Ok(payout);}
        private async Task<IActionResult> BankPassword<T>(string password,Func<Task<T>> action,bool nullable=false){if(!await _auth.VerifyPasswordAsync(CurrentUserId,password))return StatusCode(403,new{Code="SENSITIVE_AUTH_REQUIRED",Message="Mật khẩu xác nhận không đúng."});try{var result=await action();return nullable&&result is null?NotFound(new{Code="BANK_ACCOUNT_NOT_FOUND",Message="Không tìm thấy tài khoản ngân hàng."}):Ok(result);}catch(BookingRuleException e){return StatusCode(e.StatusCode,new{e.Code,Message=e.Message});}}
        private async Task<IActionResult> BankErrors<T>(Func<Task<T>> action,bool nullable=false){try{var result=await action();return nullable&&result is null?NotFound(new{Code="BANK_ACCOUNT_NOT_FOUND",Message="Không tìm thấy tài khoản ngân hàng."}):Ok(result);}catch(OtpCooldownException e){return StatusCode(429,new{Code="OTP_COOLDOWN",Message=e.Message});}catch(EmailDeliveryException){return StatusCode(503,new{Code="EMAIL_UNAVAILABLE",Message="Chưa thể gửi mã OTP. Vui lòng thử lại sau."});}catch(BookingRuleException e){return StatusCode(e.StatusCode,new{e.Code,Message=e.Message});}}
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
