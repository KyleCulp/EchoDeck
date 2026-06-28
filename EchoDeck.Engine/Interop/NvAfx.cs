using System.Runtime.InteropServices;

namespace EchoDeck.Engine.Interop;

/// <summary>
/// Raw P/Invoke surface for the NVIDIA Audio Effects SDK (<c>NVAudioEffects.dll</c>),
/// plus in-process SDK path setup. Calling convention is Cdecl. Effect handles must
/// be created/run/destroyed from a single thread (the engine's processing thread).
/// </summary>
internal static class NvAfx
{
    private const string Dll = "NVAudioEffects.dll";

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string lpPathName);

    private static bool _pathReady;

    /// <summary>
    /// Make the SDK directory (and the CUDA/TensorRT DLLs beside it) resolvable in-process.
    /// Replaces the original <c>start-bridge.bat</c> PATH shim so the app runs standalone.
    /// </summary>
    public static void EnsureSdkOnPath(string sdkDir)
    {
        if (_pathReady) return;
        SetDllDirectory(sdkDir);
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        bool present = path.Split(';')
            .Any(p => string.Equals(p.TrimEnd('\\'), sdkDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        if (!present)
            Environment.SetEnvironmentVariable("PATH", sdkDir + ";" + path);
        _pathReady = true;
    }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NvAFX_CreateEffect([MarshalAs(UnmanagedType.LPStr)] string code, out IntPtr handle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NvAFX_SetString(IntPtr handle,
        [MarshalAs(UnmanagedType.LPStr)] string paramName, [MarshalAs(UnmanagedType.LPStr)] string value);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NvAFX_SetFloat(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string paramName, float value);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NvAFX_SetU32(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string paramName, uint value);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NvAFX_GetU32(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string paramName, out uint value);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NvAFX_Load(IntPtr handle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NvAFX_Run(IntPtr handle, IntPtr[] input, IntPtr[] output, int numSamples, int numChannels);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NvAFX_DestroyEffect(IntPtr handle);
}
