namespace BeautyBookBackend.Services;

public sealed record MakeupRegion(string Evidence, string[] Signals, string[] SourceFeatures);
public sealed record MakeupProfile(int SchemaVersion, int SourceSummaryVersion, string EvidenceStatus,
    Dictionary<string, MakeupRegion> Regions, string[] Unknown, string[] Limitations);

public static class MakeupProfileConverter
{
    private static readonly Dictionary<string,string[]> Allowed = new() {
        ["eyes"]=["lower_spacing_projection","higher_spacing_projection","lower_eye_opening_projection","moderate_eye_opening_projection"],
        ["brows"]=["more_curved_brow_projection","gentler_brow_projection"],
        ["cheeks"]=["longer_outline_projection","wider_outline_projection","balanced_outline_projection"],
        ["lips"]=["wider_lip_projection","compact_lip_projection","greater_lip_height_projection","lower_lip_height_projection"]
    };
    public static bool Valid(MakeupProfile p) => p.SchemaVersion==1&&p.SourceSummaryVersion==3&&
        p.EvidenceStatus is "unknown" or "experimental_2d"&&p.Regions.Count==4&&p.Regions.All(r=>
            Allowed.TryGetValue(r.Key,out var signals)&&r.Value.Evidence==(r.Value.Signals.Length==0?"unknown":"experimental_2d")&&
            r.Value.Signals.Length<=2&&r.Value.Signals.Distinct().Count()==r.Value.Signals.Length&&r.Value.Signals.All(signals.Contains)&&
            r.Value.SourceFeatures.Length<=3&&r.Value.SourceFeatures.All(FeatureAdvisorContract.FeatureNames.Contains));
    // Experimental presentation buckets, NOT calibrated appearance classifiers.
    // Never attach a fabricated confidence percentage or infer a face shape.
    public static MakeupProfile From(FaceFeatureSummaryV3 s)
    {
        var regions=new Dictionary<string,MakeupRegion>();
        void Region(string name, Action<List<string>,List<string>> build) {
            var signals=new List<string>();var used=new List<string>();build(signals,used);
            regions[name]=new(signals.Count==0?"unknown":"experimental_2d",signals.ToArray(),used.Distinct().ToArray());
        }
        bool Value(string key,out double value) => s.Features.TryGetValue(key,out value)&&double.IsFinite(value);
        Region("eyes",(signals,used)=>{
            if(Value("eyeSpacing",out var spacing)){signals.Add(spacing<.46?"lower_spacing_projection":"higher_spacing_projection");used.Add("eyeSpacing");}
            if(Value("leftEyeAspect",out var l)&&Value("rightEyeAspect",out var r)){signals.Add(Math.Min(l,r)<.25?"lower_eye_opening_projection":"moderate_eye_opening_projection");used.AddRange(["leftEyeAspect","rightEyeAspect"]);}
        });
        Region("brows",(signals,used)=>{
            if(Value("leftBrowArch",out var l)&&Value("rightBrowArch",out var r)){signals.Add((l+r)/2>=.08?"more_curved_brow_projection":"gentler_brow_projection");used.AddRange(["leftBrowArch","rightBrowArch"]);}
        });
        Region("cheeks",(signals,used)=>{
            if(Value("outlineAspect",out var ratio)){signals.Add(ratio>=1.4?"longer_outline_projection":ratio<=1.1?"wider_outline_projection":"balanced_outline_projection");used.Add("outlineAspect");}
        });
        Region("lips",(signals,used)=>{
            if(s.Quality.ExpressionLimited)return;
            if(Value("mouthWidth",out var width)){signals.Add(width>=.36?"wider_lip_projection":"compact_lip_projection");used.Add("mouthWidth");}
            if(Value("mouthAspect",out var aspect)){signals.Add(aspect>=.3?"greater_lip_height_projection":"lower_lip_height_projection");used.Add("mouthAspect");}
        });
        return new(1,3,regions.Values.Any(r=>r.Signals.Length>0)?"experimental_2d":"unknown",regions,
            ["undertone","skinType","faceShape","anatomicalDepth"],
            ["Projection signals are experimental, uncalibrated and may be unknown; they are not anatomy or identity.","Coarsening does not establish anonymity or permission to use Unpaid Services."]);
    }
}
