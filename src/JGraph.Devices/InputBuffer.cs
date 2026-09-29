namespace JGraph.Devices;

/// <summary>
/// The bytes a transport has received and a script has not read yet: MATLAB's AsyncIO input buffer.
/// A transport's reader thread appends; the script thread takes, scans and waits. Every member is
/// safe to call from either.
/// </summary>
/// <remarks>
/// The buffer grows as needed (a ring over one array, doubled when full); nothing is dropped, as
/// MATLAB's buffer drops nothing a script has not flushed. A wait wakes on an append, a flush, a fault
/// and on its cancellation token, so a script's Stop ends a blocking read at once.
/// </remarks>
public sealed class InputBuffer
{
    /// <summary>What <see cref="Appended"/> is raised with: the bytes that came and how many are waiting now.</summary>
    public delegate void AppendedHandler(ReadOnlySpan<byte> bytes, int waiting);

    private readonly object _gate = new();
    private byte[] _ring = new byte[4096];
    private int _start;
    private int _count;
    private long _received;
    private long _generation;
    private Exception? _fault;

    /// <summary>
    /// Raised on the appending thread after bytes arrive, outside the buffer's lock, with the bytes that
    /// came and the count now waiting — what the callback logic of a client watches.
    /// </summary>
    public event AppendedHandler? Appended;

    /// <summary>How many bytes are waiting to be read.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    /// <summary>Every byte ever appended, flushed ones included.</summary>
    public long TotalReceived
    {
        get
        {
            lock (_gate)
            {
                return _received;
            }
        }
    }

    /// <summary>How many flushes have happened: a scan that saw an older generation starts over.</summary>
    public long Generation
    {
        get
        {
            lock (_gate)
            {
                return _generation;
            }
        }
    }

    /// <summary>The fault that ended the connection, once one has.</summary>
    public Exception? Fault
    {
        get
        {
            lock (_gate)
            {
                return _fault;
            }
        }
    }

    /// <summary>Adds bytes at the end and wakes every waiter.</summary>
    public void Append(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return;
        }

        int now;
        lock (_gate)
        {
            EnsureCapacity(_count + bytes.Length);
            int end = (_start + _count) % _ring.Length;
            int first = Math.Min(bytes.Length, _ring.Length - end);
            bytes[..first].CopyTo(_ring.AsSpan(end));
            bytes[first..].CopyTo(_ring.AsSpan(0));
            _count += bytes.Length;
            _received += bytes.Length;
            now = _count;
            Monitor.PulseAll(_gate);
        }

        // The handlers run on a transport's reader thread, where an exception would end the process.
        try
        {
            Appended?.Invoke(bytes, now);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Marks the connection as ended; every waiter wakes and the fault is kept.</summary>
    public void SetFault(Exception fault)
    {
        lock (_gate)
        {
            _fault ??= fault;
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>Discards what is waiting.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _start = 0;
            _count = 0;
            _generation++;
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>Removes and answers up to <paramref name="count"/> bytes from the front.</summary>
    public byte[] Take(int count)
    {
        lock (_gate)
        {
            return TakeLocked(Math.Min(count, _count));
        }
    }

    /// <summary>Removes and answers everything waiting.</summary>
    public byte[] TakeAll()
    {
        lock (_gate)
        {
            return TakeLocked(_count);
        }
    }

    /// <summary>A copy of the waiting bytes, without removing them.</summary>
    public byte[] Peek()
    {
        lock (_gate)
        {
            var copy = new byte[_count];
            CopyOut(copy, 0, _count);
            return copy;
        }
    }

    /// <summary>
    /// The index, counted from the front, just past the first occurrence of <paramref name="pattern"/>
    /// that ends at or after <paramref name="from"/> bytes; -1 when none.
    /// </summary>
    public int IndexAfter(ReadOnlySpan<byte> pattern, int from = 0)
    {
        lock (_gate)
        {
            return IndexAfterLocked(pattern, from);
        }
    }

    /// <summary>
    /// Waits until at least <paramref name="count"/> bytes are waiting, the time runs out, the token is
    /// cancelled or the connection faults. Answers whether the count was reached.
    /// </summary>
    public bool WaitForCount(int count, TimeSpan timeout, CancellationToken cancel) =>
        WaitUntil(() => _count >= count, timeout, cancel);

    /// <summary>
    /// Waits until <paramref name="pattern"/> is in the buffer; answers the index just past its first
    /// occurrence, or -1 when the time ran out.
    /// </summary>
    public int WaitForPattern(ReadOnlySpan<byte> pattern, TimeSpan timeout, CancellationToken cancel)
    {
        byte[] copy = pattern.ToArray();
        int found = -1;
        WaitUntil(() => (found = IndexAfterLocked(copy, 0)) >= 0, timeout, cancel);
        return found;
    }

    /// <summary>
    /// Waits, under the buffer's lock, until <paramref name="condition"/> holds; the condition runs with
    /// the lock held. Throws <see cref="OperationCanceledException"/> on the token and the fault's own
    /// exception when the connection ends with the condition still false.
    /// </summary>
    public bool WaitUntil(Func<bool> condition, TimeSpan timeout, CancellationToken cancel)
    {
        long deadline = timeout == Timeout.InfiniteTimeSpan ? long.MaxValue : Environment.TickCount64 + (long)Math.Ceiling(timeout.TotalMilliseconds);
        using CancellationTokenRegistration wake = cancel.CanBeCanceled
            ? cancel.Register(() =>
            {
                lock (_gate)
                {
                    Monitor.PulseAll(_gate);
                }
            })
            : default;
        lock (_gate)
        {
            while (true)
            {
                if (condition())
                {
                    return true;
                }

                cancel.ThrowIfCancellationRequested();
                if (_fault is not null)
                {
                    return false;
                }

                long left = deadline - Environment.TickCount64;
                if (left <= 0)
                {
                    return false;
                }

                Monitor.Wait(_gate, (int)Math.Min(left, 250));
            }
        }
    }

    private int IndexAfterLocked(ReadOnlySpan<byte> pattern, int from)
    {
        if (pattern.IsEmpty)
        {
            return -1;
        }

        int firstStart = Math.Max(0, from - pattern.Length + 1);
        for (int i = firstStart; i + pattern.Length <= _count; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (_ring[(_start + i + j) % _ring.Length] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i + pattern.Length;
            }
        }

        return -1;
    }

    private byte[] TakeLocked(int count)
    {
        var taken = new byte[count];
        CopyOut(taken, 0, count);
        _start = (_start + count) % _ring.Length;
        _count -= count;
        if (_count == 0)
        {
            _start = 0;
        }

        return taken;
    }

    private void CopyOut(byte[] into, int offset, int count)
    {
        int first = Math.Min(count, _ring.Length - _start);
        Array.Copy(_ring, _start, into, offset, first);
        Array.Copy(_ring, 0, into, offset + first, count - first);
    }

    private void EnsureCapacity(int needed)
    {
        if (needed <= _ring.Length)
        {
            return;
        }

        int size = _ring.Length;
        while (size < needed)
        {
            size *= 2;
        }

        var grown = new byte[size];
        CopyOut(grown, 0, _count);
        _ring = grown;
        _start = 0;
    }
}
