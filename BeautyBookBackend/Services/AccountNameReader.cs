using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace BeautyBookBackend.Services;

public interface IAccountNameReader
{
    Task<string?> ReadAsync(byte[] image, string? accountNumber, CancellationToken ct);
}

// OCR stays on this server. Images flow through pipes, without temporary files or third-party APIs.
public sealed class AccountNameReader : IAccountNameReader
{
    private static readonly SemaphoreSlim Slots = new(2);

    public async Task<string?> ReadAsync(byte[] image, string? accountNumber, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var acquired = false;
        Process? process = null;
        try
        {
            await Slots.WaitAsync(timeout.Token); acquired = true;
            using var bitmap = SKBitmap.Decode(image);
            if (bitmap == null) return null;
            var scale = Math.Min(3d, Math.Min(1600d / bitmap.Width, 4096d / bitmap.Height));
            using var resized = Math.Abs(scale - 1) > 0.001 ? bitmap.Resize(new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale))), new SKSamplingOptions(SKFilterMode.Linear)) : null;
            using var png = (resized ?? bitmap).Encode(SKEncodedImageFormat.Png, 100);
            var start = new ProcessStartInfo("tesseract") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "stdin", "stdout", "-l", "eng+vie", "--psm", "11", "tsv" }) start.ArgumentList.Add(arg);
            start.Environment["OMP_THREAD_LIMIT"] = "1";
            process = Process.Start(start);
            if (process == null) return null;
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.StandardInput.BaseStream.WriteAsync(png.ToArray(), timeout.Token);
            process.StandardInput.Close();
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), output, errors);
            return process.ExitCode == 0 && output.Result.Length <= 1_000_000 ? ExtractName(output.Result, accountNumber) : null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (Win32Exception) { return null; } // Local installs may not have the optional OCR binary.
        catch (IOException) { return null; }
        finally
        {
            if (process != null) { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (Win32Exception) { } process.Dispose(); }
            if (acquired) Slots.Release();
        }
    }

    public static string? ExtractName(string tsv, string? accountNumber)
    {
        var words = new List<(string Key, int Top, int Height, double Confidence, string Text)>();
        foreach (var row in tsv.Split('\n'))
        {
            var cells = row.TrimEnd('\r').Split('\t', 12);
            if (cells.Length != 12 || cells[0] != "5" || !int.TryParse(cells[7], out var top) || !int.TryParse(cells[9], out var height) ||
                !double.TryParse(cells[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence) || string.IsNullOrWhiteSpace(cells[11])) continue;
            words.Add((string.Join(':', cells[1..5]), top, height, confidence, cells[11]));
        }
        var lines = words.GroupBy(w => w.Key).Select(g => new {
            Top = g.Min(w => w.Top), Height = g.Max(w => w.Height), Confidence = g.Average(w => w.Confidence),
            Text = string.Join(' ', g.Select(w => w.Text))
        }).OrderBy(l => l.Top).ToList();
        var candidates = new List<(string Name, bool Anchored)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Confidence < 65) continue;
            var name = NormalizeName(line.Text);
            if (name == null) continue;
            var next = lines.Skip(i + 1).FirstOrDefault();
            var digits = next == null ? "" : string.Concat(next.Text.Where(char.IsDigit));
            var anchored = next != null && next.Top - line.Top <= Math.Max(120, line.Height * 5) &&
                (accountNumber is { Length: > 0 } ? digits == accountNumber : digits.Length >= 2 && Regex.IsMatch(next.Text, @"^[\d\s*•xX.]+$"));
            if (anchored || line.Confidence >= 85) candidates.Add((name, anchored));
        }
        var anchoredNames = candidates.Where(c => c.Anchored).Select(c => c.Name).Distinct().ToList();
        if (anchoredNames.Count == 1) return anchoredNames[0];
        if (anchoredNames.Count > 1) return null;
        var names = candidates.Select(c => c.Name).Distinct().ToList();
        return names.Count == 1 ? names[0] : null;
    }

    private static string? NormalizeName(string text)
    {
        var plain = new string(text.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Replace('Đ', 'D').Replace('đ', 'd').ToUpperInvariant();
        plain = Regex.Replace(plain, @"\s+", " ").Trim();
        plain = Regex.Replace(plain, @"^(?:TEN CHU TAI KHOAN|CHU TAI KHOAN|TEN NGUOI NHAN|NGUOI THU HUONG|ACCOUNT NAME)\s*[:\-]?\s*", "");
        if (!Regex.IsMatch(plain, @"^[A-Z]{1,25}(?: [A-Z]{1,25}){1,7}$") || plain.Length < 5 || plain.Length > 100) return null;
        if (Regex.IsMatch(plain, @"\b(?:BANK|NGAN HANG|VIETQR|NAPAS|MOMO|MO MO|VIETCOMBANK|TAI KHOAN|NHAN TIEN|CHUYEN TIEN|THANH TOAN|QUET MA|SCAN|PAYMENT|QR)\b")) return null;
        return plain;
    }
}
