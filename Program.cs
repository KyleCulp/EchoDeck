using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

const int SampleRate = 48000;
const int FrameSamples = 480; // 10ms @ 48k

string micName = "Microphone (2- Shure MV7+)";
string speakerName = "Speakers (3- Modi 5)";
string cableName = "CABLE In 16ch";

var micQ = new ConcurrentQueue<float>();
var farQ = new ConcurrentQueue<float>();

Console.WriteLine("nv-aec-bridge starting...");

using var devices = new MMDeviceEnumerator();

var micDevice = FindDevice(devices, DataFlow.Capture, micName);
var speakerDevice = FindDevice(devices, DataFlow.Render, speakerName);
var cableDevice = FindDevice(devices, DataFlow.Render, cableName);

Console.WriteLine($"Mic:     {micDevice.FriendlyName}");
Console.WriteLine($"Speaker: {speakerDevice.FriendlyName}");
Console.WriteLine($"Output:  {cableDevice.FriendlyName}");

using var micCap = new WasapiCapture(micDevice);
using var farCap = new WasapiLoopbackCapture(speakerDevice);

PrintFormat("mic", micCap.WaveFormat);
PrintFormat("far", farCap.WaveFormat);

if (micCap.WaveFormat.SampleRate != SampleRate)
  throw new Exception($"Mic is {micCap.WaveFormat.SampleRate} Hz, not 48000.");
if (farCap.WaveFormat.SampleRate != SampleRate)
  throw new Exception($"Speaker loopback is {farCap.WaveFormat.SampleRate} Hz, not 48000.");

micCap.DataAvailable += (_, e) => EnqueueMono(micQ, micCap.WaveFormat, e.Buffer, e.BytesRecorded);
farCap.DataAvailable += (_, e) => EnqueueMono(farQ, farCap.WaveFormat, e.Buffer, e.BytesRecorded);

var outFormat = new WaveFormat(SampleRate, 16, 1);
var outBuffer = new BufferedWaveProvider(outFormat)
{
  BufferDuration = TimeSpan.FromMilliseconds(300),
  DiscardOnBufferOverflow = true
};

using var outDev = new WasapiOut(cableDevice, AudioClientShareMode.Shared, true, 30);
outDev.Init(outBuffer);
outDev.Play();

using var aec = new NvAec();

bool running = true;
Console.CancelKeyPress += (_, e) =>
{
  e.Cancel = true;
  running = false;
};

micCap.StartRecording();
farCap.StartRecording();

Console.WriteLine();
Console.WriteLine("RUNNING.");
Console.WriteLine("Now set apps to use microphone: CABLE Output");
Console.WriteLine("Ctrl+C to stop.");
Console.WriteLine();

var near = new float[FrameSamples];
var far = new float[FrameSamples];
var clean = new float[FrameSamples];

while (running)
{
  if (micQ.Count < FrameSamples)
  {
    Thread.Sleep(1);
    continue;
  }

  DequeueFrame(micQ, near, fillZeros: false);
  DequeueFrame(farQ, far, fillZeros: true);

  aec.Run(near, far, clean);

  var pcm = FloatToPcm16(clean);
  outBuffer.AddSamples(pcm, 0, pcm.Length);

  while (micQ.Count > SampleRate / 2) micQ.TryDequeue(out _);
  while (farQ.Count > SampleRate / 2) farQ.TryDequeue(out _);
}

micCap.StopRecording();
farCap.StopRecording();

static MMDevice FindDevice(MMDeviceEnumerator devices, DataFlow flow, string contains)
{
  var matches = devices.EnumerateAudioEndPoints(flow, DeviceState.Active)
      .Where(d => d.FriendlyName.Contains(contains, StringComparison.OrdinalIgnoreCase))
      .ToList();

  if (matches.Count == 0)
  {
    Console.WriteLine($"Could not find {flow} device containing: {contains}");
    Console.WriteLine("Available:");
    foreach (var d in devices.EnumerateAudioEndPoints(flow, DeviceState.Active))
      Console.WriteLine($"- {d.FriendlyName}");
    throw new Exception("Device not found.");
  }

  return matches[0];
}

static void PrintFormat(string label, WaveFormat wf)
{
  Console.WriteLine($"{label} format: {wf.SampleRate} Hz, {wf.Channels} ch, {wf.BitsPerSample} bit, {wf.Encoding}");
}

static void EnqueueMono(ConcurrentQueue<float> q, WaveFormat wf, byte[] buffer, int bytes)
{
  int channels = wf.Channels;
  int blockAlign = wf.BlockAlign;
  int frames = bytes / blockAlign;

  bool treatAsFloat =
      wf.Encoding == WaveFormatEncoding.IeeeFloat ||
      (wf.Encoding == WaveFormatEncoding.Extensible && wf.BitsPerSample == 32);

  int bytesPerSample = wf.BitsPerSample / 8;

  for (int f = 0; f < frames; f++)
  {
    float sum = 0;

    for (int ch = 0; ch < channels; ch++)
    {
      int offset = f * blockAlign + ch * bytesPerSample;
      sum += ReadSample(buffer, offset, wf.BitsPerSample, treatAsFloat);
    }

    q.Enqueue(sum / channels);
  }
}

