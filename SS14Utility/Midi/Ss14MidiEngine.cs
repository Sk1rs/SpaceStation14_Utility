using NFluidsynth;
using MidiEvent = NFluidsynth.MidiEvent;

namespace SS14Utility.Midi;

public enum MidiCommand : byte
{
    NoteOff = 0x80,
    NoteOn = 0x90,
    AfterTouch = 0xA0,
    ControlChange = 0xB0,
    ProgramChange = 0xC0,
    ChannelPressure = 0xD0,
    PitchBend = 0xE0,
    SystemMessage = 0xF0,
}

/// <summary>
///     A single MIDI event as it comes out of the file player, in the same shape the game networks them
///     (status byte with command + channel, two data bytes).
/// </summary>
public readonly struct PlayerEvent
{
    public readonly byte Status;
    public readonly byte Data1;
    public readonly byte Data2;

    public PlayerEvent(byte status, byte data1, byte data2)
    {
        Status = status;
        Data1 = data1;
        Data2 = data2;
    }

    public int Channel => Status & 0x0F;
    public int Command => Status & 0xF0;
    public MidiCommand MidiCommand => (MidiCommand) Command;
    public byte Key => Data1;
    public byte Velocity => Data2;
    public byte Control => Data1;
    public byte Value => Data2;
    public byte Program => Data1;
    public byte Pressure => Data1;
    public int Pitch => (Data2 << 8) | Data1;

    public static byte MakeStatus(byte channel, MidiCommand command) => (byte) ((byte) command | channel);

    public static PlayerEvent ProgramChangeEvent(byte channel, byte program)
        => new(MakeStatus(channel, MidiCommand.ProgramChange), program, 0);

    public static PlayerEvent BankSelect(byte channel, byte bank)
        => new(MakeStatus(channel, MidiCommand.ControlChange), 0x0, bank);

    public static PlayerEvent AllNotesOff(byte channel)
        => new(MakeStatus(channel, MidiCommand.SystemMessage), 0x0B, 0x0);

    public static PlayerEvent SystemReset()
        => new((byte) ((byte) MidiCommand.SystemMessage | 0x0F), 0x0, 0x0);
}

/// <summary>
///     Fluidsynth-backed MIDI player that reproduces how RobustToolbox's MidiRenderer and the SS14
///     Instrument component treat a MIDI file: same synth settings, same soundfont order, same program
///     locking, percussion filtering and channel filtering.
/// </summary>
public sealed class Ss14MidiEngine : IDisposable
{
    public const int ChannelCount = 16;
    public const int PercussionChannel = 9;

    /// <summary>The game refuses to play MIDI files larger than this.</summary>
    public const int MidiSizeLimit = 2000000;

    private readonly object _lock = new();
    private readonly Settings _settings;
    private readonly Synth _synth;
    private readonly AudioDriver _driver;
    private readonly System.Threading.Timer _pollTimer;

    private Player? _player;
    private int _playerTotalTicks;
    private byte _midiProgram;
    private byte _midiBank;
    private bool _loopMidi;
    private bool _finishedRaised;
    private bool _disposed;

    public readonly List<string> LoadedSoundfonts = new();
    public readonly GameLimitSimulator Limits;

    /// <summary>Channels that are muted. Index 9 doubles as the percussion switch, like in the game.</summary>
    public bool[] FilteredChannels { get; } = new bool[ChannelCount];

    /// <summary>Mirrors InstrumentComponent.AllowProgramChange being false: the file cannot change program/bank.</summary>
    public bool DisableProgramChangeEvent { get; set; } = true;

    /// <summary>Fires on the polling thread when the file reached its end.</summary>
    public event Action? PlaybackFinished;

    public event Action<string>? Log;

    /// <param name="renderFile">
    ///     When set, audio is rendered to this .wav file instead of the speakers. Used by the self test.
    /// </param>
    public Ss14MidiEngine(string? renderFile = null)
    {
        // These are exactly the settings Robust.Client's MidiManager uses, except for the audio driver:
        // the game renders to OpenAL itself, we let fluidsynth talk to WASAPI.
        _settings = new Settings();
        _settings["synth.sample-rate"].DoubleValue = 44100;
        _settings["player.timing-source"].StringValue = "sample";
        _settings["synth.lock-memory"].IntValue = 0;
        _settings["synth.threadsafe-api"].IntValue = 1;
        _settings["synth.gain"].DoubleValue = 1.0d;
        _settings["synth.midi-channels"].IntValue = ChannelCount;
        _settings["synth.overflow.age"].DoubleValue = 3000;
        _settings["midi.autoconnect"].IntValue = 1;
        _settings["player.reset-synth"].IntValue = 0;
        _settings["synth.midi-bank-select"].StringValue = "gm";

        // midi.parallelism defaults to 1 in the game, which gives 1024 voices on one core.
        _settings["synth.polyphony"].IntValue = 1024;
        _settings["synth.cpu-cores"].IntValue = 1;

        if (renderFile != null)
        {
            _settings["audio.driver"].StringValue = "file";
            _settings["audio.file.name"].StringValue = renderFile;
            _settings["audio.file.type"].StringValue = "wav";
            _settings["audio.periods"].IntValue = 8;
            _settings["audio.period-size"].IntValue = 4096;
        }

        _synth = new Synth(_settings);
        _driver = renderFile == null ? OpenOutputDriver() : new AudioDriver(_settings, _synth);

        Limits = new GameLimitSimulator(this);

        _pollTimer = new System.Threading.Timer(PollPlayer, null, 100, 100);
    }

