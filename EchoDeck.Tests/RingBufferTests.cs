using EchoDeck.Engine.Io;
using Xunit;

namespace EchoDeck.Tests;

public class RingBufferTests
{
    [Fact]
    public void WriteThenRead_ReturnsSameSamplesInOrder()
    {
        var rb = new RingBuffer(8);
        rb.Write(new float[] { 1, 2, 3 });

        Assert.Equal(3, rb.Count);
        var dest = new float[3];
        int n = rb.Read(dest);

        Assert.Equal(3, n);
        Assert.Equal(new float[] { 1, 2, 3 }, dest);
        Assert.Equal(0, rb.Count);
    }

    [Fact]
    public void Read_IntoLargerBuffer_ReturnsOnlyAvailableCount()
    {
        var rb = new RingBuffer(8);
        rb.Write(new float[] { 5, 6 });

        var dest = new float[10];
        int n = rb.Read(dest);

        Assert.Equal(2, n);
        Assert.Equal(5, dest[0]);
        Assert.Equal(6, dest[1]);
    }

    [Fact]
    public void Overflow_DropsOldestSamples()
    {
        var rb = new RingBuffer(3);
        rb.Write(new float[] { 1, 2, 3, 4, 5 }); // only 3, 4, 5 should remain

        Assert.Equal(3, rb.Count);
        var dest = new float[3];
        rb.Read(dest);
        Assert.Equal(new float[] { 3, 4, 5 }, dest);
    }

    [Fact]
    public void WrapAround_PreservesFifoOrder()
    {
        var rb = new RingBuffer(4);
        rb.Write(new float[] { 1, 2, 3 });

        var two = new float[2];
        rb.Read(two);                       // consumes 1, 2; head advances
        Assert.Equal(new float[] { 1, 2 }, two);

        rb.Write(new float[] { 4, 5, 6 });  // wraps past the physical end
        Assert.Equal(4, rb.Count);          // holds 3, 4, 5, 6

        var four = new float[4];
        int n = rb.Read(four);
        Assert.Equal(4, n);
        Assert.Equal(new float[] { 3, 4, 5, 6 }, four);
    }

    [Fact]
    public void DiscardOldest_TrimsFromTheFront()
    {
        var rb = new RingBuffer(8);
        rb.Write(new float[] { 1, 2, 3, 4 });
        rb.DiscardOldest(2);

        Assert.Equal(2, rb.Count);
        var dest = new float[2];
        rb.Read(dest);
        Assert.Equal(new float[] { 3, 4 }, dest);
    }

    [Fact]
    public void DiscardOldest_MoreThanCount_ClampsToEmpty()
    {
        var rb = new RingBuffer(8);
        rb.Write(new float[] { 1, 2 });
        rb.DiscardOldest(10);
        Assert.Equal(0, rb.Count);
    }

    [Fact]
    public void Clear_EmptiesTheBuffer()
    {
        var rb = new RingBuffer(8);
        rb.Write(new float[] { 1, 2, 3 });
        rb.Clear();

        Assert.Equal(0, rb.Count);
        Assert.Equal(0, rb.Read(new float[3]));
    }

    [Fact]
    public void ReadFromEmpty_ReturnsZero()
    {
        var rb = new RingBuffer(4);
        Assert.Equal(0, rb.Read(new float[4]));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer(-1));
    }

    [Fact]
    public void ProducerConsumer_UnderConcurrency_LosesNoOrdering()
    {
        // One writer, one reader (the engine's SPSC contract). The reader must observe a
        // strictly increasing sequence with no reordering. Drops under overflow are allowed,
        // so the reader drains until the writer signals done rather than waiting for a count.
        const int total = 200_000;
        var rb = new RingBuffer(1024);
        long lastSeen = -1;
        bool ordered = true;
        bool writerDone = false;

        var reader = new Thread(() =>
        {
            var buf = new float[256];
            while (!Volatile.Read(ref writerDone) || rb.Count > 0)
            {
                int n = rb.Read(buf);
                for (int i = 0; i < n; i++)
                {
                    if (buf[i] <= lastSeen) ordered = false;
                    lastSeen = (long)buf[i];
                }
                if (n == 0) Thread.SpinWait(50);
            }
        });
        reader.Start();

        var one = new float[1];
        for (int i = 0; i < total; i++)
        {
            one[0] = i + 1; // strictly increasing sequence
            rb.Write(one);
        }
        Volatile.Write(ref writerDone, true);
        reader.Join();

        Assert.True(ordered, "reader observed out-of-order samples");
        Assert.True(lastSeen > 0, "reader observed nothing");
    }
}
