using System.Security.Claims;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers;

// Legacy compatibility adapter. New clients use /api/bank-accounts.
[Authorize]
[ApiController]
[Route("api/customer-bank-accounts")]
public sealed class CustomerBankAccountController(IBankAccountService service,IAuthService auth) : ControllerBase
{
    private Guid UserId=>Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    [HttpGet] public async Task<IActionResult> Get()=>Ok(await service.GetAsync(UserId));
    [HttpPost("request-otp")] public Task<IActionResult> RequestOtp(BankAccountDraftRequest r)=>Errors(()=>service.RequestAddOtpAsync(UserId,r),true);
    [HttpPost("{id:guid}/request-otp")] public Task<IActionResult> RequestUpdateOtp(Guid id,BankAccountDraftRequest r)=>Errors(()=>service.RequestUpdateOtpAsync(UserId,id,r),true);
    [HttpPost] public Task<IActionResult> Add(UpsertBankAccountRequest r)=>Errors(()=>service.AddAsync(UserId,r));
    [HttpPut("{id:guid}")] public Task<IActionResult> Update(Guid id,UpsertBankAccountRequest r)=>Errors(()=>service.UpdateAsync(UserId,id,r),true);
    [HttpPost("{id:guid}/set-default")] public async Task<IActionResult> Default(Guid id,SetBankAccountDefaultRequest r)=>await Password(r.CurrentPassword,()=>service.SetDefaultAsync(UserId,id),true);
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id)=>await service.DeactivateAsync(UserId,id)?NoContent():NotFound();
    private async Task<IActionResult>Password<T>(string password,Func<Task<T>> action,bool nullable=false){if(!await auth.VerifyPasswordAsync(UserId,password))return StatusCode(403,new{Code="SENSITIVE_AUTH_REQUIRED",Message="Mật khẩu xác nhận không đúng."});try{var result=await action();return nullable&&result is null?NotFound(new{Code="BANK_ACCOUNT_NOT_FOUND",Message="Không tìm thấy tài khoản ngân hàng."}):Ok(result);}catch(BookingRuleException e){return StatusCode(e.StatusCode,new{e.Code,Message=e.Message});}}
    private async Task<IActionResult>Errors<T>(Func<Task<T>> action,bool nullable=false){try{var result=await action();return nullable&&result is null?NotFound(new{Code="BANK_ACCOUNT_NOT_FOUND",Message="Không tìm thấy tài khoản ngân hàng."}):Ok(result);}catch(OtpCooldownException e){return StatusCode(429,new{Code="OTP_COOLDOWN",Message=e.Message});}catch(EmailDeliveryException){return StatusCode(503,new{Code="EMAIL_UNAVAILABLE",Message="Chưa thể gửi mã OTP. Vui lòng thử lại sau."});}catch(BookingRuleException e){return StatusCode(e.StatusCode,new{e.Code,Message=e.Message});}}
}
