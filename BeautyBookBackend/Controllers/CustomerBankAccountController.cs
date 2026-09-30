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
    [HttpPost] public async Task<IActionResult> Add(UpsertBankAccountRequest r)=>await Password(r.CurrentPassword,()=>service.AddAsync(UserId,r));
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id,UpsertBankAccountRequest r)=>await Password(r.CurrentPassword,()=>service.UpdateAsync(UserId,id,r),true);
    [HttpPost("{id:guid}/set-default")] public async Task<IActionResult> Default(Guid id,SetBankAccountDefaultRequest r)=>await Password(r.CurrentPassword,()=>service.SetDefaultAsync(UserId,id),true);
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id)=>await service.DeactivateAsync(UserId,id)?NoContent():NotFound();
    private async Task<IActionResult>Password<T>(string password,Func<Task<T>> action,bool nullable=false){if(!await auth.VerifyPasswordAsync(UserId,password))return StatusCode(403,new{Code="SENSITIVE_AUTH_REQUIRED",Message="Mật khẩu xác nhận không đúng."});try{var result=await action();return nullable&&result is null?NotFound(new{Code="BANK_ACCOUNT_NOT_FOUND",Message="Không tìm thấy tài khoản ngân hàng."}):Ok(result);}catch(BookingRuleException e){return StatusCode(e.StatusCode,new{e.Code,Message=e.Message});}}
}
