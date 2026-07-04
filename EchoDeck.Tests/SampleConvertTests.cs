using EchoDeck.Engine.Audio;
using NAudio.Wave;
using Xunit;

namespace EchoDeck.Tests;

public class SampleConvertTests
{
    // ── FloatToPcm16 ─────────────────────────────────────────────────────────────
    [Fact]
    public void FloatToPcm16_EncodesFullScaleAndZero_LittleEndian()
    {
        var dest = new byte[3 * 2];
        int n = SampleConvert.FloatToPcm16(new float[] { 1f, 0f, -1f }, dest);

        Assert.Equal(6, n);
        Assert.Equal((short)32767, BitConverter.ToInt16(dest, 0)); //  +1.0
        Assert.Equal((short)0, BitConverter.ToInt16(dest, 2));     //   0.0
        Assert.Equal((short)-32767, BitConverter.ToInt16(dest, 4)); // -1.0
    }

    [Fact]
    public void FloatToPcm16_ClampsOutOfRangeInput()
    {
        var dest = new byte[2 * 2];
        SampleConvert.FloatToPcm16(new float[] { 2f, -2f }, dest);

        Assert.Equal((short)32767, BitConverter.ToInt16(dest, 0));
        Assert.Equal((short)-32767, BitConverter.ToInt16(dest, 2));
    }

    [Fact]
    public void FloatToPcm16_ThenReadBack16_RoundTripsWithinQuantization()
    {
        float[] input = { 0.5f, -0.5f, 0.25f, -0.75f };
        var pcm = new byte[input.Length * 2];
        SampleConvert.FloatToPcm16(input, pcm);

        for (int i = 0; i < input.Length; i++)
        {
            float back = SampleConvert.ReadSample(pcm, i * 2, 16, isFloat: false);
            Assert.InRange(back, input[i] - 0.001f, input[i] + 0.001f);
        }
    }

    // ── DownmixToMono ────────────────────────────────────────────────────────────
    [Fact]
    public void Downmix_Mono16Bit_PassesThroughValues()
    {
        var wf = new WaveFormat(48000, 16, 1);
        byte[] buf = Concat(Pcm16(16384), Pcm16(-16384)); // exactly ±0.5
        var dest = new float[2];

        int frames = SampleConvert.DownmixToMono(wf, buf, buf.Length, dest);

        Assert.Equal(2, frames);
        Assert.Equal(0.5f, dest[0], 3);
        Assert.Equal(-0.5f, dest[1], 3);
    }

    [Fact]
    public void Downmix_Stereo16Bit_AveragesChannels()
    {
        var wf = new WaveFormat(48000, 16, 2);
        // frame0: L=+0.5, R=-0.5 → 0.0 ;  frame1: L=+0.5, R=+0.5 → +0.5
        byte[] buf = Concat(Pcm16(16384), Pcm16(-16384), Pcm16(16384), Pcm16(16384));
        var dest = new float[2];

        int frames = SampleConvert.DownmixToMono(wf, buf, buf.Length, dest);

        Assert.Equal(2, frames);
        Assert.Equal(0.0f, dest[0], 3);
        Assert.Equal(0.5f, dest[1], 3);
    }

    [Fact]
    public void Downmix_Float32Mono_PassesThroughExactly()
    {
        var wf = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        byte[] buf = Concat(BitConverter.GetBytes(0.123f), BitConverter.GetBytes(-0.456f));
        var dest = new float[2];

        SampleConvert.DownmixToMono(wf, buf, buf.Length, dest);

        Assert.Equal(0.123f, dest[0], 5);
        Assert.Equal(-0.456f, dest[1], 5);
    }

    [Fact]
    public void Downmix_24BitMono_SignExtendsCorrectly()
    {
        var wf = new WaveFormat(48000, 24, 1);
        byte[] buf = Concat(Pcm24(0x400000), Pcm24(unchecked((int)0xFFC00000))); // +0.5, -0.5
        var dest = new float[2];

        SampleConvert.DownmixToMono(wf, buf, buf.Length, dest);

        Assert.Equal(0.5f, dest[0], 3);
        Assert.Equal(-0.5f, dest[1], 3);
    }

    [Fact]
    public void Downmix_32BitIntMono_Scales()
    {
        var wf = new WaveFormat(48000, 32, 1); // PCM (not float) → treated as 32-bit int
        byte[] buf = BitConverter.GetBytes(0x40000000); // +0.5
        var dest = new float[1];

        SampleConvert.DownmixToMono(wf, buf, buf.Length, dest);

        Assert.Equal(0.5f, dest[0], 3);
    }

    [Fact]
    public void ReadSample_UnsupportedBitDepth_Throws()
    {
        Assert.Throws<NotSupportedException>(() => SampleConvert.ReadSample(new byte[1], 0, 8, isFloat: false));
    }

    // ── helpers ──────────────────────────────────────────────────────────────────
    private static byte[] Pcm16(int v) => BitConverter.GetBytes((short)v);

    private static byte[] Pcm24(int v) => new[] { (byte)(v & 0xff), (byte)((v >> 8) & 0xff), (byte)((v >> 16) & 0xff) };

    private static byte[] Concat(params byte[][] parts)
    {
        var outp = new List<byte>();
        foreach (var p in parts) outp.AddRange(p);
        return outp.ToArray();
    }
}
