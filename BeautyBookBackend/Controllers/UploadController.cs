using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BeautyBookBackend.Services;
using System.Security.Claims;

namespace BeautyBookBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UploadController : ControllerBase
    {
        private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
        { "image/jpeg", "image/png", "image/webp", "image/heic" };
        private readonly IImageStorage _imageStorage;

        public UploadController(IImageStorage imageStorage) => _imageStorage = imageStorage;

        [HttpPost("image")]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("media-upload")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> UploadImage(IFormFile file, [FromForm] string? purpose)
        {
            if (purpose is not ("avatar" or "portfolio" or "service" or "review"))
                return BadRequest(new { Code = "UPLOAD_PURPOSE_REQUIRED", Message = "Vui lòng cập nhật app. Giấy tờ, chat và bằng chứng phải dùng kho ảnh riêng tư." });
            if (file.Length == 0 || file.Length > 10 * 1024 * 1024 || !AllowedTypes.Contains(file.ContentType))
                return BadRequest(new { Message = "Ảnh không hợp lệ hoặc vượt quá 10MB." });

            using var input = new MemoryStream();
            await file.CopyToAsync(input, HttpContext.RequestAborted);
            byte[] pixels;
            try { pixels = VerificationImage.Normalize(input.ToArray()); }
            catch (ArgumentException ex) { return BadRequest(new { Message = ex.Message }); }
            // A client-supplied public purpose must not bypass receive-QR protection.
            try
            {
                _ = BankQrDecoder.DecodeImage(pixels);
                return BadRequest(new { Code = "FINANCIAL_QR_PUBLIC_FORBIDDEN", Message = "QR nhận tiền chỉ được dùng để đọc thông tin tài khoản, không được tải lên ảnh công khai." });
            }
            catch (InvalidOperationException) { /* Ordinary image or unrelated QR. */ }
            using var stream = new MemoryStream(pixels);
            var url = await _imageStorage.UploadOwnedPublicImageAsync(
                Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
                stream,
                "image/jpeg",
                ".jpg",
                HttpContext.RequestAborted);
            return Ok(new { Url = url });
        }

        [HttpPost("bank-qr")]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("media-upload")]
        [RequestSizeLimit(5 * 1024 * 1024)]
        [RequestFormLimits(MemoryBufferThreshold = 5 * 1024 * 1024, MultipartBodyLengthLimit = 5 * 1024 * 1024)]
        public async Task<IActionResult> UploadBankQr(IFormFile file)
        {
            if (file.Length == 0 || file.Length > 5 * 1024 * 1024 || !AllowedTypes.Contains(file.ContentType))
                return BadRequest(new { Message = "Ảnh QR không hợp lệ hoặc vượt quá 5MB." });

            await using var input = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await input.CopyToAsync(buffer, HttpContext.RequestAborted);
            BankQrData decoded;
            try {
                if (buffer.Length > 5 * 1024 * 1024) return BadRequest(new { Message = "Ảnh QR vượt quá 5MB." });
                decoded = BankQrDecoder.DecodeImage(VerificationImage.Normalize(buffer.ToArray()));
            }
            catch (ArgumentException ex) { return BadRequest(new { Code = "QR_IMAGE_INVALID", Message = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { Code = "QR_NOT_RECOGNIZED", Message = ex.Message }); }
            catch (Exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    Code = "QR_DECODER_UNAVAILABLE",
                    Message = "Bộ đọc QR đang tạm thời không khả dụng. Vui lòng thử lại sau."
                });
            }

            // Receive QR images are decode-only. Never store bytes, return the raw QR
            // payload, or generate a public URL containing beneficiary details.
            Response.Headers.CacheControl = "no-store";
            return Ok(new
            {
                decoded.Method,
                decoded.BankBin,
                decoded.AccountNumber,
                decoded.AccountName,
                RequiresManualAccountName = string.IsNullOrWhiteSpace(decoded.AccountName),
                RequiresManualAccountNumber = string.IsNullOrWhiteSpace(decoded.AccountNumber)
            });
        }
    }
}
