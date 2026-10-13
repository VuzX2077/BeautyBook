using BeautyBookAdvisor.Api;
// Does not execute BeautyBookBackend.Program or register DbContext/migrations.
var builder=WebApplication.CreateBuilder(args);
AdvisorApiHost.Configure(builder);
var app=builder.Build();AdvisorApiHost.Map(app);
var port=Environment.GetEnvironmentVariable("PORT");
if(!string.IsNullOrWhiteSpace(port))app.Urls.Add($"http://0.0.0.0:{port}");
await app.RunAsync();
