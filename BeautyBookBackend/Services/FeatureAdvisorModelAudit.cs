using System.Net;
using System.Text.Json;

namespace BeautyBookBackend.Services;

public sealed record ModelAudit(string Code, string[] AccessibleModels, bool SelectedModelAvailable, int? InputTokenLimit, int? OutputTokenLimit);
public static class FeatureAdvisorModelAudit
{
    public static async Task<ModelAudit> Check(HttpClient client, FeatureAdvisorOptions config, CancellationToken ct)
    {
        if (!FeatureAdvisorProvider.ValidKey(config.ApiKey)) return new("KEY_MISSING",[],false,null,null);
        var models = new HashSet<string>(); int? input=null, output=null; bool available=false;
        string? token=null;
        try {
            // Pagination token is provider-generated and escaped; credentials
            // are only in a header. Do not print provider error bodies.
            for(var page=0;page<20;page++) {
                using var request=new HttpRequestMessage(HttpMethod.Get,"https://generativelanguage.googleapis.com/v1beta/models?pageSize=100"+(token is null ? "" : "&pageToken="+Uri.EscapeDataString(token)));
                request.Headers.Add("x-goog-api-key",config.ApiKey);
                using var response=await client.SendAsync(request,ct);
                if (!response.IsSuccessStatusCode) return new($"HTTP_{(int)response.StatusCode}",[],false,null,null);
                var bytes=await response.Content.ReadAsByteArrayAsync(ct);
                if(bytes.Length>1048576) return new("INVALID_MODELS_RESPONSE",[],false,null,null);
                using var body=JsonDocument.Parse(bytes);
                foreach(var model in body.RootElement.GetProperty("models").EnumerateArray()) {
                    var name=model.GetProperty("name").GetString();
                    if(name is null || !name.StartsWith("models/gemini-",StringComparison.Ordinal)) continue;
                    if(!model.TryGetProperty("supportedGenerationMethods",out var methods)||!methods.EnumerateArray().Any(x=>x.GetString()=="generateContent")) continue;
                    models.Add(name[7..]);
                    if(name=="models/"+config.Model) {
                        available=true;
                        input=model.TryGetProperty("inputTokenLimit",out var i)&&i.TryGetInt32(out var iv)?iv:null;
                        output=model.TryGetProperty("outputTokenLimit",out var o)&&o.TryGetInt32(out var ov)?ov:null;
                    }
                }
                token=body.RootElement.TryGetProperty("nextPageToken",out var next)?next.GetString():null;
                if(string.IsNullOrEmpty(token))return new("OK",models.Order().ToArray(),available,input,output);
            }
            return new("MODEL_LIST_TRUNCATED",models.Order().ToArray(),available,input,output);
        }
        catch(Exception e) when(e is HttpRequestException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException) {
            return new("NETWORK_OR_INVALID_MODELS_RESPONSE",[],false,null,null);
        }
    }
}
