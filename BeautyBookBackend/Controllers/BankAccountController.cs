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
    [HttpPost] public async Task<IActionResult> Add(UpsertBankAccountRequest request)=>await WithPassword(request.CurrentPassword,()=>service.AddAsync(UserId,request));
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id,UpsertBankAccountRequest request)=>await WithPassword(request.CurrentPassword,()=>service.UpdateAsync(UserId,id,request),true);
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id)=>await service.DeactivateAsync(UserId,id)?NoContent():NotFound(Error("BANK_ACCOUNT_NOT_FOUND","Không tìm thấy tài khoản ngân hàng."));
    [HttpPost("{id:guid}/set-default")] public async Task<IActionResult> SetDefault(Guid id,SetBankAccountDefaultRequest request)=>await WithPassword(request.CurrentPassword,()=>service.SetDefaultAsync(UserId,id),true);

    private async Task<IActionResult> WithPassword<T>(string password,Func<Task<T>> action,bool nullable=false)
    {
        if(!await auth.VerifyPasswordAsync(UserId,password))return StatusCode(403,Error("SENSITIVE_AUTH_REQUIRED","Mật khẩu xác nhận không đúng."));
        try{var result=await action();if(nullable&&result is null)return NotFound(Error("BANK_ACCOUNT_NOT_FOUND","Không tìm thấy tài khoản ngân hàng."));return Ok(result);}
        catch(BookingRuleException e){return StatusCode(e.StatusCode,Error(e.Code,e.Message));}
    }
    private static object Error(string code,string message)=>new{Code=code,Message=message};
}
