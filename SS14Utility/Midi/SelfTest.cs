using System.Diagnostics;
using System.Text;

namespace SS14Utility.Midi;

/// <summary>
///     Headless smoke test: boots the synth exactly like the UI does, plays a file for a few seconds and
///     writes what happened to a log file. Run with:
///     SS14Utility.exe --selftest [midi file] [instrument id] [seconds] [--limits]
/// </summary>
public static class SelfTest
{
    public static int Run(string[] args)
    {
        var log = new StringBuilder();
        var logPath = Path.Combine(Path.GetTempPath(), "ss14midiplayer-selftest.log");

        void Write(string line)
        {
            log.AppendLine(line);
        }

        try
        {
            var positional = new List<string>();
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i].StartsWith("--"))
                {
                    if (args[i] is "--render" or "--maxeps")
                        i++; // Skip its value.

                    continue;
                }

                positional.Add(args[i]);
            }

            var file = positional.FirstOrDefault(File.Exists);
            var instrumentId = positional.FirstOrDefault(x => !File.Exists(x) && !int.TryParse(x, out _));
            var seconds = positional.Where(x => int.TryParse(x, out _)).Select(int.Parse).FirstOrDefault(4);
            var limits = args.Contains("--limits");
            var renderIndex = Array.IndexOf(args, "--render");
            var renderFile = renderIndex >= 0 && renderIndex + 1 < args.Length ? args[renderIndex + 1] : null;
            logPath = renderFile != null ? renderFile + ".log" : logPath;

            file ??= Paths.DefaultMidiFolders()
                .SelectMany(x => Directory.EnumerateFiles(x, "*.mid", SearchOption.AllDirectories))
                .FirstOrDefault();

            if (file == null)
            {
                Write("Не найден ни один MIDI файл для теста.");
                File.WriteAllText(logPath, log.ToString());
                return 1;
            }

            using var engine = new Ss14MidiEngine(renderFile);
            engine.Log += x => Write("[engine] " + x);

            var fonts = Paths.SoundfontLoadOrder();
            Write("Саундфонты по порядку загрузки:");
            foreach (var font in fonts)
            {
                Write("  " + font + " (" + new FileInfo(font).Length / 1024 + " КБ)");
            }

            engine.LoadSoundfonts(fonts);
            Write("Загружено саундфонтов: " + engine.LoadedSoundfonts.Count);

            var instrument = Catalog.Instruments.FirstOrDefault(x => x.Id == instrumentId)
                             ?? Catalog.Instruments.First(x => x.Id == "ViolinInstrument");

            Write("Инструментов в каталоге: " + Catalog.Instruments.Count);
            Write("Инструмент: " + instrument.Id + " (" + instrument.DisplayName + ") program=" + instrument.Program
                  + " bank=" + instrument.Bank + " percussion=" + instrument.AllowPercussion
                  + " programChange=" + instrument.AllowProgramChange);

            engine.ApplyInstrument(instrument.Program, instrument.Bank, instrument.AllowPercussion, instrument.AllowProgramChange);
            engine.Limits.RespectMidiLimits = instrument.RespectMidiLimits;

            // Lets the test squeeze the limits to check the simulation actually kicks in.
            var epsIndex = Array.IndexOf(args, "--maxeps");
            if (epsIndex >= 0 && epsIndex + 1 < args.Length && int.TryParse(args[epsIndex + 1], out var maxEps))
            {
                engine.Limits.MaxEventsPerSecond = maxEps;
                engine.Limits.MaxEventsPerBatch = Math.Max(1, maxEps / GameLimitSimulator.TickRate);
                Write("Лимиты понижены для теста: " + maxEps + " событий/сек, "
                      + engine.Limits.MaxEventsPerBatch + " за батч.");
            }
            engine.Limits.Enabled = limits;
            engine.Gain = 0.5f;

            var data = File.ReadAllBytes(file);
            Write("Файл: " + file + " (" + data.Length / 1024 + " КБ)");

            if (MidiFileParser.TryGetTracks(data, out var tracks, out var division, out var parseError))
            {
                Write("Треков распознано: " + tracks.Count + ", division=" + division);
                for (var i = 0; i < tracks.Count; i++)
                {
                    Write("  канал " + i + ": " + (tracks[i]?.Label(i, true) ?? "нет данных"));
                }
            }
            else
            {
                Write("Парсер треков не справился: " + parseError);
            }

            if (!engine.OpenMidi(data, out var error))
            {
                Write("Не удалось открыть MIDI: " + error);
                File.WriteAllText(logPath, log.ToString());
                return 1;
            }

            var timer = Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < seconds)
            {
                Thread.Sleep(500);
                Write("t=" + timer.Elapsed.TotalSeconds.ToString("0.0") + "s tick=" + engine.PlayerTick
                      + "/" + engine.PlayerTotalTick + " bpm=" + engine.Bpm + " playing=" + engine.IsPlaying
                      + (limits
                          ? " | лимиты: " + engine.Limits.Status + " ev/s=" + engine.Limits.EventsPerSecond
                            + " sent=" + engine.Limits.SentPerSecond + " queue=" + engine.Limits.QueueLength
                          : ""));
            }

            engine.CloseMidi();
            Write("Готово.");
        }
        catch (Exception e)
        {
            Write("ОШИБКА: " + e);
            File.WriteAllText(logPath, log.ToString());
            return 1;
        }

        File.WriteAllText(logPath, log.ToString());
        return 0;
    }
}
