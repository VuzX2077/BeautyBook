using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BeautyBookBackend.Services;

public sealed record CanonicalStyleManifest(string Key, int? ExistingStyleId, string? ExpectedName, long? ExpectedVersion, bool CreateMissing = false);
public sealed record MakeupCatalogManifest(string SourceSha256, IReadOnlyList<CanonicalStyleManifest> Styles);
public sealed record CatalogImportReport(bool DryRun, Guid? BatchId, string SourceSha256, IReadOnlyList<string> Inserted, IReadOnlyList<string> Unchanged, IReadOnlyList<string> Conflicts);

// Operator-only service. Not called by startup or exposed through an HTTP API.
public sealed class MakeupCatalogImportService(ApplicationDbContext db)
{
    public const string SourceHash = "8F45E1B7DAA4E6F7BA097BDFCE536D7CAC56F1409E88D24669B7603DA85C710F";
    private static string Resource(string name)
    {
        using var stream = typeof(MakeupCatalogImportService).Assembly.GetManifestResourceStream($"BeautyBookBackend.Data.MakeupCatalog.{name}")!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    public static string ApprovedSource => Resource("catalog-v1.3.json");
    public static IReadOnlyList<string> CanonicalKeys => JsonNode.Parse(Resource("baseline-v1.2.json"))!["styles"]!.AsArray().Select(s => (string)s!["key"]!).ToList();
    private static JsonObject Validate(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > 2_000_000) throw new ArgumentException("Catalog source too large.");
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new ArgumentException("Invalid catalog JSON.");
        var approved = JsonNode.Parse(ApprovedSource)!;
        if ((string?)root["schemaVersion"] != "1.3" || (string?)root["language"] != "vi-VN" || (string?)root["catalogType"] != "makeup-style-library" ||
            !JsonNode.DeepEquals(root["styles"], JsonNode.Parse(Resource("baseline-v1.2.json"))!["styles"])) throw new ArgumentException("Schema or immutable 20-style baseline mismatch.");
        var taxonomy = root["tagTaxonomy"]?.AsObject() ?? throw new ArgumentException("Missing taxonomy.");
        foreach (var (field, count) in new[] { ("occasionGroups",6),("occasions",26),("intensities",3),("colorTones",15),("effectGroups",6),("effects",39) })
        {
            var rows = taxonomy[field]?.AsArray() ?? throw new ArgumentException($"Missing {field}.");
            var keys = rows.Select(r => (string?)r?["key"]).ToList();
            var expected = approved["tagTaxonomy"]![field]!.AsArray().Select(r => (string?)r!["key"]).ToHashSet();
            if (rows.Count != count || keys.Distinct().Count() != count || keys.Any(k => !MakeupCatalogService.ValidKey(k)) || !keys.ToHashSet().SetEquals(expected)) throw new ArgumentException($"Invalid keys/count: {field}.");
            foreach (var row in rows)
            {
                UnifiedStyleService.ValidateName((string?)row!["label"]);
                if (((string?)row["description"])?.Length > 2000) throw new ArgumentException("Taxonomy description too long.");
            }
        }
        // The initial v1.3 import is a pinned, reviewed artifact, not an editing API.
        if (!JsonNode.DeepEquals(root, approved)) throw new ArgumentException("Unreviewed v1.3 source change; edit labels through Admin APIs instead.");
        return root;
    }
    private void EnsureDisposable(string? confirmedDatabase)
    {
        if (db.Database.IsNpgsql())
        {
            var c = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
            if (c.Host is not ("localhost" or "127.0.0.1" or "::1") || c.Database != confirmedDatabase || c.Database?.StartsWith("beautybook_test_", StringComparison.Ordinal) != true)
                throw new InvalidOperationException("Import requires an explicitly confirmed local beautybook_test_* database.");
        }
        else
        {
            var c = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = db.Database.GetConnectionString() };
            var memory = (c.TryGetValue("Data Source", out var value) || c.TryGetValue("DataSource", out value)) && string.Equals(value as string, ":memory:", StringComparison.Ordinal);
            if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite" || !memory)
                throw new InvalidOperationException("Only disposable local PostgreSQL or in-memory SQLite tests are allowed.");
        }
    }
    public async Task<CatalogImportReport> Execute(string json, MakeupCatalogManifest? manifest, bool apply = false, string? confirmedDatabase = null, CancellationToken ct = default)
    {
        EnsureDisposable(confirmedDatabase);
        var root = Validate(json); var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var keys = CanonicalKeys;
        var inserted = new List<string>(); var unchanged = new List<string>(); var conflicts = new List<string>();
        if (manifest == null) conflicts.Add("Reviewed canonical key → existing StyleId/create-missing manifest is required.");
        else if (!string.Equals(manifest.SourceSha256, hash, StringComparison.OrdinalIgnoreCase) || manifest.Styles.Count != 20 || manifest.Styles.Select(x => x.Key).Distinct().Count() != 20 || !manifest.Styles.Select(x => x.Key).ToHashSet().SetEquals(keys))
            conflicts.Add("Manifest hash/20 canonical keys mismatch.");
        if (conflicts.Count > 0) return new(!apply, null, hash, inserted, unchanged, conflicts);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await new UnifiedStyleService(db).LockCatalog();
            var all = await db.MakeupStyles.ToListAsync(ct); var plan = new List<(CanonicalStyleManifest Mapping, MakeupStyle? Style, JsonNode Definition)>();
            foreach (var m in manifest!.Styles)
            {
                var definition = root["styles"]!.AsArray().Single(s => (string)s!["key"]! == m.Key)!;
                var keyed = all.SingleOrDefault(s => s.Key == m.Key);
                var mapped = m.ExistingStyleId.HasValue ? all.SingleOrDefault(s => s.StyleId == m.ExistingStyleId) : null;
                if (keyed != null)
                {
                    if (m.ExistingStyleId.HasValue && keyed.StyleId != m.ExistingStyleId || keyed.SystemDefinition == null || !JsonNode.DeepEquals(JsonNode.Parse(keyed.SystemDefinition), definition)) conflicts.Add($"{m.Key}: key/baseline conflicts with existing row.");
                    else { unchanged.Add($"style:{m.Key}"); plan.Add((m,keyed,definition)); }
                    continue;
                }
                if (m.ExistingStyleId.HasValue)
                {
                    if (mapped == null || mapped.Key != null || m.CreateMissing || m.ExpectedVersion == null || m.ExpectedVersion != mapped.Version || m.ExpectedName != mapped.Name)
                        conflicts.Add($"{m.Key}: existing ID/name/version changed or mapping is ambiguous.");
                    else { inserted.Add($"canonical-assignment:{m.Key}:{mapped.StyleId}"); plan.Add((m,mapped,definition)); }
                }
                else if (!m.CreateMissing || all.Any(s => UnifiedStyleService.NameKey(s.Name) == UnifiedStyleService.NameKey((string?)definition["displayName"]) || UnifiedStyleService.NameKey(s.Name) == UnifiedStyleService.NameKey((string?)definition["name"])))
                    conflicts.Add($"{m.Key}: missing reviewed ID or create-missing collides with existing name; no name inference.");
                else { inserted.Add($"style:{m.Key}"); plan.Add((m,null,definition)); }
            }
            if (manifest.Styles.Where(m => m.ExistingStyleId.HasValue).Select(m => m.ExistingStyleId).Distinct().Count() != manifest.Styles.Count(m => m.ExistingStyleId.HasValue)) conflicts.Add("A StyleId cannot represent two canonical keys.");
            var terms = await db.MakeupCatalogTerms.ToListAsync(ct);
            var termPlans = new List<(string Kind, JsonNode Row, int Order)>();
            foreach (var (field, kind) in new[] { ("occasionGroups","occasion-group"),("effectGroups","effect-group"),("intensities","intensity"),("colorTones","color-tone"),("occasions","occasion"),("effects","effect") })
            {
                var order = 0;
                foreach (var row in root["tagTaxonomy"]![field]!.AsArray())
                {
                    var key = (string)row!["key"]!; var existing = terms.SingleOrDefault(t => t.Kind == kind && t.Key == key);
                    if (existing != null) unchanged.Add($"term:{kind}:{key}");
                    else if (terms.Any(t => t.Kind == kind && t.NormalizedLabel == UnifiedStyleService.NameKey((string?)row["label"]))) conflicts.Add($"term:{kind}:{key}: label conflict.");
                    else inserted.Add($"term:{kind}:{key}");
                    termPlans.Add((kind,row,order++));
                }
            }
            if (conflicts.Count > 0 || !apply) { await tx.RollbackAsync(ct); db.ChangeTracker.Clear(); return new(!apply,null,hash,inserted,unchanged,conflicts); }
            var batch = Guid.NewGuid(); var catalog = new MakeupCatalogService(db);
            foreach (var (m, existing, definition) in plan)
            {
                if (existing?.Key == m.Key) continue; // Do not overwrite later admin edits, metadata or status.
                var style = existing ?? new MakeupStyle { Name=(string?)definition["displayName"], NormalizedName=UnifiedStyleService.NameKey((string?)definition["displayName"]), Description=(string?)definition["shortDescription"] };
                var before = existing == null ? null : new { style.StyleId, style.Key, style.Source, style.ReviewStatus, style.Version };
                style.Key=m.Key;style.SystemDefinition=definition.ToJsonString();
                if (existing == null) { style.Source="System";style.ReviewStatus="Approved";style.SortOrder=keys.ToList().IndexOf(m.Key);db.MakeupStyles.Add(style); }
                await db.SaveChangesAsync(ct);
                catalog.Log(null,"import-style",$"style:{style.StyleId}",before,new {style.StyleId,style.Key,style.Source,style.Version},hash,batch);
            }
            var added = new HashSet<MakeupCatalogTerm>();
            foreach (var (kind,row,order) in termPlans)
            {
                var key=(string)row["key"]!;if(terms.Any(t=>t.Kind==kind&&t.Key==key))continue;
                var term=new MakeupCatalogTerm{Kind=kind,Key=key,Label=(string)row["label"]!,NormalizedLabel=UnifiedStyleService.NameKey((string?)row["label"]),Description=(string?)row["description"],SortOrder=order};
                db.MakeupCatalogTerms.Add(term);terms.Add(term);added.Add(term);
            }
            await db.SaveChangesAsync(ct);
            foreach(var term in added)
            {
                var row=termPlans.Single(t=>t.Kind==term.Kind&&(string)t.Row["key"]! == term.Key).Row;
                if((string?)row["groupKey"] is {} group)term.GroupId=terms.Single(t=>t.Kind==term.Kind+"-group"&&t.Key==group).Id;
                if((string?)row["broaderOccasionKey"] is {} broader)term.BroaderTermId=terms.Single(t=>t.Kind=="occasion"&&t.Key==broader).Id;
            }
            await db.SaveChangesAsync(ct);
            foreach(var term in added)catalog.Log(null,"import-term",$"term:{term.Id}",null,MakeupCatalogService.Map(term),hash,batch);
            // Only initialize baseline metadata for rows newly assigned in this run.
            foreach(var (_,existing,definition) in plan.Where(p=>p.Style?.Key == null || inserted.Any(i=>i.StartsWith($"canonical-assignment:{p.Mapping.Key}:") || i==$"style:{p.Mapping.Key}")))
            {
                var style=await db.MakeupStyles.SingleAsync(s=>s.Key==(string)definition["key"]!,ct);
                if(existing!=null)continue; // Existing admin/legacy metadata is never inferred or overwritten.
                var intensity=(string)definition["intensity"]!;
                var occasions=definition["occasions"]!.AsArray().Select(o=>(string)o!).ToHashSet();
                foreach(var term in terms.Where(t=>t.Kind=="intensity"&&t.Key==intensity || t.Kind=="occasion"&&occasions.Contains(t.Key)))
                    db.MakeupStyleTermLinks.Add(new(){StyleId=style.StyleId,TermId=term.Id,Kind=term.Kind});
            }
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return new(false,batch,hash,inserted,unchanged,conflicts);
        }
        catch { await tx.RollbackAsync(ct); db.ChangeTracker.Clear(); throw; }
    }
}
