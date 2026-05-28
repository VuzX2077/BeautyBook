using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BeautyBookBackend.DTOs;
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
