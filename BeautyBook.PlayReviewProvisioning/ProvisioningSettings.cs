using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using BeautyBookBackend.Services;
using Npgsql;

namespace BeautyBook.PlayReviewProvisioning;

public sealed class ProvisioningSettings
{
    public string EnvironmentName { get; set; } = "";
    public string ExpectedEnvironment { get; set; } = "";
    public string ExpectedHost { get; set; } = "";
    public string ExpectedDatabase { get; set; } = "";
    public Guid ReviewUserId { get; set; }
    public Guid CounterpartUserId { get; set; }
    public Guid SampleBankAccountId { get; set; }
    public string ReviewEmail { get; set; } = "";
    public string CounterpartEmail { get; set; } = "";
    public string AssetsDirectory { get; set; } = "";
    public string BookingTimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";
    public string BankCode { get; set; } = "VCB";
    public string BankBin { get; set; } = "970436";
    public string LocationLabel { get; set; } = "Sample public meeting area — no appointment is actually held";
    public string OperatingAreaId { get; set; } = "";
    public int OperatingProvinceCode { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public Dictionary<string, string> AssetChecksums { get; set; } = new();
    public Dictionary<string, string> AssetFiles { get; set; } = new();
    public bool SampleAssetsAcknowledged { get; set; }

    public void CheckTarget(string connectionString, string command, bool apply, bool maintenance)
    {
        if (command is not ("validate" or "provision" or "refresh-scenarios" or "rotate-credential")) throw Error("COMMAND_INVALID");
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(EnvironmentName) || EnvironmentName != ExpectedEnvironment
            || string.IsNullOrWhiteSpace(ExpectedHost) || target.Host != ExpectedHost
            || string.IsNullOrWhiteSpace(ExpectedDatabase) || target.Database != ExpectedDatabase) throw Error("TARGET_MISMATCH");
        if (ReviewUserId == Guid.Empty || CounterpartUserId == Guid.Empty || SampleBankAccountId == Guid.Empty
            || new[] { ReviewUserId, CounterpartUserId, SampleBankAccountId }.Distinct().Count() != 3) throw Error("IDS_INVALID");
        try { _ = new MailAddress(ReviewEmail); _ = new MailAddress(CounterpartEmail); }
        catch { throw Error("EMAIL_INVALID"); }
        if (ReviewEmail.Trim().Equals(CounterpartEmail.Trim(), StringComparison.OrdinalIgnoreCase)) throw Error("EMAIL_COLLISION");
        // An acknowledgement is an operator attestation, never a detector of idle sessions.
        if (apply && (command == "validate" || !maintenance)) throw Error("MAINTENANCE_REQUIRED");
        if (!BankAccountService.IsKnownBankPair(BankCode, BankBin)) throw Error("BANK_MAPPING_INVALID");
        if (string.IsNullOrWhiteSpace(LocationLabel) || string.IsNullOrWhiteSpace(OperatingAreaId)
            || OperatingProvinceCode <= 0 || !double.IsFinite(Latitude) || !double.IsFinite(Longitude)
            || Latitude is < -90 or > 90 || Longitude is < -180 or > 180) throw Error("LOCATION_INVALID");
    }
    public Guid Id(string key) => StableId($"bbook-play-review-v1:{ReviewUserId:D}:{CounterpartUserId:D}:{key}");
    public static Guid StableId(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
    public static ProvisioningException Error(string code) => new(code);
}

public sealed class ProvisioningException(string code) : InvalidOperationException(code);
