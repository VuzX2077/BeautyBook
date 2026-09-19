namespace BeautyBookBackend.Services;

public sealed class BookingTimeService
{
    private readonly TimeZoneInfo _businessTimeZone;

    public BookingTimeService(IConfiguration configuration)
        : this(ResolveTimeZone(configuration["Booking:TimeZoneId"]))
    {
    }

    public BookingTimeService(TimeZoneInfo businessTimeZone)
    {
        _businessTimeZone = businessTimeZone;
    }

    public DateTime ToUtc(DateTime bookingDate, TimeSpan bookingTime)
    {
        var localAppointment = DateTime.SpecifyKind(
            bookingDate.Date.Add(bookingTime),
            DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localAppointment, _businessTimeZone);
    }

    public DateTime NormalizeInstantToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), _businessTimeZone)
    };

    private static TimeZoneInfo ResolveTimeZone(string? configuredId)
    {
        var requested = string.IsNullOrWhiteSpace(configuredId) ? "Asia/Ho_Chi_Minh" : configuredId.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(requested);
        }
        catch (TimeZoneNotFoundException) when (requested == "Asia/Ho_Chi_Minh")
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
    }
}
