namespace SubExtractor;

internal static class Common
{
    internal static string FormatFileSize(long bytes)
    {
        const double bytesPerMegabyte = 1024 * 1024;
        const double bytesPerGigabyte = 1024 * bytesPerMegabyte;

        if (bytes > bytesPerGigabyte)
        {
            return $"{bytes / bytesPerGigabyte:0.###} GB";
        }

        return $"{Math.Max(1, bytes / bytesPerMegabyte):0.##} MB";
    }

    internal static string FormatDuration(TimeSpan duration)
    {
        var hours = (int)duration.TotalHours;
        return $"{hours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    }

    internal static string FormatResolution(int height)
    {
        return height switch
        {
            >= 4320 => "8K",
            >= 2160 => "4K",
            _ => $"{height}p"
        };
    }
}
