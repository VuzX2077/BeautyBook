using System.Net.Http.Headers;

namespace BeautyBookBackend.Services;

internal static class SupabaseStorageAuthentication
{
    public static void Apply(HttpRequestMessage request, string key)
    {
        static bool IsSegment(string value) => value.Length > 0 && value.All(c =>
            c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-');

        var isSecret = key.StartsWith("sb_secret_", StringComparison.Ordinal)
            && IsSegment(key["sb_secret_".Length..]);
        var segments = key.Split('.');
        // Classify JWT syntax only; Supabase validates the credential itself.
        var isJwt = segments.Length == 3 && segments.All(IsSegment);
        if (!isSecret && !isJwt)
            throw new InvalidOperationException("Supabase Storage credential format is invalid.");

        request.Headers.Add("apikey", key);
        request.Headers.Authorization = isSecret ? null : new AuthenticationHeaderValue("Bearer", key);
    }
}
