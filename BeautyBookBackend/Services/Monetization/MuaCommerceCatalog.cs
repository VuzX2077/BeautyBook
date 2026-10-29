namespace BeautyBookBackend.Services.Monetization;

public sealed record MuaCommerceProduct(string Id, string Name, int AmountVnd, int? Days);

public static class MuaCommerceCatalog
{
    public const string Activation = "bbook_mua_activation_v1";
    public const string CustomerAi = "bbook_customer_ai_lifetime_v1";
    public const string Visibility = "bbook_visibility_30d_v1";
    public const string TermsVersion = "bbook-web-v2";
    public static MuaCommerceProduct Get(string productId) => productId switch
    {
        CustomerAi => new(CustomerAi, "AI quét mặt trọn đời", 20000, null),
        Visibility => new(Visibility, "BBook Visibility 30 ngày", 75000, 30),
        _ => throw new CommerceException("UNKNOWN_PRODUCT")
    };
}

public sealed class CommerceException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}
