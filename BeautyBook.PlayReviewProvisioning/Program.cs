using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BeautyBook.PlayReviewProvisioning;
using BeautyBookBackend.Data;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

try
{
    if (args.Length == 1 && args[0] == "--help")
    {
        Console.WriteLine("<validate|provision|refresh-scenarios|rotate-credential> --config <file> --expect-env <name> --expect-host <host> --expect-db <database> [--assets <directory>] [--apply --maintenance-ack STOPPED_AND_DRAINED]");
        return 0;
    }
    if (args.Length == 0) throw ProvisioningSettings.Error("COMMAND_REQUIRED");
    var options = new Dictionary<string, string>();
    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] == "--apply") { if (!options.TryAdd(args[i], "true")) throw ProvisioningSettings.Error("ARGUMENT_INVALID"); continue; }
        if (args[i] is not ("--config" or "--expect-env" or "--expect-host" or "--expect-db" or "--assets" or "--maintenance-ack") || i + 1 == args.Length
            || !options.TryAdd(args[i], args[++i])) throw ProvisioningSettings.Error("ARGUMENT_INVALID");
    }
    string Required(string key) => options.TryGetValue(key, out var value) ? value : throw ProvisioningSettings.Error("ARGUMENT_REQUIRED");
    var settings = JsonSerializer.Deserialize<ProvisioningSettings>(File.ReadAllText(Required("--config")), new JsonSerializerOptions {
        PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw ProvisioningSettings.Error("CONFIG_INVALID");
    settings.ExpectedEnvironment = Required("--expect-env"); settings.ExpectedHost = Required("--expect-host"); settings.ExpectedDatabase = Required("--expect-db");
    if (options.TryGetValue("--assets", out var assets)) settings.AssetsDirectory = assets;
    var config = new ConfigurationBuilder().AddEnvironmentVariables("BBOOK_").Build();
    var connection = config.GetConnectionString("Provisioning") ?? throw ProvisioningSettings.Error("CONNECTION_REQUIRED");
    var apply = options.ContainsKey("--apply");
    var maintenance = options.GetValueOrDefault("--maintenance-ack") == "STOPPED_AND_DRAINED";
    settings.CheckTarget(connection, args[0], apply, maintenance);
    await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options);
    using var privateHttp = new HttpClient(); using var publicHttp = new HttpClient();
    var privateStorage = new SupabaseVerificationStorage(privateHttp, config);
    var publicStorage = new SupabaseImageStorage(publicHttp, config, db);
    var provisioner = new ReviewProvisioner(db, settings, new SampleMediaProvisioner(db, privateStorage, publicStorage));
    Console.WriteLine(await provisioner.RunAsync(args[0], apply, maintenance, ReadSecret));
    return 0;
}
catch (ProvisioningException error) { Console.Error.WriteLine(error.Message); return 2; }
catch { Console.Error.WriteLine("PROVISIONING_FAILED: no diagnostic secrets are printed. Inspect the target safely before retrying."); return 2; }

static string ReadSecret()
{
    if (Console.IsInputRedirected) throw ProvisioningSettings.Error("INTERACTIVE_SECRET_INPUT_REQUIRED");
    Console.Write("Review password (hidden, 12–128 characters): ");
    var buffer = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Escape) throw ProvisioningSettings.Error("SECURE_INPUT_CANCELLED");
        if (key.Key == ConsoleKey.Backspace) { if (buffer.Length > 0) buffer.Length--; continue; }
        if (!char.IsControl(key.KeyChar)) { if (buffer.Length == 128) throw ProvisioningSettings.Error("CREDENTIAL_LENGTH_INVALID"); buffer.Append(key.KeyChar); }
    }
    Console.WriteLine(); return buffer.ToString();
}
