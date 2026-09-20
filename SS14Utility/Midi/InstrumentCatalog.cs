using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SS14Utility.Midi;

public sealed class InstrumentDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("nameRu")] public string? NameRu { get; set; }
    [JsonPropertyName("program")] public byte Program { get; set; }
    [JsonPropertyName("bank")] public byte Bank { get; set; }
    [JsonPropertyName("allowPercussion")] public bool AllowPercussion { get; set; }
    [JsonPropertyName("allowProgramChange")] public bool AllowProgramChange { get; set; }
    [JsonPropertyName("respectMidiLimits")] public bool RespectMidiLimits { get; set; } = true;
    [JsonPropertyName("styles")] public List<InstrumentStyle>? Styles { get; set; }
    [JsonPropertyName("file")] public string File { get; set; } = "";

    public string DisplayName
    {
        get
        {
            var ru = NameRu;
            if (string.IsNullOrWhiteSpace(ru) || ru.Contains('{'))
                return Name;

            return ru;
        }
    }

    public override string ToString()
    {
        var name = DisplayName;
        if (!string.Equals(name, Name, StringComparison.OrdinalIgnoreCase))
            name = name + " (" + Name + ")";

        return name;
    }
}

public sealed class InstrumentStyle
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("program")] public byte Program { get; set; }
    [JsonPropertyName("bank")] public byte Bank { get; set; }
}

public sealed class ProgramDef
{
    [JsonPropertyName("program")] public int Program { get; set; }
    [JsonPropertyName("en")] public string En { get; set; } = "";
    [JsonPropertyName("ru")] public string Ru { get; set; } = "";

    public override string ToString() => Program.ToString("D3") + " - " + Ru;
}

public static class Catalog
{
    private static List<InstrumentDef>? _instruments;
    private static List<ProgramDef>? _programs;

    public static IReadOnlyList<InstrumentDef> Instruments => _instruments ??= Load<InstrumentDef>("instruments.json");

    public static IReadOnlyList<ProgramDef> Programs => _programs ??= Load<ProgramDef>("programs.json");

    public static string ProgramName(int program)
    {
        if (program >= 0 && program < Programs.Count)
            return Programs[program].Ru;

        return "Программа " + program;
    }

    private static List<T> Load<T>(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames()
            .First(x => x.EndsWith(name, StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resource)!;
        return JsonSerializer.Deserialize<List<T>>(stream) ?? new List<T>();
    }
}
