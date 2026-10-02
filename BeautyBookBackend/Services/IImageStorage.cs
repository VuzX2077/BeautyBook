namespace BeautyBookBackend.Services
{
    public interface IImageStorage
    {
        Task<string> UploadOwnedPublicImageAsync(Guid owner, Stream content, string contentType, string extension, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Owned uploads require server-side ownership tracking.");
        Task<string> UploadPublicImageAsync(
            Stream content,
            string contentType,
            string extension,
            CancellationToken cancellationToken = default);
    }
}
