namespace EchoDeck.Engine.Interop;

/// <summary>
/// A single NVIDIA Audio Effects SDK effect at 48 kHz / 10 ms / 480-sample frames.
/// Handles both 1-input effects (denoiser / dereverb / dereverb_denoiser) and the
/// 2-input AEC (near + far). Must be created, run, and disposed on one thread
/// (the engine's processing thread).
/// </summary>
internal sealed unsafe class NvAfxEffect : IDisposable
{
    public string Code { get; }
    public int InputChannels { get; }

    private IntPtr _handle;
    // Reused every frame so the ~100 fps hot path stays allocation-free (plan 02: zero-alloc).
    private readonly IntPtr[] _inputPtrs;
    private readonly IntPtr[] _outputPtrs = new IntPtr[1];

    public NvAfxEffect(string sdkDir, string code, string modelFile, int inputChannels, float intensity = 1.0f)
    {
        Code = code;
        InputChannels = inputChannels;
        _inputPtrs = new IntPtr[inputChannels];

        NvAfx.EnsureSdkOnPath(sdkDir);
        string model = Path.Combine(sdkDir, "models", modelFile);
        if (!File.Exists(model))
            throw new FileNotFoundException($"NVIDIA model not found:\n{model}", model);

        Check(NvAfx.NvAFX_CreateEffect(code, out _handle), $"CreateEffect({code})");
        Check(NvAfx.NvAFX_SetString(_handle, "model_path", model), "SetString(model_path)");
        NvAfx.NvAFX_SetFloat(_handle, "intensity_ratio", intensity); // ignored by effects that don't support it
        if (code == "aec") NvAfx.NvAFX_SetU32(_handle, "enable_vad", 0);
        Check(NvAfx.NvAFX_Load(_handle), "Load");
    }

    /// <summary>
    /// Process one frame. <paramref name="primary"/> is the main input (mic / previous
    /// stage); <paramref name="far"/> is the AEC reference (ignored for 1-input effects).
    /// All buffers are <c>length</c> samples.
    /// </summary>
    public void Run(float[] primary, float[]? far, float[] output)
    {
        fixed (float* pPrimary = primary)
        fixed (float* pFar = far)
        fixed (float* pOut = output)
        {
            _inputPtrs[0] = (IntPtr)pPrimary;
            if (InputChannels == 2) _inputPtrs[1] = (IntPtr)pFar;
            _outputPtrs[0] = (IntPtr)pOut;
            Check(NvAfx.NvAFX_Run(_handle, _inputPtrs, _outputPtrs, primary.Length, InputChannels), $"Run({Code})");
        }
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            NvAfx.NvAFX_DestroyEffect(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private static void Check(int status, string where)
    {
        if (status != 0)
            throw new InvalidOperationException($"NvAFX {where} failed (status {status}).");
    }
}
