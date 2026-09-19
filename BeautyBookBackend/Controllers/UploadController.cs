using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BeautyBookBackend.Infrastructure;

namespace BeautyBookBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UploadController : ControllerBase
    {
        private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
        { "image/jpeg", "image/png", "image/webp", "image/heic" };
        private readonly string _storagePath;

        public UploadController(IConfiguration configuration, IWebHostEnvironment environment) =>
            _storagePath = UploadStorage.ResolvePath(configuration, environment);

        [HttpPost("image")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> UploadImage(IFormFile file)
        {
            if (file.Length == 0 || file.Length > 10 * 1024 * 1024 || !AllowedTypes.Contains(file.ContentType))
                return BadRequest(new { Message = "Ảnh không hợp lệ hoặc vượt quá 10MB." });

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(extension)) extension = ".jpg";
            Directory.CreateDirectory(_storagePath);
            var fileName = $"{Guid.NewGuid():N}{extension}";
            await using var stream = System.IO.File.Create(Path.Combine(_storagePath, fileName));
            await file.CopyToAsync(stream);
            var url = $"{Request.Scheme}://{Request.Host}{UploadStorage.RequestPath}/{fileName}";
            return Ok(new { Url = url });
        }
    }
}
