using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace SubExtractor;

internal sealed record VideoMetadata(int Width, int Height, TimeSpan Duration, double FrameRate);

internal static class VideoMetadataReader
{
    public static async Task<VideoMetadata> ReadAsync(string filePath)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-v");
        process.StartInfo.ArgumentList.Add("error");
        process.StartInfo.ArgumentList.Add("-select_streams");
        process.StartInfo.ArgumentList.Add("v:0");
        process.StartInfo.ArgumentList.Add("-show_entries");
        process.StartInfo.ArgumentList.Add("stream=width,height,avg_frame_rate,r_frame_rate,duration:format=duration");
        process.StartInfo.ArgumentList.Add("-of");
        process.StartInfo.ArgumentList.Add("json");
        process.StartInfo.ArgumentList.Add(filePath);

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Could not start ffprobe.");
            }
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                "Could not start ffprobe. Install FFmpeg and make sure ffprobe is available on PATH.",
                ex);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync();
            throw new TimeoutException("ffprobe did not finish reading the video within 30 seconds.");
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException(
                string.IsNullOrWhiteSpace(error) ? "ffprobe could not read this video file." : error.Trim());
        }

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        if (!root.TryGetProperty("streams", out var streams) ||
            !streams.EnumerateArray().Any())
        {
            throw new InvalidDataException("The dropped file does not contain a video stream.");
        }

        var stream = streams[0];
        var width = GetRequiredInt(stream, "width");
        var height = GetRequiredInt(stream, "height");
        var durationSeconds = GetNumber(root, "format", "duration")
            ?? GetNumber(stream, "duration")
            ?? throw new InvalidDataException("Could not determine the video duration.");
        if (durationSeconds <= 0 || durationSeconds > TimeSpan.MaxValue.TotalSeconds)
        {
            throw new InvalidDataException("The video duration is invalid.");
        }

        var frameRate = GetFrameRate(stream, "avg_frame_rate")
            ?? GetFrameRate(stream, "r_frame_rate")
            ?? throw new InvalidDataException("Could not determine the video frame rate.");

        return new VideoMetadata(width, height, TimeSpan.FromSeconds(durationSeconds), frameRate);
    }

    private static int GetRequiredInt(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) &&
            property.TryGetInt32(out var value) &&
            value > 0)
        {
            return value;
        }

        throw new InvalidDataException($"Could not determine the video {propertyName}.");
    }

    private static double? GetNumber(JsonElement element, string objectName, string propertyName)
    {
        if (!element.TryGetProperty(objectName, out var nested))
        {
            return null;
        }

        return GetNumber(nested, propertyName);
    }

    private static double? GetNumber(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        var text = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            double.IsFinite(value) &&
            value >= 0)
        {
            return value;
        }

        return null;
    }

    private static double? GetFrameRate(JsonElement stream, string propertyName)
    {
        if (!stream.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var parts = property.GetString()!.Split('/');
        if (parts.Length != 2 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) ||
            denominator == 0)
        {
            return null;
        }

        var frameRate = numerator / denominator;
        return double.IsFinite(frameRate) && frameRate > 0 ? frameRate : null;
    }
}
