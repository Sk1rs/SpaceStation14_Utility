using System.Diagnostics;

namespace SS14MidiPlayer;

public enum LimitStatus
{
    Idle,
    Fine,
    Lagging,
    Cramping,
    Stopped,
}

/// <summary>
///     Simulates what everyone else in the round would hear.
///
///     The player himself always hears the file in full: his own renderer plays it locally. Other players
///     only get what the client manages to send over the network, and that is rate limited: at most
///     midi.max_events_per_batch (60) events per game tick and midi.max_events_per_second (1000) events per
///     second, with the rest queueing up. Once the client keeps falling behind for midi.max_lagged_batches
///     (8) batches the server stops relaying the music, cramps the player's fingers and stuns them.
///
///     Turning this on routes playback through the same queue, so a MIDI that is too dense for the game
///     sounds here the way it would sound in the round.
/// </summary>
public sealed class GameLimitSimulator
{
    /// <summary>net.tickrate</summary>
    public const int TickRate = 30;

    /// <summary>midi.max_events_per_batch</summary>
    public int MaxEventsPerBatch { get; set; } = 60;

    /// <summary>midi.max_events_per_second</summary>
    public int MaxEventsPerSecond { get; set; } = 1000;

    /// <summary>midi.max_lagged_batches</summary>
    public int MaxLaggedBatches { get; set; } = 8;

    private readonly Ss14MidiEngine _engine;
    private readonly object _lock = new();
    private readonly Queue<PlayerEvent> _queue = new();
    private readonly System.Threading.Timer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private long _lastSecondReset;
    private int _sentWithinASec;
    private int _receivedWithinASec;
    private int _laggedBatches;
    private bool _crampedRaised;
    private bool _enabled;

    public GameLimitSimulator(Ss14MidiEngine engine)
    {
        _engine = engine;
        _timer = new System.Threading.Timer(_ => Tick(), null, 1000 / TickRate, 1000 / TickRate);
    }

    /// <summary>Fired once when the server would have cut the music off and stunned the player.</summary>
    public event Action? Cramped;

    /// <summary>Whether playback goes through the network rate limit.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;

            _enabled = value;
            Reset();
        }
    }

    /// <summary>
    ///     Whether the music actually goes silent once the limits are blown, like the server cutting it off,
    ///     or whether it keeps playing (delayed) and only reports the problem.
    /// </summary>
    public bool StopWhenCramped { get; set; } = true;

    /// <summary>Instrument's respectMidiLimits: admin instruments ignore the limits entirely.</summary>
    public bool RespectMidiLimits { get; set; } = true;

    public LimitStatus Status { get; private set; } = LimitStatus.Idle;

    /// <summary>Events the file produced during the last second.</summary>
    public int EventsPerSecond { get; private set; }

    /// <summary>Events that actually made it through during the last second.</summary>
    public int SentPerSecond { get; private set; }

    public int QueueLength
    {
        get
        {
            lock (_lock)
                return _queue.Count;
        }
    }

    public int LaggedBatches => _laggedBatches;

    /// <summary>Roughly how far behind the other players' audio is, in milliseconds.</summary>
    public int DelayMs
    {
        get
        {
            var queued = QueueLength;
            if (queued == 0)
                return 0;

            var perSecond = Math.Max(1, Math.Min(MaxEventsPerSecond, MaxEventsPerBatch * TickRate));
            return (int) (queued / (double) perSecond * 1000);
        }
    }

    public void Reset()
    {
        lock (_lock)
            _queue.Clear();

        _sentWithinASec = 0;
        _receivedWithinASec = 0;
        _laggedBatches = 0;
        _crampedRaised = false;
        EventsPerSecond = 0;
        SentPerSecond = 0;
        Status = LimitStatus.Idle;
    }

    public void Enqueue(PlayerEvent ev)
    {
        lock (_lock)
        {
            _queue.Enqueue(ev);
            _receivedWithinASec++;
        }
    }

    /// <summary>
    ///     One game tick worth of relaying, mirroring InstrumentSystem.Update on the client and
    ///     OnMidiEventRx on the server.
    /// </summary>
    public void Tick()
    {
        if (!_enabled)
            return;

        RotateSecondCounters();

        // The server has cleaned the instrument up: nothing reaches the listeners any more.
        if (Status == LimitStatus.Stopped)
        {
            lock (_lock)
                _queue.Clear();

            return;
        }

        // Admin instruments (respectMidiLimits: false) send everything, no matter how much it is.
        if (!RespectMidiLimits)
        {
            DrainAll();
            Status = LimitStatus.Fine;
            return;
        }

        var max = Math.Min(MaxEventsPerBatch, MaxEventsPerSecond - _sentWithinASec);

        List<PlayerEvent> batch;
        int remaining;

        lock (_lock)
        {
            if (_queue.Count == 0)
            {
                _laggedBatches = 0;

                if (Status != LimitStatus.Idle)
                    Status = LimitStatus.Fine;

                return;
            }

            if (max <= 0)
            {
                // Hit the per-second limit: this whole tick is lost time for the listeners.
                _laggedBatches++;
                Status = LimitStatus.Lagging;
                CheckCramps();
                return;
            }

            batch = new List<PlayerEvent>(Math.Min(max, _queue.Count));
            while (batch.Count < max && _queue.Count > 0)
            {
                batch.Add(_queue.Dequeue());
            }

            remaining = _queue.Count;
        }

        foreach (var ev in batch)
        {
            _engine.SendMidiEvent(ev);
        }

        _sentWithinASec += batch.Count;

        if (remaining > 0)
        {
            // Still behind after a full batch, that is exactly what makes the server call it lag.
            _laggedBatches++;
            Status = LimitStatus.Lagging;
            CheckCramps();
        }
        else
        {
            _laggedBatches = 0;
            Status = LimitStatus.Fine;
        }
    }

    private void RotateSecondCounters()
    {
        var now = _clock.ElapsedMilliseconds;
        if (now - _lastSecondReset < 1000)
            return;

        _lastSecondReset = now;
        EventsPerSecond = _receivedWithinASec;
        SentPerSecond = _sentWithinASec;
        _receivedWithinASec = 0;
        _sentWithinASec = 0;
    }

    private void DrainAll()
    {
        while (true)
        {
            PlayerEvent ev;
            lock (_lock)
            {
                if (_queue.Count == 0)
                    return;

                ev = _queue.Dequeue();
            }

            _engine.SendMidiEvent(ev);
            _sentWithinASec++;
        }
    }

    private void CheckCramps()
    {
        // Two thirds of the way to the limit is where the game starts warning the player.
        if (_laggedBatches >= (int) (MaxLaggedBatches * (2 / 3d) + 1))
            Status = LimitStatus.Cramping;

        if (_laggedBatches < MaxLaggedBatches)
            return;

        if (StopWhenCramped)
            Status = LimitStatus.Stopped;

        if (_crampedRaised)
            return;

        _crampedRaised = true;
        Cramped?.Invoke();
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
