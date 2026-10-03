using System.Security.Claims;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Controllers
{
    [Authorize(Roles = "Admin")]
    [ApiController]
    [Route("api/[controller]")]
    public class RefundController : ControllerBase
    {
        private readonly IRefundService _refundService;
        private readonly FinancialMediaService? _financial;
        public RefundController(IRefundService refundService, FinancialMediaService? financial = null) { _refundService = refundService; _financial = financial; }

        private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString());

        [HttpGet]
        public async Task<IActionResult> GetQueue([FromQuery] RefundStatus? status = null) =>
            Ok(await _refundService.GetAdminQueueAsync(status));

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var result = await _refundService.GetAdminByIdAsync(id);
            return result == null ? NotFound() : Ok(result);
        }

        [HttpGet("/api/admin/refunds/{id:guid}/transfer-qr")]
        [ResponseCache(NoStore=true, Location=ResponseCacheLocation.None)]
        public async Task<IActionResult> TransferQr(Guid id) {
            Response.Headers.CacheControl="no-store";
            var refund=await _refundService.GetAdminByIdAsync(id);
            if(refund==null)return NotFound();
            if(refund.Status is not (RefundStatus.Pending or RefundStatus.ManualActionRequired or RefundStatus.Processing) || !string.IsNullOrWhiteSpace(refund.ProviderReferenceId))return Conflict(new { Message="Refund không ở trạng thái chuyển thủ công." });
            try {
                if(refund.DestinationBankCode=="MOMO") {
                    var image=_financial==null?null:await _financial.RefundImageAsync(id,HttpContext.RequestAborted);
                    return image==null?Conflict(new { Message="Không có QR MoMo hợp lệ; hãy đối chiếu thông tin thủ công." }):Ok(new { RefundId=id, refund.Amount, ImageDataUrl=image, Kind="MOMO_ORIGINAL", ContainsRefundAmount=false });
                }
                var dto=new AdminPayoutDto { Id=id, Amount=refund.Amount, Status=BeautyBookBackend.Models.Enums.PayoutStatus.Processing, BankCode=refund.DestinationBankCode??"", BankBin=refund.DestinationBankBin, AccountNumber=refund.DestinationAccountNumber??"", AccountHolderName=refund.DestinationAccountName??"" };
                return Ok(new { RefundId=id, refund.Amount, ImageDataUrl=PayoutQrGenerator.ImageDataUrl(dto), Kind="BANK_GENERATED", ContainsRefundAmount=true });
            } catch(InvalidOperationException) { return Conflict(new { Message="QR chưa khả dụng; hãy đối chiếu thông tin thủ công." }); }
            catch(HttpRequestException) { return StatusCode(503,new { Message="Chưa thể đọc QR riêng tư." }); }
        }
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
