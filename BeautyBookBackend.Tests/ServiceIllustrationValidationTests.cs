using System.ComponentModel.DataAnnotations;
using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Tests;

public class ServiceIllustrationValidationTests
{
    private static bool Valid(List<string> urls)
    {
        var request = new ServiceCreateDto { ServiceName = "Cô dâu", Price = 1500000, DurationMinutes = 180, ImageUrls = urls };
        return Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), true);
    }
    [Fact] public void AcceptsFiveIllustrations() => Assert.True(Valid(Enumerable.Range(1, 5).Select(index => $"https://example.com/{index}.jpg").ToList()));
    [Fact] public void RejectsSixIllustrations() => Assert.False(Valid(Enumerable.Range(1, 6).Select(index => $"https://example.com/{index}.jpg").ToList()));
    [Fact] public void RejectsDeviceUris() => Assert.False(Valid(new() { "file:///picture.jpg" }));
}
