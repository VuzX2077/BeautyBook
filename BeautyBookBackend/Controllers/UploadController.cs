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
    }
}
