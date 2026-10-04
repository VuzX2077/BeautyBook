using BeautyBook.StorageVerification;
using Microsoft.Extensions.Configuration;

namespace BeautyBook.StorageVerification;

internal static class VerificationEntryPoint
{
    public static async Task<int> Main(string[] args)
    {
        // No command-line credentials, app host, database or provisioning services.
        if (args.Length != 0) { Console.WriteLine("result: FAIL_ARGUMENTS"); return 2; }
        try
        {
            var values = new Dictionary<string, string?>();
            foreach (var (name, fallback) in new[] {
                ("Url", "SUPABASE_URL"), ("ServiceRoleKey", "SUPABASE_SERVICE_ROLE_KEY"),
                ("StorageBucket", "SUPABASE_STORAGE_BUCKET"), ("VerificationBucket", "SUPABASE_VERIFICATION_BUCKET") })
                values["Supabase:" + name] = Environment.GetEnvironmentVariable("BBOOK_Supabase__" + name)
                    ?? Environment.GetEnvironmentVariable(fallback);
            var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            using var transport = StorageVerifier.CreateTransport();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            return await StorageVerifier.RunAsync(config, transport, Console.Out, timeout.Token);
        }
        catch
        {
            Console.WriteLine("result: FAIL_CONFIGURATION_OR_TRANSPORT");
            return 2;
        }
    }
}
