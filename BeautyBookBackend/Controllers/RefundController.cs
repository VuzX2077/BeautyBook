using System.Security.Claims;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers
{
    [Authorize(Roles = "Admin")]
    [ApiController]
    [Route("api/[controller]")]
    public class RefundController : ControllerBase
    {
        private readonly IRefundService _refundService;
        public RefundController(IRefundService refundService) => _refundService = refundService;

        private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString());

        [HttpPost("{id:guid}/start-processing")]
        public async Task<IActionResult> StartProcessing(Guid id, [FromBody] RefundProcessingRequest request)
        {
            var result = await _refundService.StartProcessingAsync(id, CurrentUserId, request.Reference);
            return result == null ? Conflict(new { Message = "Refund không ở trạng thái có thể bắt đầu xử lý." }) : Ok(result);
        }

        [HttpPost("{id:guid}/complete")]
        public async Task<IActionResult> Complete(Guid id, [FromBody] RefundCompletionRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var result = await _refundService.CompleteAsync(id, CurrentUserId, request.Reference);
            return result == null ? Conflict(new { Message = "Refund chưa ở trạng thái Processing hoặc thiếu bằng chứng hoàn tiền." }) : Ok(result);
        }

        [HttpPost("{id:guid}/fail")]
        public async Task<IActionResult> Fail(Guid id, [FromBody] RefundFailureRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var result = await _refundService.FailAsync(id, CurrentUserId, request.FailureCode, request.FailureMessage);
            return result == null ? Conflict(new { Message = "Refund không ở trạng thái Processing." }) : Ok(result);
        }

        [HttpPost("{id:guid}/retry")]
        public async Task<IActionResult> Retry(Guid id)
        {
            var result = await _refundService.RetryAsync(id, CurrentUserId);
            return result == null ? Conflict(new { Message = "Chỉ refund đã xác nhận Failed mới có thể đưa về hàng đợi xử lý thủ công." }) : Ok(result);
        }
    }
}
