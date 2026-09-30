using System.Security.Claims;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers;

[Authorize]
[ApiController]
[Route("api/bank-accounts")]
public sealed class BankAccountController(IBankAccountService service,IAuthService auth) : ControllerBase
{
    private Guid UserId=>Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    [HttpGet] public async Task<IActionResult> Get()=>Ok(await service.GetAsync(UserId));
    [HttpPost("request-otp")] public Task<IActionResult> RequestAddOtp(BankAccountDraftRequest request)=>WithErrors(()=>service.RequestAddOtpAsync(UserId,request),true);
    [HttpPost("{id:guid}/request-otp")] public Task<IActionResult> RequestUpdateOtp(Guid id,BankAccountDraftRequest request)=>WithErrors(()=>service.RequestUpdateOtpAsync(UserId,id,request),true);
    [HttpPost] public Task<IActionResult> Add(UpsertBankAccountRequest request)=>WithErrors(()=>service.AddAsync(UserId,request));
    [HttpPut("{id:guid}")] public Task<IActionResult> Update(Guid id,UpsertBankAccountRequest request)=>WithErrors(()=>service.UpdateAsync(UserId,id,request),true);
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id)=>await service.DeactivateAsync(UserId,id)?NoContent():NotFound(Error("BANK_ACCOUNT_NOT_FOUND","Không tìm thấy tài khoản ngân hàng."));
    [HttpPost("{id:guid}/set-default")] public async Task<IActionResult> SetDefault(Guid id,SetBankAccountDefaultRequest request)=>await WithPassword(request.CurrentPassword,()=>service.SetDefaultAsync(UserId,id),true);

    private async Task<IActionResult> WithPassword<T>(string password,Func<Task<T>> action,bool nullable=false)
    {
        if(!await auth.VerifyPasswordAsync(UserId,password))return StatusCode(403,Error("SENSITIVE_AUTH_REQUIRED","Mật khẩu xác nhận không đúng."));
        try{var result=await action();if(nullable&&result is null)return NotFound(Error("BANK_ACCOUNT_NOT_FOUND","Không tìm thấy tài khoản ngân hàng."));return Ok(result);}
        catch(BookingRuleException e){return StatusCode(e.StatusCode,Error(e.Code,e.Message));}
    }
    private async Task<IActionResult> WithErrors<T>(Func<Task<T>> action,bool nullable=false)
    {
        try
        {
            var result=await action();
            if(nullable&&result is null)return NotFound(Error("BANK_ACCOUNT_NOT_FOUND","Không tìm thấy tài khoản ngân hàng hoặc tài khoản người dùng không hoạt động."));
            return Ok(result);
        }
        catch(OtpCooldownException ex){return StatusCode(429,Error("OTP_COOLDOWN",ex.Message));}
        catch(EmailDeliveryException){return StatusCode(503,Error("EMAIL_UNAVAILABLE","Chưa thể gửi mã OTP. Vui lòng thử lại sau."));}
        catch(BookingRuleException ex){return StatusCode(ex.StatusCode,Error(ex.Code,ex.Message));}
    }
    private static object Error(string code,string message)=>new{Code=code,Message=message};
}
