using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Tests;

public sealed class MuaApprovalContractTests
{
    [Fact]
    public void NewProfile_StartsAsDraftAndUnverified()
    {
        var profile = new MakeupArtistProfile();
        Assert.Equal(MuaStatus.Draft, profile.Status);
        Assert.Equal(MuaVerificationStatus.Draft, profile.VerificationStatus);
    }

    [Fact]
    public void ApprovalStates_HaveStableDatabaseValues()
    {
        Assert.Equal(0, (byte)MuaVerificationStatus.Draft);
        Assert.Equal(1, (byte)MuaVerificationStatus.PendingReview);
        Assert.Equal(2, (byte)MuaVerificationStatus.Approved);
        Assert.Equal(3, (byte)MuaVerificationStatus.Rejected);
    }
}
