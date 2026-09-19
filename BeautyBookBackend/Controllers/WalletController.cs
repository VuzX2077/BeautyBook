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
            return StatusCode(StatusCodes.Status410Gone, new { Code = "WALLET_DEPOSIT_DEPRECATED", Message = "Ví lưu trữ đã ngừng nhận giao dịch mới." });
        }

        [HttpPost("topups")]
        public async Task<IActionResult> CreateTopUp([FromBody] CreateTopUpDto request)
        {
            return StatusCode(StatusCodes.Status410Gone, new { Code = "WALLET_TOPUP_DEPRECATED", Message = "Nạp tiền vào Ví BBook đã ngừng hoạt động. Booking mới thanh toán cọc trực tiếp." });
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
            return StatusCode(StatusCodes.Status410Gone, new { Code = "WALLET_WITHDRAW_DEPRECATED", Message = "Rút tiền qua Ví BBook đã ngừng hoạt động. Số dư legacy cần được đối soát thủ công; doanh thu MUA dùng Payout." });
        }
    }

    public class WithdrawRequest
    {
        public decimal Amount { get; set; }
    }
}
