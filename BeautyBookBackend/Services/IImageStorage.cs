namespace BeautyBookBackend.Services
{
    public interface IImageStorage
    {
        Task<string> UploadPublicImageAsync(
            Stream content,
            string contentType,
            string extension,
            CancellationToken cancellationToken = default);
    }
}
