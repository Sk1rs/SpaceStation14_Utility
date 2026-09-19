using System.Text.Json;

namespace SS14Utility.Midi;

public static class Paths
{
    /// <summary>%APPDATA%/Space Station 14/data — the client's user data directory.</summary>
    public static string GameData => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Space Station 14",
        "data");

    /// <summary>Where the launcher keeps downloaded engine builds, used to find the fallback soundfont.</summary>
    public static string EngineCache => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Space Station 14",
        "launcher",
        "engines");

    public static string AppDir => AppDomain.CurrentDomain.BaseDirectory;

    public static string SoundfontDir => Path.Combine(AppDir, "soundfonts");

    public static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SS14MidiPlayer",
        "settings.json");

    /// <summary>
    ///     MIDI folders the game itself uses, so the file list is populated on first start.
    /// </summary>
    public static IEnumerable<string> DefaultMidiFolders()
    {
        var data = GameData;
        foreach (var name in new[] { "MIDI", "UserMidis" })
        {
            var path = Path.Combine(data, name);
            if (Directory.Exists(path))
                yield return path;
        }

        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (Directory.Exists(music))
            yield return music;
    }

    /// <summary>
    ///     Soundfonts in exactly the order Robust.Client's MidiManager loads them; the last one wins for
    ///     any preset it defines. Engine fallback, OS soundfont, content soundfonts, then user soundfonts.
    /// </summary>
    public static List<string> SoundfontLoadOrder()
    {
        var result = new List<string>();

        var fallback = Path.Combine(SoundfontDir, "fallback.sf2");
        if (File.Exists(fallback))
            result.Add(fallback);

        // MidiManager: {SystemRoot}/system32/drivers/gm.dls on Windows.
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        if (!string.IsNullOrEmpty(systemRoot))
        {
            var osFont = Path.Combine(systemRoot, "system32", "drivers", "gm.dls");
            if (File.Exists(osFont))
                result.Add(osFont);
        }

        // Content soundfonts from Resources/Audio/MidiCustom, sorted like the resource manager returns them.
        if (Directory.Exists(SoundfontDir))
        {
            foreach (var file in Directory.GetFiles(SoundfontDir).OrderBy(x => x, StringComparer.Ordinal))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".sf2" or ".sf3" or ".dls"))
                    continue;

                if (Path.GetFileName(file).Equals("fallback.sf2", StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(file);
            }
        }

        // User soundfonts override everything, same as in the game.
        var userFonts = Path.Combine(GameData, "soundfonts");
        if (Directory.Exists(userFonts))
        {
            foreach (var file in Directory.GetFiles(userFonts).OrderBy(x => x, StringComparer.Ordinal))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".sf2" or ".sf3" or ".dls")
                    result.Add(file);
            }
        }

        return result;
    }
}

/// <summary>
///     Remembers what the user picked last time.
/// </summary>
public sealed class AppSettings
{
    public string? MidiFolder { get; set; }
    public string? InstrumentId { get; set; }
    public float Volume { get; set; } = 0.5f;
    public bool Loop { get; set; }
    public bool SimulateLimits { get; set; }
    public bool TrackNames { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Paths.SettingsFile)) ?? new AppSettings();
        }
        catch
        {
            // Corrupt settings are not worth bothering the user about.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Paths.SettingsFile)!);
            File.WriteAllText(Paths.SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Not being able to save settings shouldn't kill the app.
        }
    }
}
