using System.Security.Claims;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/customer-bank-accounts")]
    public class CustomerBankAccountController : ControllerBase
    {
        private readonly IRefundService _refunds;
        private readonly IAuthService _auth;
        public CustomerBankAccountController(IRefundService refunds, IAuthService auth) { _refunds = refunds; _auth = auth; }
        private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString());

        [HttpGet]
        public async Task<IActionResult> Get() => Ok(await _refunds.GetBankAccountsAsync(CurrentUserId));

        [HttpPost]
        public async Task<IActionResult> Add([FromBody] UpsertCustomerBankAccountRequest request) =>
            !await _auth.VerifyPasswordAsync(CurrentUserId, request.CurrentPassword) ? StatusCode(403, new { Code = "SENSITIVE_AUTH_REQUIRED", Message = "Mật khẩu xác nhận không đúng." }) : Ok(await _refunds.AddBankAccountAsync(CurrentUserId, request));

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertCustomerBankAccountRequest request)
        {
            if (!await _auth.VerifyPasswordAsync(CurrentUserId, request.CurrentPassword)) return StatusCode(403, new { Code = "SENSITIVE_AUTH_REQUIRED", Message = "Mật khẩu xác nhận không đúng." });
            var result = await _refunds.UpdateBankAccountAsync(CurrentUserId, id, request);
            return result == null ? NotFound() : Ok(result);
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id) =>
            await _refunds.DeactivateBankAccountAsync(CurrentUserId, id) ? NoContent() : NotFound();
    }
}
