using SkiaSharp;

namespace BeautyBookBackend.Services;

public static class AiMakeupImage
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public static byte[] Normalize(byte[] bytes, string mime)
    {
        if (bytes.Length == 0 || bytes.Length > MaxBytes) throw new AiMakeupException(400, "AI_IMAGE_SIZE", "Ảnh phải nhỏ hơn 5 MB.");
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec == null || !((mime == "image/jpeg" && codec.EncodedFormat == SKEncodedImageFormat.Jpeg) ||
            (mime == "image/png" && codec.EncodedFormat == SKEncodedImageFormat.Png)))
            throw new AiMakeupException(400, "AI_IMAGE_FORMAT", "Vui lòng chọn ảnh JPEG hoặc PNG hợp lệ.");
        if (codec.Info.Width < 128 || codec.Info.Height < 128 || (long)codec.Info.Width * codec.Info.Height > 4_000_000 || codec.FrameCount > 1)
            throw new AiMakeupException(400, "AI_IMAGE_RESOLUTION", "Ảnh cần rõ, tối thiểu 128 pixel và tối đa 4 megapixel.");
        try
        {
            // Existing pixel decoder also corrects orientation and removes EXIF/GPS.
            var normalized = VerificationImage.Normalize(bytes);
            if (normalized.Length > MaxBytes) throw new ArgumentException();
            return normalized;
        }
        catch (ArgumentException) { throw new AiMakeupException(400, "AI_IMAGE_INVALID", "Không đọc được ảnh. Vui lòng chọn ảnh khác."); }
    }
}
