namespace BeautyBookBackend.Infrastructure
{
    public static class UploadStorage
    {
        public const string RequestPath = "/uploads";

        public static string ResolvePath(IConfiguration configuration, IWebHostEnvironment environment)
        {
            var configuredPath = configuration["Uploads:StoragePath"]
                ?? Environment.GetEnvironmentVariable("UPLOAD_STORAGE_PATH");

            var path = string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(environment.ContentRootPath, "wwwroot", "uploads")
                : configuredPath;

            return Path.GetFullPath(path);
        }
    }
}
