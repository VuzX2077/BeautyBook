namespace BeautyBookBackend.Services.Monetization;

public static class CommerceCheckoutReadiness
{
    // Existing app features stay available when commerce credentials are not configured.
    public static bool Ready(IConfiguration config)
    {
        if (!config.GetValue<bool>("MuaMonetization:LivePaymentsEnabled")) return false;
        foreach (var key in new[] { "ClientId", "ApiKey", "ChecksumKey", "Channel" })
            if (string.IsNullOrWhiteSpace(config["CommercePayOS:" + key])) return false;
        if (!Uri.TryCreate(config["MuaWeb:Origin"], UriKind.Absolute, out var origin) || origin.Scheme != "https" || !origin.IsDefaultPort || origin.UserInfo.Length != 0 || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0) return false;
        foreach (var key in new[] { "ReturnUrl", "CancelUrl" })
            if (!Uri.TryCreate(config["CommercePayOS:" + key], UriKind.Absolute, out var url) || url.Scheme != "https" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Host != origin.Host) return false;
        return true;
    }
}
