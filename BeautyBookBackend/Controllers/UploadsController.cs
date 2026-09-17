using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UploadsController : ControllerBase
{
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };

    private readonly IWebHostEnvironment _environment;

    public UploadsController(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    [Authorize]
    [HttpPost("image")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(IFormFile file)
    {
        if (file.Length == 0) return BadRequest(new { Message = "Ảnh không hợp lệ." });
        if (file.Length > 10 * 1024 * 1024) return BadRequest(new { Message = "Ảnh không được vượt quá 10 MB." });

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
            return BadRequest(new { Message = "Chỉ hỗ trợ ảnh JPG, PNG hoặc WebP." });

        var directory = Path.Combine(_environment.ContentRootPath, "uploads");
        Directory.CreateDirectory(directory);
        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var filePath = Path.Combine(directory, fileName);

        await using var stream = System.IO.File.Create(filePath);
        await file.CopyToAsync(stream);

        return Ok(new { Url = $"/uploads/{fileName}" });
    }
}
