using Microsoft.Extensions.Configuration;
using BeautyBookBackend.Services;
namespace BeautyBookBackend.Tests;
public class MomoPhoneTests {
 [Theory][InlineData("0300000000")][InlineData("84300000000")][InlineData("+84300000000")]
 public void Vietnamese_formats_have_one_identity(string input){Assert.Equal("0300000000",MomoPhone.Normalize(input));}
 [Theory][InlineData("+15555555555")][InlineData("0000000000")][InlineData("849123456789")][InlineData("8300000000")]
 public void Invalid_or_other_international_numbers_are_not_rewritten(string input){Assert.Throws<BookingRuleException>(()=>MomoPhone.Normalize(input));}
 [Fact] public void Decoder_returns_canonical_phone_but_never_invents_opaque_phone(){Assert.Equal("0300000000",BankQrDecoder.ParsePayload("momo://receive?phone=84300000000").AccountNumber);Assert.Null(BankQrDecoder.ParsePayload("momo://receive?receiver=0000000000000000000").AccountNumber);}
 [Theory][InlineData(null)][InlineData("verification-private")][InlineData("images")]
 public void Financial_storage_configuration_never_falls_back_to_identity_or_public(string? bucket){var config=new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Supabase:Url","https://storage.example.test"},{"Supabase:ServiceRoleKey","synthetic-only"},{"Supabase:FinancialBucket",bucket??""},{"Supabase:VerificationBucket","verification-private"},{"Supabase:StorageBucket","images"}}).Build();var storage=new SupabaseFinancialStorage(new HttpClient(),config);Assert.Throws<InvalidOperationException>(()=>storage.LocationId);}
}