    /// <summary>The audio backend fluidsynth ended up using.</summary>
    public string AudioDriverName { get; private set; } = "file";

    /// <summary>
    ///     Opens the first audio backend that works. WASAPI is the good one on Windows; the others are
    ///     there for machines where it refuses to open.
    /// </summary>
    private AudioDriver OpenOutputDriver()
    {
        Exception? last = null;

        foreach (var driver in new[] { "wasapi", "dsound", "waveout" })
        {
            try
            {
                _settings["audio.driver"].StringValue = driver;
                var audioDriver = new AudioDriver(_settings, _synth);
                AudioDriverName = driver;
                return audioDriver;
            }
            catch (Exception e)
            {
                last = e;
            }
        }

        throw new InvalidOperationException("Не удалось открыть аудиовыход.", last);
    }

    /// <summary>Master volume, matching the game's midi.volume CVar range (0..1).</summary>
    public float Gain
    {
        get => _synth.Gain;
        set => _synth.Gain = Math.Clamp(value, 0f, 10f);
    }

    public bool IsPlaying
    {
        get
        {
            lock (_lock)
                return _player is { Status: FluidPlayerStatus.Playing };
        }
    }

    public bool IsFileOpen
    {
        get
        {
            lock (_lock)
                return _player != null;
        }
    }

    public bool LoopMidi
    {
        get => _loopMidi;
        set
        {
            lock (_lock)
                _player?.SetLoop(value ? -1 : 1);

            _loopMidi = value;
        }
    }

    /// <summary>Port of MidiRenderer.MidiProgram: pushes the program onto every channel but percussion.</summary>
    public byte MidiProgram
    {
        get => _midiProgram;
        set
        {
            var disable = DisableProgramChangeEvent;
            DisableProgramChangeEvent = false;

            lock (_lock)
            {
                for (byte i = 0; i < ChannelCount; i++)
                {
                    if (i == PercussionChannel)
                        continue;

                    SendMidiEvent(PlayerEvent.ProgramChangeEvent(i, value));
                }
            }

            DisableProgramChangeEvent = disable;
            _midiProgram = value;
        }
    }

    /// <summary>Port of MidiRenderer.MidiBank: bank select followed by a program re-select.</summary>
    public byte MidiBank
    {
        get => _midiBank;
        set
        {
            var disable = DisableProgramChangeEvent;
            DisableProgramChangeEvent = false;

            lock (_lock)
            {
                for (byte i = 0; i < ChannelCount; i++)
                {
                    if (i == PercussionChannel)
                        continue;

                    SendMidiEvent(PlayerEvent.BankSelect(i, value));
                    SendMidiEvent(PlayerEvent.ProgramChangeEvent(i, _midiProgram));
                }
            }

            DisableProgramChangeEvent = disable;
            _midiBank = value;
        }
    }

    public bool DisablePercussionChannel
    {
        get => FilteredChannels[PercussionChannel];
        set => FilteredChannels[PercussionChannel] = value;
    }

    public int PlayerTotalTick
    {
        get
        {
            if (_playerTotalTicks != 0)
                return _playerTotalTicks;

            lock (_lock)
                return _playerTotalTicks = _player?.GetTotalTicks ?? 0;
        }
    }

    public int PlayerTick
    {
        get
        {
            lock (_lock)
                return _player?.CurrentTick ?? 0;
        }
        set
        {
            lock (_lock)
            {
                if (_player == null)
                    return;

                _player.Seek(Math.Max(Math.Min(value, PlayerTotalTick - 1), 0));
            }

            // Seeking leaves notes hanging, the game deals with this by sending a system reset.
            StopAllNotes();
        }
    }

    /// <summary>Beats per minute the file is currently playing at.</summary>
    public int Bpm
    {
        get
        {
            lock (_lock)
                return _player?.Bpm ?? 0;
        }
        set
        {
            lock (_lock)
                _player?.SetBpmSafe(value);
        }
    }

