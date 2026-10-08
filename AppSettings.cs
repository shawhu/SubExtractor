using System.Text.Json;

namespace SubExtractor;

internal sealed class AppSettings
{
    public required WindowSize ClientSize { get; init; }
    public required WindowPosition ClientStartPosition { get; init; }

    public static AppSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<AppSettings>(json)
            ?? throw new InvalidDataException("appsettings.json must contain a JSON object.");

        if (settings.ClientSize.Width <= 0 || settings.ClientSize.Height <= 0)
        {
            throw new InvalidDataException("ClientSize Width and Height must be positive values.");
        }

        return settings;
    }
}

internal sealed class WindowSize
{
    public int Width { get; init; }
    public int Height { get; init; }
}

internal sealed class WindowPosition
{
    public int X { get; init; }
    public int Y { get; init; }
}
