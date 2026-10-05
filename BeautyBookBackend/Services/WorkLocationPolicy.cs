using BeautyBookBackend.Models;
using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services;

public static class WorkLocationPolicy
{
    public const string CustomerAddress = "CUSTOMER_ADDRESS";
    public const string MuaWorkLocation = "MUA_WORK_LOCATION";
    public static bool ValidCoordinates(double? lat, double? lng) =>
        lat.HasValue == lng.HasValue && (!lat.HasValue ||
        (double.IsFinite(lat.Value) && double.IsFinite(lng!.Value) && lat >= -90 && lat <= 90 && lng >= -180 && lng <= 180 && !(lat == 0 && lng == 0)));
    public static bool CanVisit(MakeupArtistProfile profile) => profile.AllowCustomerVisit && !string.IsNullOrWhiteSpace(profile.WorkLocationAddress) && profile.WorkLocationAddress.Trim().Length <= 500;
    public static bool Valid(string? name, string? address, double? lat, double? lng, bool confirmed, bool visit) =>
        (name?.Trim().Length ?? 0) <= 100 && (address?.Trim().Length ?? 0) <= 500 && ValidCoordinates(lat, lng)
        && (!confirmed || lat.HasValue) && ((!visit && !lat.HasValue && string.IsNullOrWhiteSpace(name)) || !string.IsNullOrWhiteSpace(address));
    public static void Clear(MakeupArtistProfile profile)
    {
        profile.WorkLocationName = null; profile.WorkLocationAddress = null; profile.AllowCustomerVisit = false;
        profile.Latitude = null; profile.Longitude = null; profile.OperatingLocationConfirmed = false;
        profile.PublicMeetingPoint = false; profile.OperatingLocationLabel = null;
    }
    public static void Set(MakeupArtistProfile profile, string? name, string? address, double? lat, double? lng, bool confirmed, bool visit)
    {
        profile.WorkLocationName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        profile.WorkLocationAddress = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        profile.AllowCustomerVisit = visit; profile.Latitude = lat; profile.Longitude = lng;
        profile.OperatingLocationConfirmed = confirmed && lat.HasValue;
        // Legacy consent is never used to publish the new workplace.
        profile.PublicMeetingPoint = false; profile.OperatingLocationLabel = null;
    }
    public sealed record Destination(string Type, string? Name, string Address, decimal? Latitude, decimal? Longitude);
    public static Destination Resolve(BookingCreateDto request, MakeupArtistProfile profile)
    {
        var mode = request.ServiceLocationType ?? CustomerAddress;
        if (mode == MuaWorkLocation)
        {
            if (profile.MUAId != request.MUAId || !CanVisit(profile))
                throw new BookingRuleException("WORK_LOCATION_UNAVAILABLE", "MUA hiện không cho phép khách đến nơi làm việc. Vui lòng chọn địa điểm khác.", 409);
            if (!Valid(profile.WorkLocationName, profile.WorkLocationAddress, profile.Latitude, profile.Longitude, profile.OperatingLocationConfirmed, true))
                throw new BookingRuleException("WORK_LOCATION_UNAVAILABLE", "Nơi làm việc của MUA chưa có thông tin hợp lệ.", 409);
            return new(mode, profile.WorkLocationName, profile.WorkLocationAddress!.Trim(),
                profile.OperatingLocationConfirmed && profile.Latitude.HasValue ? (decimal?)profile.Latitude : null,
                profile.OperatingLocationConfirmed && profile.Longitude.HasValue ? (decimal?)profile.Longitude : null);
        }
        if (mode != CustomerAddress) throw new BookingRuleException("INVALID_SERVICE_LOCATION_TYPE", "Loại địa điểm không hợp lệ.");
        var address = (request.ServiceAddress ?? request.Address)?.Trim();
        if (string.IsNullOrWhiteSpace(address) || address.Length > 500 ||
            !ValidCoordinates((double?)request.ServiceLatitude, (double?)request.ServiceLongitude))
            throw new BookingRuleException("INVALID_SERVICE_LOCATION", "Vui lòng nhập địa chỉ hợp lệ; GPS là không bắt buộc.");
        return new(mode, null, address, request.ServiceLatitude, request.ServiceLongitude);
    }
}