static float ReadSample(byte[] b, int o, int bits, bool isFloat)
{
  if (isFloat)
    return BitConverter.ToSingle(b, o);

  return bits switch
  {
    16 => BitConverter.ToInt16(b, o) / 32768f,
    24 => Read24(b, o) / 8388608f,
    32 => BitConverter.ToInt32(b, o) / 2147483648f,
    _ => throw new Exception($"Unsupported sample size: {bits}")
  };
}

static int Read24(byte[] b, int o)
{
  int v = b[o] | (b[o + 1] << 8) | (b[o + 2] << 16);
  if ((v & 0x800000) != 0)
    v |= unchecked((int)0xFF000000);
  return v;
}

static void DequeueFrame(ConcurrentQueue<float> q, float[] dst, bool fillZeros)
{
  for (int i = 0; i < dst.Length; i++)
  {
    if (q.TryDequeue(out var v))
      dst[i] = v;
    else
      dst[i] = fillZeros ? 0 : throw new Exception("Mic underrun.");
  }
}

static byte[] FloatToPcm16(float[] samples)
{
  var bytes = new byte[samples.Length * 2];

  for (int i = 0; i < samples.Length; i++)
  {
    float s = Math.Clamp(samples[i], -1f, 1f);
    short v = (short)(s * short.MaxValue);
    bytes[i * 2] = (byte)(v & 0xff);
    bytes[i * 2 + 1] = (byte)((v >> 8) & 0xff);
  }

  return bytes;
}

unsafe sealed class NvAec : IDisposable
{
  private IntPtr _handle;

  private const string SdkDir = @"C:\Program Files\NVIDIA Corporation\NVIDIA Audio Effects SDK";
  private const string Model = @"C:\Program Files\NVIDIA Corporation\NVIDIA Audio Effects SDK\models\aec_48k.trtpkg";

  public NvAec()
  {
    SetDllDirectory(SdkDir);

    Check(NvAFX_CreateEffect("aec", out _handle), "NvAFX_CreateEffect");
    Check(NvAFX_SetString(_handle, "model_path", Model), "NvAFX_SetString(model_path)");
    Check(NvAFX_SetFloat(_handle, "intensity_ratio", 1.0f), "NvAFX_SetFloat(intensity_ratio)");

    NvAFX_SetU32(_handle, "enable_vad", 0);

    Check(NvAFX_Load(_handle), "NvAFX_Load");

    Console.WriteLine("NVIDIA AEC loaded.");
  }

  public void Run(float[] near, float[] far, float[] output)
  {
    fixed (float* pNear = near)
    fixed (float* pFar = far)
    fixed (float* pOut = output)
    {
      IntPtr[] input = [(IntPtr)pNear, (IntPtr)pFar];
      IntPtr[] outp = [(IntPtr)pOut];

      Check(NvAFX_Run(_handle, input, outp, 480, 2), "NvAFX_Run");
    }
  }

  public void Dispose()
  {
    if (_handle != IntPtr.Zero)
    {
      NvAFX_DestroyEffect(_handle);
      _handle = IntPtr.Zero;
    }
  }

  private static void Check(int status, string where)
  {
    if (status != 0)
      throw new Exception($"{where} failed: {status}");
  }

  [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
  private static extern bool SetDllDirectory(string lpPathName);

  [DllImport("NVAudioEffects.dll", CallingConvention = CallingConvention.Cdecl)]
  private static extern int NvAFX_CreateEffect(
      [MarshalAs(UnmanagedType.LPStr)] string code,
      out IntPtr handle);

  [DllImport("NVAudioEffects.dll", CallingConvention = CallingConvention.Cdecl)]
  private static extern int NvAFX_SetString(
      IntPtr handle,
      [MarshalAs(UnmanagedType.LPStr)] string paramName,
      [MarshalAs(UnmanagedType.LPStr)] string value);

  [DllImport("NVAudioEffects.dll", CallingConvention = CallingConvention.Cdecl)]
  private static extern int NvAFX_SetFloat(
      IntPtr handle,
      [MarshalAs(UnmanagedType.LPStr)] string paramName,
      float value);

  [DllImport("NVAudioEffects.dll", CallingConvention = CallingConvention.Cdecl)]
  private static extern int NvAFX_SetU32(
      IntPtr handle,
      [MarshalAs(UnmanagedType.LPStr)] string paramName,
      uint value);

  [DllImport("NVAudioEffects.dll", CallingConvention = CallingConvention.Cdecl)]
  private static extern int NvAFX_Load(IntPtr handle);

  [DllImport("NVAudioEffects.dll", CallingConvention = CallingConvention.Cdecl)]
  private static extern int NvAFX_Run(
      IntPtr handle,
      IntPtr[] input,
      IntPtr[] output,
      int numSamples,
      int numChannels);

  [DllImport("NVAudioEffects.dll", CallingConvention = CallingConvention.Cdecl)]
  private static extern int NvAFX_DestroyEffect(IntPtr handle);
}
