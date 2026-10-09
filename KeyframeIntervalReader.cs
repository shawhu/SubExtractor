using System.Globalization;

namespace SubExtractor;

internal static class KeyframeIntervalReader
{
    internal static async Task<long> ReadAsync(string path)
    {
        const long fallbackMs = 5000;
        const int keyframeCount = 20; // how many keyframes to sample
        const int scanStartSeconds = 30 * 60; // skip the first 30 minutes
        const int scanSeconds = 140; // how many seconds to scan after the start position
        var startInfo = new System.Diagnostics.ProcessStartInfo("ffprobe")
        {
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        foreach (var arg in new[]
                 {
                 "-v", "error", "-select_streams", "v:0", "-skip_frame", "nokey",
                 "-show_entries", "frame=pts_time", "-of", "csv=p=0",
                 "-read_intervals", $"{scanStartSeconds}%+{scanSeconds}", "-i", path
             })
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            using var process = System.Diagnostics.Process.Start(startInfo)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            var times = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => double.TryParse(line.TrimEnd(','), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : -1)
                .Where(t => t >= 0)
                .Take(keyframeCount)
                .ToList();
            var intervalMs = times.Count < 2
                ? 0
                : (long)Math.Ceiling((times[^1] - times[0]) / (times.Count - 1)) * 1000;
            return intervalMs > 0 ? intervalMs : fallbackMs;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return fallbackMs;
        }
    }
}
