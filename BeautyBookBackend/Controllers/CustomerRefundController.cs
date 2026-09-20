using System.Security.Claims;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/customer-refunds")]
    public class CustomerRefundController : ControllerBase
    {
        private readonly IRefundService _refunds;
        public CustomerRefundController(IRefundService refunds) => _refunds = refunds;
        private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString());

        [HttpPost("{id:guid}/destination")]
        public async Task<IActionResult> SetDestination(Guid id, [FromBody] RefundDestinationRequest request)
        {
            var result = await _refunds.SetDestinationAsync(id, CurrentUserId, request.BankAccountId);
            return result == null
                ? Conflict(new { Code = "REFUND_DESTINATION_NOT_ALLOWED", Message = "Không thể cập nhật tài khoản nhận tiền cho refund này." })
                : Ok(result);
        }
    }
}
