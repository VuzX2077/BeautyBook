using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Infrastructure;

public static class DatabaseReadiness
{
    public static async Task<IResult> CheckAsync(ApplicationDbContext db, HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1", timeout.Token);
            return Results.Ok(new { Status = "Healthy", Database = "Connected" });
        }
        catch (Exception)
        {
            // Do not expose the host, credentials, or provider exception publicly.
            return Results.Json(new { Status = "Unhealthy", Database = "Unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
