using System.Diagnostics;

namespace SS14Utility.Midi;

public enum LimitStatus
{
    Idle,
    Fine,
    Lagging,
    Cramping,
    Stopped,
}

public sealed class GameLimitSimulator
{
    public const int TickRate = 30;

    public int MaxEventsPerBatch { get; set; } = 60;

    public int MaxEventsPerSecond { get; set; } = 1000;

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

    public event Action? Cramped;

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

    public bool StopWhenCramped { get; set; } = true;

    public bool RespectMidiLimits { get; set; } = true;

    public LimitStatus Status { get; private set; } = LimitStatus.Idle;

    public int EventsPerSecond { get; private set; }

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

    public void Tick()
    {
        if (!_enabled)
            return;

        RotateSecondCounters();

        if (Status == LimitStatus.Stopped)
        {
            lock (_lock)
                _queue.Clear();

            return;
        }

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
