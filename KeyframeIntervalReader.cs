namespace SubExtractor;

internal static class KeyframeIntervalReader
{
    internal static async Task<long> ReadAsync(string path)
    {
        const long fallbackMs = 5000;
        const int keyframeCount = 20; // how many keyframes to sample
        var durationSeconds = await ReadDurationAsync(path);
        var scanSeconds = durationSeconds < 140 ? durationSeconds * 8 / 10 : 140; // how many seconds to scan, centered in the video

        var scanStartSeconds = (durationSeconds - scanSeconds) / 2;
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
                .Select(line => int.TryParse(line.Split('.')[0].TrimEnd(','), out var t) ? t : -1)
                .Where(t => t >= 0)
                .Take(keyframeCount)
                .ToList();
            var intervalMs = times.Count < 2
                ? 0
                : (long)Math.Ceiling((double)(times[^1] - times[0]) / (times.Count - 1)) * 1000;
            return intervalMs > 0 ? intervalMs : fallbackMs;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return fallbackMs;
        }
    }

    private static async Task<int> ReadDurationAsync(string path)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("ffprobe")
        {
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        foreach (var arg in new[]
                 {
                 "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", "-i", path
             })
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            using var process = System.Diagnostics.Process.Start(startInfo)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return int.TryParse(output.Trim().Split('.')[0], out var d) ? d : 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return 0;
        }
    }
}