    #region Soundfonts

    /// <summary>
    ///     Loads soundfonts in the same order Robust.Client does, so the resulting sound matches the game:
    ///     engine fallback, then the OS soundfont, then the content ones, then anything in the user directory.
    /// </summary>
    public void LoadSoundfonts(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (!File.Exists(path))
                    continue;

                if (!SoundFont.IsSoundFont(path))
                {
                    Log?.Invoke("Не саундфонт, пропущен: " + path);
                    continue;
                }

                lock (_lock)
                    _synth.LoadSoundFont(path, true);

                LoadedSoundfonts.Add(path);
                Log?.Invoke("Загружен саундфонт: " + path);
            }
            catch (Exception e)
            {
                Log?.Invoke("Не удалось загрузить " + path + ": " + e.Message);
            }
        }

        lock (_lock)
            _synth.ProgramReset();
    }

    #endregion

    #region Playback

    /// <summary>Port of MidiRenderer.OpenMidi.</summary>
    public bool OpenMidi(byte[] data, out string? error)
    {
        error = null;

        if (data.Length > MidiSizeLimit)
        {
            error = "Файл слишком большой: " + (data.Length / 1000000d).ToString("0.0") + " МБ, игра принимает до 2 МБ.";
            return false;
        }

        lock (_lock)
        {
            StopAllNotesNoLock();

            _playerTotalTicks = 0;
            _player?.Dispose();
            _player = new Player(_synth);
            _player.SetPlaybackCallback(HandlePlayerEvent);
            _player.AddMem(data);
            _player.Seek(0);
            _player.Play();
            _player.SetLoop(_loopMidi ? -1 : 1);
        }

        _finishedRaised = false;
        Limits.Reset();
        return true;
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (_player is not { Status: FluidPlayerStatus.Playing })
                return;

            _player.Stop();
        }

        StopAllNotes();
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (_player == null || _player.Status == FluidPlayerStatus.Playing)
                return;

            var tick = _player.CurrentTick;
            _player.Play();
            _player.Seek(Math.Max(Math.Min(tick, PlayerTotalTick - 1), 0));
        }
    }

    /// <summary>Port of MidiRenderer.CloseMidi.</summary>
    public void CloseMidi()
    {
        lock (_lock)
        {
            if (_player == null)
                return;

            _player.Stop();
            _player.Join();
            _player.Dispose();
            _player = null;
            _playerTotalTicks = 0;
        }

        Limits.Reset();
        StopAllNotes();
    }

    public void StopAllNotes()
    {
        lock (_lock)
            StopAllNotesNoLock();
    }

    private void StopAllNotesNoLock()
    {
        for (byte i = 0; i < ChannelCount; i++)
        {
            SendMidiEvent(PlayerEvent.AllNotesOff(i));
        }
    }

    public void SystemReset()
    {
        SendMidiEvent(PlayerEvent.SystemReset());
    }

    private void PollPlayer(object? state)
    {
        if (_disposed)
            return;

        try
        {
            bool done;
            lock (_lock)
            {
                if (_player == null)
                    return;

                done = _player.Status == FluidPlayerStatus.Done
                       || (PlayerTotalTick > 0 && _player.CurrentTick >= PlayerTotalTick);
            }

            if (!done || _finishedRaised)
                return;

            _finishedRaised = true;
            PlaybackFinished?.Invoke();
        }
        catch (Exception e)
        {
            Log?.Invoke("Ошибка опроса плеера: " + e.Message);
        }
    }

    #endregion

    #region Event handling

    /// <summary>
    ///     Callback fluidsynth's player calls for every MIDI event in the file. The game does the same thing
    ///     and never lets the player touch the synth directly, which is what makes program locking possible.
    /// </summary>
    private int HandlePlayerEvent(MidiEvent midiEvent)
    {
        if (_disposed)
            return 0;

        try
        {
            var status = (byte) (midiEvent.Type | midiEvent.Channel);
            var data1 = (byte) midiEvent.Control;
            var data2 = (byte) midiEvent.Value;

            if (midiEvent.Type == (int) MidiCommand.PitchBend)
            {
                var pitch = (ushort) midiEvent.Pitch;
                data1 = (byte) pitch;
                data2 = (byte) (pitch >> 8);
            }

            var ev = new PlayerEvent(status, data1, data2);

            if (Limits.Enabled)
                Limits.Enqueue(ev);
            else
                SendMidiEvent(ev);
        }
        catch (Exception e)
        {
            Log?.Invoke("Ошибка обработки события: " + e.Message);
        }

        return 0;
    }

    /// <summary>
    ///     Port of MidiRenderer.SendMidiEvent: this is where the instrument's rules are actually applied.
    /// </summary>
    public void SendMidiEvent(PlayerEvent midiEvent)
    {
        if (_disposed)
            return;

        try
        {
            lock (_lock)
            {
                switch (midiEvent.MidiCommand)
                {
                    case MidiCommand.NoteOff:
                        _synth.TryNoteOff(midiEvent.Channel, midiEvent.Key);
                        break;

                    case MidiCommand.NoteOn:
                    {
                        // Velocity 0 *can* represent a NoteOff event.
                        var velocity = midiEvent.Velocity;
                        if (velocity == 0)
                        {
                            _synth.TryNoteOn(midiEvent.Channel, midiEvent.Key, velocity);
                            break;
                        }

                        if (FilteredChannels[midiEvent.Channel])
                            break;

                        _synth.TryNoteOn(midiEvent.Channel, midiEvent.Key, velocity);
                        break;
                    }

                    case MidiCommand.AfterTouch:
                        _synth.KeyPressure(midiEvent.Channel, midiEvent.Key, midiEvent.Value);
                        break;

                    case MidiCommand.ControlChange:
                        // CC0 is bank selection.
                        if (midiEvent.Control == 0x0 && DisableProgramChangeEvent)
                            break;

                        if (midiEvent.Control != 0x0)
                            _synth.CC(midiEvent.Channel, midiEvent.Control, midiEvent.Value);
                        else // Fluidsynth doesn't respect CC0 as bank selection, so it's done manually.
                            _synth.BankSelect(midiEvent.Channel, midiEvent.Value);
                        break;

                    case MidiCommand.ProgramChange:
                        if (DisableProgramChangeEvent)
                            break;

                        _synth.ProgramChange(midiEvent.Channel, midiEvent.Program);
                        break;

                    case MidiCommand.ChannelPressure:
                        _synth.ChannelPressure(midiEvent.Channel, midiEvent.Pressure);
                        break;

                    case MidiCommand.PitchBend:
                        _synth.PitchBend(midiEvent.Channel, midiEvent.Pitch);
                        break;

                    // MIDI files spam these, the game ignores them too. 0x50 is the tempo meta event,
                    // which the player handles by itself.
                    case (MidiCommand) 0x00:
                    case (MidiCommand) 0x01:
                    case (MidiCommand) 0x05:
                    case (MidiCommand) 0x50:
                        return;

                    case MidiCommand.SystemMessage:
                        switch (midiEvent.Control)
                        {
                            case 0x0 when midiEvent.Status == 0xFF:
                                _synth.SystemReset();

                                // Reset the instrument to the one we were using.
                                if (DisableProgramChangeEvent)
                                {
                                    MidiBank = _midiBank;
                                    MidiProgram = _midiProgram;
                                }

                                break;

                            case 0x0B:
                                _synth.AllNotesOff(midiEvent.Channel);
                                break;
                        }

                        break;
                }
            }
        }
        catch (IndexOutOfRangeException)
        {
            // Malformed event, same as the game we just drop it.
        }
        catch (FluidSynthInteropException)
        {
            // Fluidsynth loves to complain about NoteOff for notes that already ended.
        }
    }

    #endregion

    #region Instrument logic

    /// <summary>
    ///     Applies an instrument prototype the way InstrumentSystem.UpdateRenderer does.
    /// </summary>
    public void ApplyInstrument(byte program, byte bank, bool allowPercussion, bool allowProgramChange)
    {
        DisablePercussionChannel = !allowPercussion;
        DisableProgramChangeEvent = !allowProgramChange;

        if (!allowPercussion)
            SendMidiEvent(PlayerEvent.AllNotesOff(PercussionChannel));

        if (!allowProgramChange)
        {
            MidiBank = bank;
            MidiProgram = program;
        }
    }

    /// <summary>
    ///     Mutes or unmutes a channel, matching InstrumentSystem.SetFilteredChannel.
    /// </summary>
    public void SetFilteredChannel(int channel, bool filtered)
    {
        if (channel < 0 || channel >= ChannelCount)
            return;

        FilteredChannels[channel] = filtered;

        if (filtered)
            SendMidiEvent(PlayerEvent.AllNotesOff((byte) channel));
    }

    #endregion

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _pollTimer.Dispose();
        Limits.Dispose();

        lock (_lock)
        {
            try
            {
                _player?.Stop();
                _player?.Join();
                _player?.Dispose();
            }
            catch
            {
                // Shutting down anyway.
            }

            _player = null;
            _driver.Dispose();
            _synth.Dispose();
            _settings.Dispose();
        }
    }
}

internal static class PlayerExtensions
{
    public static void SetBpmSafe(this Player player, int bpm)
    {
        if (bpm > 0)
            player.Bpm = bpm;
    }
}
