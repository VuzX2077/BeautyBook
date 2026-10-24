using System.Text.Json;
using BeautyBookBackend.Data;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// Deliberately does not read appsettings or migrate any database.
var envName = args.SkipWhile(x => x != "--connection-env").Skip(1).FirstOrDefault()
    ?? throw new ArgumentException("Specify --connection-env ENV_NAME; connection strings are never command-line arguments.");
var connection = Environment.GetEnvironmentVariable(envName) ?? throw new ArgumentException("Connection environment variable is missing.");
var settings = new NpgsqlConnectionStringBuilder(connection);
var apply = args.Contains("--apply");
if (args.Contains("--catalog-import"))
{
    var confirmDb = args.SkipWhile(x => x != "--confirm-database").Skip(1).FirstOrDefault();
    if (settings.Host is not ("127.0.0.1" or "localhost" or "::1") || confirmDb != settings.Database || settings.Database?.StartsWith("beautybook_test_", StringComparison.Ordinal) != true)
        throw new ArgumentException("Catalog dry-run/apply only targets an explicitly confirmed local beautybook_test_* database.");
    await using var catalogDb = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options);
    var sourcePath = args.SkipWhile(x => x != "--source").Skip(1).FirstOrDefault();
    var json = sourcePath == null ? MakeupCatalogImportService.ApprovedSource : await File.ReadAllTextAsync(sourcePath);
    var manifestPath = args.SkipWhile(x => x != "--manifest").Skip(1).FirstOrDefault();
    var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
    var reviewed = manifestPath == null ? null : JsonSerializer.Deserialize<MakeupCatalogManifest>(await File.ReadAllTextAsync(manifestPath), jsonOptions);
    var importer = new MakeupCatalogImportService(catalogDb);
    var dryRun = await importer.Execute(json, reviewed, confirmedDatabase: confirmDb);
    Console.WriteLine(JsonSerializer.Serialize(dryRun, jsonOptions));
    if (dryRun.Conflicts.Count > 0) { Environment.ExitCode = 2; return; }
    if (apply) Console.WriteLine(JsonSerializer.Serialize(await importer.Execute(json, reviewed, true, confirmDb), jsonOptions));
    return;
}
if (apply)
{
    var confirm = args.SkipWhile(x => x != "--confirm-database").Skip(1).FirstOrDefault();
    if (settings.Host is not ("127.0.0.1" or "localhost") || confirm != settings.Database || settings.Database?.StartsWith("beautybook_test_", StringComparison.Ordinal) != true)
        throw new ArgumentException("This Phase 1 utility only applies reviewed mappings to explicitly confirmed local beautybook_test_* databases.");
}
await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options);
var maintenance = new UnifiedStyleMaintenance(db);
var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
if (apply)
{
    var path = args.SkipWhile(x => x != "--manifest").Skip(1).FirstOrDefault() ?? throw new ArgumentException("--manifest is required for apply.");
    var manifest = JsonSerializer.Deserialize<ReviewedManifest>(await File.ReadAllTextAsync(path), options) ?? throw new ArgumentException("Invalid manifest.");
    await maintenance.Apply(manifest.Names, manifest.Mappings);
}
Console.WriteLine(JsonSerializer.Serialize(await maintenance.Audit(), options));
record ReviewedManifest(List<ReviewedStyleName> Names, List<LegacyStyleMapping> Mappings);
