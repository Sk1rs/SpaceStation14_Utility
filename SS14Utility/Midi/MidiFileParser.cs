using System.Text;

namespace SS14Utility.Midi;

public sealed class MidiTrackInfo
{
    public string? TrackName;
    public string? InstrumentName;
    public string? ProgramName;

    public string Label(int channel, bool trackNames)
    {
        var fallback = "MIDI канал " + channel;

        if (trackNames)
        {
            if (TrackName != null && InstrumentName != null)
                return channel + ": " + TrackName + " (" + InstrumentName + ")";

            if (TrackName != null)
                return channel + ": " + TrackName;

            return fallback;
        }

        if (ProgramName != null)
            return channel + ": " + ProgramName;

        return fallback;
    }
}

public static class MidiFileParser
{
    public static bool TryGetTracks(byte[] data, out List<MidiTrackInfo?> tracks, out int division, out string? error)
    {
        tracks = new List<MidiTrackInfo?>();
        division = 0;
        error = null;

        try
        {
            if (!TryParse(data, out var parsed, out division, out error))
                return false;

            foreach (var track in parsed)
            {
                if (track is { TrackName: null, ProgramName: null, InstrumentName: null })
                    continue;

                tracks.Add(track);
            }

            if (tracks.Count > 16)
                tracks = tracks.GetRange(0, 16);

            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            tracks = new List<MidiTrackInfo?>();
            return false;
        }
    }

    private static bool TryParse(byte[] data, out List<MidiTrackInfo> tracks, out int division, out string? error)
    {
        tracks = new List<MidiTrackInfo>();
        division = 0;
        error = null;

        var stream = new Reader(data);

        if (stream.ReadString(4) != "MThd")
        {
            error = "Invalid file header";
            return false;
        }

        var headerLength = stream.ReadUInt32();

        stream.Skip(2);
        var trackCount = stream.ReadUInt16();
        division = stream.ReadUInt16();

        stream.Skip((int) (headerLength - 6));

        for (var i = 0; i < trackCount; i++)
        {
            if (stream.ReadString(4) != "MTrk")
            {
                error = "Track contains invalid header";
                return false;
            }

            var track = new MidiTrackInfo();

            var trackLength = stream.ReadUInt32();
            var trackEnd = stream.Position + trackLength;
            var hasMidiEvent = false;
            byte? lastStatusByte = null;

            while (stream.Position < trackEnd)
            {
                stream.ReadVariableLengthQuantity();

                var firstByte = stream.ReadByte();
                if (firstByte >= 0x80)
                    lastStatusByte = firstByte;
                else
                    stream.Skip(-1);

                if (lastStatusByte == null)
                {
                    error = "Track data not valid, expected status byte, got nothing.";
                    return false;
                }

                var eventType = (byte) (lastStatusByte & 0xF0);

                switch (lastStatusByte)
                {
                    case 0xFF:
                    {
                        var metaType = stream.ReadByte();
                        var metaLength = stream.ReadVariableLengthQuantity();
                        var metaData = stream.ReadBytes((int) metaLength);
                        if (metaType == 0x00)
                            continue;

                        if (metaType is < 0x01 or > 0x0F)
                            break;

                        var text = Encoding.ASCII.GetString(metaData, 0, (int) metaLength);
                        switch (metaType)
                        {
                            case 0x03 when track.TrackName == null:
                                track.TrackName = Sanitize(text);
                                break;
                            case 0x04 when track.InstrumentName == null:
                                track.InstrumentName = Sanitize(text);
                                break;
                        }

                        break;
                    }

                    case 0xF0:
                    case 0xF7:
                    {
                        var sysexLength = stream.ReadVariableLengthQuantity();
                        stream.Skip((int) sysexLength);
                        lastStatusByte = null;
                        break;
                    }

                    default:
                        switch (eventType)
                        {
                            case 0xC0:
                            {
                                var programNumber = stream.ReadByte();
                                track.ProgramName ??= Catalog.ProgramName(programNumber);
                                break;
                            }

                            case 0x80:
                            case 0x90:
                            case 0xA0:
                            case 0xB0:
                            case 0xE0:
                            {
                                hasMidiEvent = true;
                                stream.Skip(2);
                                break;
                            }

                            case 0xD0:
                            {
                                hasMidiEvent = true;
                                stream.Skip(1);
                                break;
                            }

                            default:
                                error = "Unknown MIDI event type " + lastStatusByte.Value.ToString("X2");
                                return false;
                        }

                        break;
                }
            }

            if (hasMidiEvent)
                tracks.Add(track);
        }

        return true;
    }

    private static string Sanitize(string input)
    {
        var sanitized = new StringBuilder(input.Length);

        foreach (var c in input)
        {
            if (!char.IsControl(c))
                sanitized.Append(c);
        }

        return sanitized.ToString().Trim();
    }

    private sealed class Reader
    {
        private readonly byte[] _data;

        public Reader(byte[] data)
        {
            _data = data;
        }

        public long Position { get; private set; }

        public void Skip(int count)
        {
            Position += count;
        }

        public byte ReadByte()
        {
            if (Position >= _data.Length)
                throw new EndOfStreamException("Unexpected end of MIDI file.");

            return _data[Position++];
        }

        public byte[] ReadBytes(int count)
        {
            if (Position + count > _data.Length)
                throw new EndOfStreamException("Unexpected end of MIDI file.");

            var result = new byte[count];
            Array.Copy(_data, Position, result, 0, count);
            Position += count;
            return result;
        }

        public string ReadString(int length)
        {
            return Encoding.ASCII.GetString(ReadBytes(length));
        }

        public ushort ReadUInt16()
        {
            return (ushort) ((ReadByte() << 8) | ReadByte());
        }

        public uint ReadUInt32()
        {
            return (uint) ((ReadByte() << 24) | (ReadByte() << 16) | (ReadByte() << 8) | ReadByte());
        }

        public uint ReadVariableLengthQuantity()
        {
            uint result = 0;

            for (var i = 0; i < 4; i++)
            {
                var b = ReadByte();
                result = (result << 7) | (uint) (b & 0x7F);

                if ((b & 0x80) == 0)
                    break;
            }

            return result;
        }
    }
}
