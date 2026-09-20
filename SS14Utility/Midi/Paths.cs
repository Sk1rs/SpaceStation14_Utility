using System.Text.Json;

namespace SS14Utility.Midi;

public static class Paths
{
    public static string GameData => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Space Station 14",
        "data");

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

    public static List<string> SoundfontLoadOrder()
    {
        var result = new List<string>();

        var fallback = Path.Combine(SoundfontDir, "fallback.sf2");
        if (File.Exists(fallback))
            result.Add(fallback);

        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        if (!string.IsNullOrEmpty(systemRoot))
        {
            var osFont = Path.Combine(systemRoot, "system32", "drivers", "gm.dls");
            if (File.Exists(osFont))
                result.Add(osFont);
        }

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
        }
    }
}
