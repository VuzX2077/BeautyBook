using BeautyBook.StorageDiagnostics;
using Microsoft.Extensions.Configuration;

// No arguments, app host, database configuration or provisioning services.
if (args.Length != 0) return 2;
try
{
    var values = new Dictionary<string, string?>();
    foreach (var (name, fallback) in new[] {
        ("Url", "SUPABASE_URL"), ("ServiceRoleKey", "SUPABASE_SERVICE_ROLE_KEY"),
        ("StorageBucket", "SUPABASE_STORAGE_BUCKET"), ("VerificationBucket", "SUPABASE_VERIFICATION_BUCKET") })
        values["Supabase:" + name] = Environment.GetEnvironmentVariable("BBOOK_Supabase__" + name)
            ?? Environment.GetEnvironmentVariable(fallback);
    var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    using var transport = ReadOnlyStorageDiagnostic.CreateTransport();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    return await ReadOnlyStorageDiagnostic.RunAsync(config, transport, Console.Out, timeout.Token);
}
catch
{
    Console.WriteLine("result: STOP_CONFIGURATION_OR_TRANSPORT_FAILURE");
    return 2;
}
