using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

public sealed class MakeupStyleConflictException() : Exception("Tên phong cách đã tồn tại. Hãy chọn tên khác.");
public sealed class AdminMakeupStyleService(ApplicationDbContext db)
{
    public static string NormalizeName(string? name) => UnifiedStyleService.DisplayName(name);
    public static string? Validate(AdminMakeupStyleWriteRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return "Nhập tên phong cách.";
        if (r.Name.Length > 100 || NormalizeName(r.Name).Length > 100 || r.Name.Any(char.IsControl) && r.Name.Any(c => char.IsControl(c) && !char.IsWhiteSpace(c))) return "Tên phong cách không hợp lệ hoặc dài hơn 100 ký tự.";
        if (r.Description?.Length > 255) return "Mô tả tối đa 255 ký tự.";
        if (r.Key != null && !MakeupCatalogService.ValidKey(r.Key)) return "Key không hợp lệ.";
        if (r.SortOrder is < 0 or > 100000 || r.ExpectedVersion is < 1) return "Thứ tự hoặc phiên bản không hợp lệ.";
        return null;
    }
    private static AdminMakeupStyleDto Map(MakeupStyle s, IReadOnlyList<CatalogTermDto>? metadata = null) => new(s.StyleId,s.Name,s.Description,s.IsActive,s.CreatedAt,s.Key,s.SortOrder,s.Source,s.ReviewStatus,s.ArchivedAt,s.Version,metadata);
    private async Task<AdminMakeupStyleDto> DetailOf(MakeupStyle style) => Map(style, (await db.MakeupStyleTermLinks.AsNoTracking().Where(x => x.StyleId == style.StyleId).Include(x => x.Term).OrderBy(x => x.TermId).ToListAsync()).Select(x => MakeupCatalogService.Map(x.Term)).ToList());
    public async Task<AdminMakeupStylePage> List(int page, int pageSize, string? search, string status, CancellationToken ct = default, string? source = null, string? reviewStatus = null)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page-1)*pageSize > int.MaxValue || !new[]{"all","active","inactive","archived"}.Contains(status) || source != null && !new[]{"System","Admin","MUA","LegacyUnknown"}.Contains(source) || reviewStatus != null && !new[]{"Pending","Approved","Rejected","NotApplicable"}.Contains(reviewStatus)) throw new ArgumentException("Trang hoặc bộ lọc không hợp lệ.");
        var q=db.MakeupStyles.AsNoTracking();
        if(status=="active") q=q.Where(x=>x.IsActive && x.ArchivedAt==null);
        if(status=="inactive") q=q.Where(x=>!x.IsActive && x.ArchivedAt==null);
        if(status=="archived") q=q.Where(x=>x.ArchivedAt!=null);
        if(source!=null) q=q.Where(x=>x.Source==source);
        if(reviewStatus!=null) q=q.Where(x=>x.ReviewStatus==reviewStatus);
        var term=search?.Trim().ToLower(); if(!string.IsNullOrEmpty(term)) q=q.Where(x=>x.Name!=null && x.Name.ToLower().Contains(term) || x.Key!=null && x.Key.Contains(term));
        var total=await q.CountAsync(ct);
        var rows=await q.OrderBy(x=>x.SortOrder).ThenBy(x=>x.Name).ThenBy(x=>x.StyleId).Skip((page-1)*pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows.Select(x=>Map(x)).ToList(),total,page,pageSize);
    }
    public async Task<AdminMakeupStyleDto?> Detail(int id,CancellationToken ct=default) => await db.MakeupStyles.AsNoTracking().SingleOrDefaultAsync(x=>x.StyleId==id,ct) is {} s ? await DetailOf(s) : null;
    public async Task<AdminMakeupStyleDto?> Save(int? id,AdminMakeupStyleWriteRequest r,Guid actor,CancellationToken ct=default)
    {
        if(Validate(r) is {} error) throw new ArgumentException(error);
        if(!id.HasValue && r.Key != null && MakeupCatalogImportService.CanonicalKeys.Contains(r.Key))
            MakeupCatalogService.Fail("CATALOG_KEY_RESERVED","Key phong cách chuẩn chỉ được gán bằng reviewed import.");
        return await new UnifiedStyleService(db).Write(actor,async()=>{
            var catalog=new MakeupCatalogService(db); await catalog.EnsureAdmin(actor);
            var style=id.HasValue?await db.MakeupStyles.SingleOrDefaultAsync(x=>x.StyleId==id,ct):new MakeupStyle { Source="Admin",CreatedByUserId=actor,ReviewStatus="Approved",Key=r.Key??$"style-{Guid.NewGuid():N}" };
            if(style==null) return null;
            MakeupCatalogService.CheckVersion(style.Version,r.ExpectedVersion);
            if(id.HasValue && r.Key!=null && r.Key!=style.Key) MakeupCatalogService.Fail("CATALOG_KEY_IMMUTABLE","Không được đổi key phong cách.");
            if(style.Key!=null && await db.MakeupStyles.AnyAsync(x=>x.StyleId!=style.StyleId && x.Key==style.Key,ct)) MakeupCatalogService.Fail("CATALOG_DUPLICATE","Key đã tồn tại.",409);
            var name=NormalizeName(r.Name);
            if(await new UnifiedStyleService(db).FindName(name,id)!=null) throw new MakeupStyleConflictException();
            var before=id.HasValue?await DetailOf(style):null;
            if(id.HasValue && style.Name!=name){
                var oldKey=UnifiedStyleService.NameKey(style.Name);
                var services=await db.Services.Where(x=>db.ServiceStyles.Any(l=>l.ServiceId==x.ServiceId&&l.StyleId==id)).ToListAsync(ct);
                foreach(var service in services) service.Tags=service.Tags.Select(x=>UnifiedStyleService.NameKey(x)==oldKey?name:x).Distinct().ToList();
            }
            style.Name=name;style.NormalizedName=UnifiedStyleService.NameKey(name);style.Description=r.Description?.Trim();if(r.SortOrder.HasValue)style.SortOrder=r.SortOrder.Value;
            style.UpdatedAt=DateTime.UtcNow;
            if(!id.HasValue)db.MakeupStyles.Add(style);
            await db.SaveChangesAsync(ct);
            if(r.MetadataTermIds!=null)await catalog.ReplaceMetadata(style,r.MetadataTermIds);
            await db.SaveChangesAsync(ct);
            var result=await DetailOf(style);catalog.Log(actor,id.HasValue?"style-update":"style-create",$"style:{style.StyleId}",before,result);await db.SaveChangesAsync(ct);return result;
        });
    }
    public Task<AdminMakeupStyleDto?> SetStatus(int id,bool active,Guid actor,CancellationToken ct=default,long? expectedVersion=null) => Lifecycle(id,actor,active,false,null,expectedVersion,null,ct);
    public Task<AdminMakeupStyleDto?> Archive(int id,Guid actor,CatalogLifecycleRequest r,CancellationToken ct) => Lifecycle(id,actor,false,true,null,r.ExpectedVersion,r.Reason,ct);
    public Task<AdminMakeupStyleDto?> Review(int id,Guid actor,CatalogReviewRequest r,CancellationToken ct) => Lifecycle(id,actor,null,false,r.Status,r.ExpectedVersion,r.Reason,ct);
    private Task<AdminMakeupStyleDto?> Lifecycle(int id,Guid actor,bool? active,bool archive,string? review,long? expected,string? reason,CancellationToken ct) => new UnifiedStyleService(db).Write<AdminMakeupStyleDto?>(actor,async()=>{
        var catalog=new MakeupCatalogService(db);await catalog.EnsureAdmin(actor);
        var style=await db.MakeupStyles.SingleOrDefaultAsync(x=>x.StyleId==id,ct);if(style==null)return null;
        MakeupCatalogService.CheckVersion(style.Version,expected);var before=await DetailOf(style);
        if(review!=null){
            if(style.Source!="MUA" || style.ArchivedAt!=null || review is not ("Approved" or "Rejected") || string.IsNullOrWhiteSpace(reason) || reason.Length>500) MakeupCatalogService.Fail("CATALOG_REVIEW_INVALID","Chỉ kiểm duyệt phong cách MUA hợp lệ; cần lý do.");
            style.ReviewStatus=review;if(review=="Rejected")style.IsActive=false;
        }else{
            if(active==true && (style.ArchivedAt!=null || style.ReviewStatus=="Rejected")) MakeupCatalogService.Fail("CATALOG_ARCHIVED","Không được kích hoạt mục đã lưu trữ hoặc bị từ chối.",409);
            style.IsActive=active??false;if(archive)style.ArchivedAt??=DateTime.UtcNow;
        }
        style.UpdatedAt=DateTime.UtcNow;await db.SaveChangesAsync(ct);
        var after=await DetailOf(style);catalog.Log(actor,archive?"style-archive":review!=null?"style-review":"style-status",$"style:{id}",before,new{Item=after,Reason=reason});await db.SaveChangesAsync(ct);return after;
    });
}
