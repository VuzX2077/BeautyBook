using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;

namespace BeautyBookBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class WalletController : ControllerBase
    {
        private readonly IWalletService _walletService;

        public WalletController(IWalletService walletService)
        {
            _walletService = walletService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString());

        [HttpGet]
        public async Task<IActionResult> GetWallet()
        {
            var wallet = await _walletService.GetWalletAsync(CurrentUserId);
            if (wallet == null)
            {
                return NotFound(new { Message = "Không tìm thấy thông tin ví của người dùng này." });
            }
            return Ok(wallet);
        }

        [Authorize(Roles = nameof(UserRole.Admin))]
        [HttpPost("deposit")]
        public async Task<IActionResult> Deposit([FromBody] DepositDto depositDto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var success = await _walletService.DepositAsync(CurrentUserId, depositDto.Amount, depositDto.Description);
            if (!success)
            {
                return BadRequest(new { Message = "Yêu cầu nạp tiền thất bại." });
            }

            return Ok(new { Message = $"Nạp thành công {depositDto.Amount:N0} VND vào ví ảo! Chúc bạn có trải nghiệm tuyệt vời." });
        }

        [HttpPost("topups")]
        public async Task<IActionResult> CreateTopUp([FromBody] CreateTopUpDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var topUp = await _walletService.CreateTopUpAsync(CurrentUserId, request);
                return Ok(topUp);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpGet("topups")]
        public async Task<IActionResult> GetTopUps()
        {
            var topUps = await _walletService.GetTopUpsAsync(CurrentUserId);
            return Ok(topUps);
        }

        [HttpGet("topups/{topUpId:guid}")]
        public async Task<IActionResult> GetTopUp(Guid topUpId)
        {
            var topUp = await _walletService.GetTopUpAsync(CurrentUserId, topUpId);
            return topUp == null ? NotFound(new { Message = "Không tìm thấy yêu cầu nạp tiền." }) : Ok(topUp);
        }

        [AllowAnonymous]
        [HttpPost("topups/payos/webhook")]
        public async Task<IActionResult> PayOsWebhook([FromBody] PayOsWebhookDto webhook)
        {
            var handled = await _walletService.HandlePayOsWebhookAsync(webhook);
            if (!handled)
            {
                return BadRequest(new { Message = "Webhook payOS không hợp lệ hoặc không khớp giao dịch nạp tiền." });
            }

            return Ok(new { Message = "Webhook payOS đã được xử lý." });
        }

        [HttpPost("withdraw")]
        public async Task<IActionResult> Withdraw([FromBody] WithdrawRequest request)
        {
            if (request.Amount < 50000)
            {
                return BadRequest(new { Message = "Số tiền rút tối thiểu phải từ 50,000 VND trở lên." });
            }

            var success = await _walletService.WithdrawAsync(CurrentUserId, request.Amount);
            if (!success)
            {
                return BadRequest(new { Message = "Rút tiền thất bại. Số dư trong ví ảo của bạn không đủ để thực hiện giao dịch này." });
            }

            return Ok(new { Message = $"Đã gửi yêu cầu rút {request.Amount:N0} VND thành công! Tiền sẽ được giải ngân về ngân hàng liên kết trong vòng 24 giờ." });
        }
    }

    public class WithdrawRequest
    {
        public decimal Amount { get; set; }
    }
}
