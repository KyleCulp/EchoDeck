namespace EchoDeck.Engine.Io;

/// <summary>
/// Single-producer / single-consumer float ring buffer. Drop-oldest on overflow.
/// Lock-based (uncontended at the ~10 ms WASAPI cadence) and allocation-free after
/// construction. Replaces the per-sample-allocating <c>ConcurrentQueue&lt;float&gt;</c>
/// the original prototype used.
/// </summary>
public sealed class RingBuffer
{
    private readonly float[] _buf;
    private readonly int _capacity;
    private readonly object _gate = new();
    private int _head;   // index of the oldest sample
    private int _count;

    public RingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _buf = new float[capacity];
    }

    public int Count
    {
        get { lock (_gate) return _count; }
    }

    /// <summary>Append samples; overwrites the oldest data if capacity is exceeded.</summary>
    public void Write(ReadOnlySpan<float> data)
    {
        lock (_gate)
        {
            foreach (float s in data)
            {
                int tail = (_head + _count) % _capacity;
                _buf[tail] = s;
                if (_count == _capacity)
                    _head = (_head + 1) % _capacity; // full → drop oldest
                else
                    _count++;
            }
        }
    }

    /// <summary>Reads up to <paramref name="dest"/>.Length samples; returns the count read.</summary>
    public int Read(Span<float> dest)
    {
        lock (_gate)
        {
            int n = Math.Min(dest.Length, _count);
            for (int i = 0; i < n; i++)
                dest[i] = _buf[(_head + i) % _capacity];
            _head = (_head + n) % _capacity;
            _count -= n;
            return n;
        }
    }

    /// <summary>Drops the oldest <paramref name="n"/> samples (latency trim).</summary>
    public void DiscardOldest(int n)
    {
        lock (_gate)
        {
            n = Math.Min(n, _count);
            _head = (_head + n) % _capacity;
            _count -= n;
        }
    }

    public void Clear()
    {
        lock (_gate) { _head = 0; _count = 0; }
    }
}
