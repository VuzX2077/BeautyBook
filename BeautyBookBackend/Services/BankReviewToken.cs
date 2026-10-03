using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BeautyBookBackend.Models;
namespace BeautyBookBackend.Services;
public static class BankReviewToken
{
    public static string For(BankAccount x) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
        x.Id, x.UserId, x.Method, x.BankCode, x.BankBin, x.BankName, x.AccountNumber,
        x.AccountHolderName, x.FinancialQrMediaId, x.IsActive, x.VerificationStatus, x.UpdatedAt
    }))));
}
