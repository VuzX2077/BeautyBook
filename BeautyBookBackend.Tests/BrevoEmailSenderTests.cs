using System.Net;
using System.Text.Json;
using BeautyBookBackend.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Tests;

public class BrevoEmailSenderTests : IDisposable
{
    private readonly List<BeautyBookBackend.Data.ApplicationDbContext> contexts = new();
    public void Dispose() { foreach (var context in contexts) context.Dispose(); }
    private BrevoEmailSender Sender(HttpMessageHandler handler, bool configured = true)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:BrevoApiKey"] = configured ? "test-key" : null,
            ["Email:FromEmail"] = "sender@example.com",
            ["Email:FromName"] = "BBook"
        }).Build();
        var db = new BeautyBookBackend.Data.ApplicationDbContext(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<BeautyBookBackend.Data.ApplicationDbContext>().UseSqlite("Data Source=:memory:").Options);
        db.Database.OpenConnection();
        db.Database.ExecuteSqlRaw("CREATE TABLE Users (Email TEXT, IsDemoAccount INTEGER NOT NULL DEFAULT 0); INSERT INTO Users VALUES ('recipient@example.com', 0)");
        contexts.Add(db);
        return new BrevoEmailSender(new HttpClient(handler), config, NullLogger<BrevoEmailSender>.Instance, db);
    }

    [Fact]
    public async Task SendsCorrectHttpsPayloadAndApiKey()
    {
        var handler = new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.brevo.com/v3/smtp/email", request.RequestUri!.ToString());
            Assert.Equal("test-key", request.Headers.GetValues("api-key").Single());
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("sender@example.com", json.RootElement.GetProperty("sender").GetProperty("email").GetString());
            Assert.Equal("recipient@example.com", json.RootElement.GetProperty("to")[0].GetProperty("email").GetString());
            Assert.Contains("123456", json.RootElement.GetProperty("textContent").GetString());
            Assert.Contains("đặt lại mật khẩu", json.RootElement.GetProperty("subject").GetString());
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        await Sender(handler).SendOtpAsync("recipient@example.com", "123456", "RESET_PASSWORD");
    }

    [Theory]
    [InlineData("BANK_ACCOUNT_ADD", "Mã OTP xác minh thêm tài khoản nhận tiền")]
    [InlineData("BANK_ACCOUNT_UPDATE", "Mã OTP xác minh thay đổi tài khoản nhận tiền")]
    [InlineData("BANK_ACCOUNT_SET_DEFAULT", "Mã OTP xác nhận đổi tài khoản nhận tiền mặc định")]
    public async Task BankOtpExplainsSensitiveReceivingDestinationChange(string purpose,string subject)
    {
        var handler=new Handler(async request=>{using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());Assert.Equal(subject,json.RootElement.GetProperty("subject").GetString());var text=json.RootElement.GetProperty("textContent").GetString();Assert.Contains("thay đổi nơi nhận tiền",text);Assert.Contains("Không chia sẻ OTP",text);Assert.Contains("đổi mật khẩu",text);return new HttpResponseMessage(HttpStatusCode.Created);});
        await Sender(handler).SendOtpAsync("recipient@example.com","123456",purpose);
    }

    [Theory]
    [InlineData(401, "{\"code\":\"unauthorized\"}")]
    [InlineData(429, "{\"code\":\"too_many_requests\"}")]
    [InlineData(500, "non-JSON upstream failure")]
    [InlineData(400, "[]")]
    public async Task RejectionsBecomeEmailDeliveryFailures(int status, string body)
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) }));
        await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(handler).SendOtpAsync("recipient@example.com", "123456", "REGISTER"));
    }

    [Fact]
    public async Task MissingConfigurationMakesNoNetworkRequest()
    {
        var handler = new Handler(_ => throw new Exception("Unexpected HTTP request"));
        await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(handler, false).SendOtpAsync("recipient@example.com", "123456", "REGISTER"));
    }

    [Fact]
    public async Task TimeoutBecomesEmailDeliveryFailure()
    {
        var handler = new Handler(_ => throw new TaskCanceledException());
        await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(handler).SendOtpAsync("recipient@example.com", "123456", "REGISTER"));
    }

    [Fact]
    public async Task DefaultOtpContainsOnlySafeActionAndCodeAndUnsupportedPurposeSendsNothing()
    {
        var calls = 0;
        var handler = new Handler(async request =>
        {
            calls++;
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            foreach (var field in new[] { "textContent", "htmlContent" })
            {
                var body = WebUtility.HtmlDecode(json.RootElement.GetProperty(field).GetString()!);
                Assert.Contains("123456", body);
                Assert.Contains("tài khoản nhận tiền mặc định", body);
                Assert.Contains("5 phút", body);
                Assert.DoesNotContain("AccountNumber", body);
                Assert.DoesNotContain("MoMo", body);
                Assert.DoesNotContain("<img", body);
                Assert.DoesNotContain("https://", body);
            }
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        var sender = Sender(handler);
        await sender.SendOtpAsync("recipient@example.com", "123456", "BANK_ACCOUNT_SET_DEFAULT");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sender.SendOtpAsync("recipient@example.com", "123456", "UNSUPPORTED"));
        Assert.Equal(1, calls);
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
