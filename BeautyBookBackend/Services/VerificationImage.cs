using SkiaSharp;

namespace BeautyBookBackend.Services;

public static class VerificationImage
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public static byte[] Normalize(byte[] source)
    {
        if (source.Length == 0 || source.Length > MaxBytes) throw new ArgumentException("Ảnh vượt quá 10 MB hoặc không có dữ liệu.");
        using var data = SKData.CreateCopy(source);
        using var codec = SKCodec.Create(data);
        if (codec == null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp)
            || codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 24_000_000)
            throw new ArgumentException("Chọn ảnh JPEG, PNG hoặc WebP hợp lệ, tối đa 24 megapixel.");
        using var bitmap = new SKBitmap(codec.Info.Width, codec.Info.Height);
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new ArgumentException("Ảnh bị hỏng hoặc không đọc được.");
        // Apply EXIF orientation physically, then encode fresh pixels without GPS/EXIF.
        var swap = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        using var surface = SKSurface.Create(new SKImageInfo(swap ? bitmap.Height : bitmap.Width, swap ? bitmap.Width : bitmap.Height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        switch (codec.EncodedOrigin)
        {
            case SKEncodedOrigin.TopRight: canvas.Translate(bitmap.Width, 0); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.BottomRight: canvas.Translate(bitmap.Width, bitmap.Height); canvas.RotateDegrees(180); break;
            case SKEncodedOrigin.BottomLeft: canvas.Translate(0, bitmap.Height); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(bitmap.Height, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(bitmap.Height, bitmap.Width); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, bitmap.Width); canvas.RotateDegrees(-90); break;
        }
        canvas.DrawBitmap(bitmap, 0, 0);
        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        if (encoded.Size > MaxBytes) throw new ArgumentException("Ảnh sau xử lý vượt quá 10 MB.");
        return encoded.ToArray();
    }
}
