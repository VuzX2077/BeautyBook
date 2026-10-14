using BeautyBookBackend.Services;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;
using Xunit;

public sealed class MakeupProfileTests
{
    [Fact] public void ProfilesDifferWithMeasuredInputsAndUnknownStaysUnknown() {
        var first=FeatureAdvisorProvider.SyntheticFixture();
        var second=first with {Features=new(first.Features){["eyeSpacing"]=.6,["outlineAspect"]=1.6,["mouthWidth"]=.5}};
        var a=MakeupProfileConverter.From(first);var b=MakeupProfileConverter.From(second);
        Assert.True(MakeupProfileConverter.Valid(a));Assert.True(MakeupProfileConverter.Valid(b));
        Assert.NotEqual(a.Regions["eyes"].Signals[0],b.Regions["eyes"].Signals[0]);
        Assert.NotEqual(a.Regions["cheeks"].Signals[0],b.Regions["cheeks"].Signals[0]);
        Assert.NotEqual(a.Regions["lips"].Signals[0],b.Regions["lips"].Signals[0]);
        var absent=MakeupProfileConverter.From(first with {Features=new()});
        Assert.Equal("unknown",absent.EvidenceStatus);Assert.All(absent.Regions.Values,r=>Assert.Equal("unknown",r.Evidence));
    }
    [Fact] public void ProfileContainsNoDetailedGeometryOrIdentityAndExpressionOmitsLips() {
        var s=FeatureAdvisorProvider.SyntheticFixture();
        var profile=MakeupProfileConverter.From(s with {Quality=s.Quality with {ExpressionLimited=true}});
        Assert.Empty(profile.Regions["lips"].Signals);
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(FeatureAdvisorDefinitions.Prompt(s),FeatureAdvisorContract.Json));
        Assert.Equal(["makeupProfile","supportedStyles"],json.RootElement.EnumerateObject().Select(p=>p.Name).ToArray());
        var text=json.RootElement.ToString();
        foreach(var key in new[]{"boundingBox","pose","measurements","points","triangles","embedding","base64","userId","requestId","confidence"})Assert.DoesNotContain('"'+key+'"',text);
        Assert.Equal(1,json.RootElement.GetProperty("makeupProfile").GetProperty("schemaVersion").GetInt32());
    }
    [Fact] public void GlobalSlotsDoNotQueueAndLeaseReleaseIsIdempotent() {
        using var gate=new FeatureAdvisorConcurrency(Options.Create(new FeatureAdvisorOptions{MaxConcurrentRequests=1}));
        var lease=gate.TryEnter(default);Assert.NotNull(lease);Assert.Null(gate.TryEnter(default));
        lease!.Dispose();lease.Dispose();using var next=gate.TryEnter(default);Assert.NotNull(next);
    }
    sealed class BusyHandler:HttpMessageHandler {
        public TaskCompletionSource<bool> Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){Started.TrySetResult(true);await Task.Delay(Timeout.Infinite,ct);return new(HttpStatusCode.ServiceUnavailable);}
    }
    [Fact] public async Task ProviderCancellationReleasesSharedSlotsAcrossProviderInstances() {
        var options=Options.Create(new FeatureAdvisorOptions{SyntheticDiagnosticsEnabled=true,ApiKey="synthetic-test-key",Model="gemini-3.5-flash-lite",MaxConcurrentRequests=1});
        using var gate=new FeatureAdvisorConcurrency(options);var handler=new BusyHandler();using var http=new HttpClient(handler);
        var first=new FeatureAdvisorProvider(http,options,gate);var second=new FeatureAdvisorProvider(http,options,gate);using var stop=new CancellationTokenSource();
        var pending=first.DiagnoseSynthetic(stop.Token);await handler.Started.Task;
        Assert.Equal("PROVIDER_BUSY",(await second.DiagnoseSynthetic(default)).Code);
        stop.Cancel();Assert.Equal("CANCELLED_OR_TIMEOUT",(await pending).Code);
        using var recovered=gate.TryEnter(default);Assert.NotNull(recovered);
    }
}
