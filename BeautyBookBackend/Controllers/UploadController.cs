using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BeautyBookBackend.Services;

namespace BeautyBookBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UploadController : ControllerBase
    {
        private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
        { "image/jpeg", "image/png", "image/webp", "image/heic" };
        private static readonly IReadOnlyDictionary<string, string> ExtensionsByContentType =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["image/jpeg"] = ".jpg",
                ["image/png"] = ".png",
                ["image/webp"] = ".webp",
                ["image/heic"] = ".heic"
            };
        private readonly IImageStorage _imageStorage;

        public UploadController(IImageStorage imageStorage) => _imageStorage = imageStorage;

        [HttpPost("image")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> UploadImage(IFormFile file)
        {
            if (file.Length == 0 || file.Length > 10 * 1024 * 1024 || !AllowedTypes.Contains(file.ContentType))
                return BadRequest(new { Message = "Ảnh không hợp lệ hoặc vượt quá 10MB." });

            var extension = ExtensionsByContentType[file.ContentType];
            await using var stream = file.OpenReadStream();
            var url = await _imageStorage.UploadPublicImageAsync(
                stream,
                file.ContentType,
                extension,
                HttpContext.RequestAborted);
            return Ok(new { Url = url });
        }

        [HttpPost("bank-qr")]
        [RequestSizeLimit(5 * 1024 * 1024)]
        public async Task<IActionResult> UploadBankQr(IFormFile file)
        {
            if (file.Length == 0 || file.Length > 5 * 1024 * 1024 || !AllowedTypes.Contains(file.ContentType))
                return BadRequest(new { Message = "Ảnh QR không hợp lệ hoặc vượt quá 5MB." });

            await using var input = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await input.CopyToAsync(buffer, HttpContext.RequestAborted);
            BankQrData decoded;
            try { decoded = BankQrDecoder.DecodeImage(buffer.ToArray()); }
            catch (InvalidOperationException ex) { return BadRequest(new { Code = "QR_NOT_RECOGNIZED", Message = ex.Message }); }

            buffer.Position = 0;
            var url = await _imageStorage.UploadPublicImageAsync(
                buffer, file.ContentType, ExtensionsByContentType[file.ContentType], HttpContext.RequestAborted);
            return Ok(new
            {
                Url = url,
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
