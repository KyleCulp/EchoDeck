using NAudio.Wave;

namespace EchoDeck.Engine.Audio;

/// <summary>
/// Sample decode / downmix / PCM helpers, ported from the original prototype
/// (handles 16/24/32-bit PCM and 32-bit float, any channel count).
/// </summary>
internal static class SampleConvert
{
    /// <summary>
    /// Downmix an interleaved WASAPI buffer to mono float into <paramref name="dest"/>
    /// (must hold at least <c>bytes / wf.BlockAlign</c> samples). Returns frames written.
    /// </summary>
    public static int DownmixToMono(WaveFormat wf, byte[] buffer, int bytes, float[] dest)
    {
        int channels = wf.Channels;
        int blockAlign = wf.BlockAlign;
        int frames = bytes / blockAlign;
        int bytesPerSample = wf.BitsPerSample / 8;
        bool isFloat = wf.Encoding == WaveFormatEncoding.IeeeFloat
                       || (wf.Encoding == WaveFormatEncoding.Extensible && wf.BitsPerSample == 32);

        for (int f = 0; f < frames; f++)
        {
            float sum = 0f;
            for (int ch = 0; ch < channels; ch++)
            {
                int offset = f * blockAlign + ch * bytesPerSample;
                sum += ReadSample(buffer, offset, wf.BitsPerSample, isFloat);
            }
            dest[f] = sum / channels;
        }
        return frames;
    }

    public static float ReadSample(byte[] b, int o, int bits, bool isFloat)
    {
        if (isFloat) return BitConverter.ToSingle(b, o);
        return bits switch
        {
            16 => BitConverter.ToInt16(b, o) / 32768f,
            24 => Read24(b, o) / 8388608f,
            32 => BitConverter.ToInt32(b, o) / 2147483648f,
            _ => throw new NotSupportedException($"Unsupported sample size: {bits} bit")
        };
    }

    private static int Read24(byte[] b, int o)
    {
        int v = b[o] | (b[o + 1] << 8) | (b[o + 2] << 16);
        if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
        return v;
    }

    /// <summary>
    /// Convert mono float [-1,1] to 16-bit PCM into <paramref name="dest"/>
    /// (must hold <c>samples.Length * 2</c> bytes). Returns bytes written.
    /// </summary>
    public static int FloatToPcm16(ReadOnlySpan<float> samples, byte[] dest)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            float s = Math.Clamp(samples[i], -1f, 1f);
            short v = (short)(s * short.MaxValue);
            dest[i * 2] = (byte)(v & 0xff);
            dest[i * 2 + 1] = (byte)((v >> 8) & 0xff);
        }
        return samples.Length * 2;
    }
}
