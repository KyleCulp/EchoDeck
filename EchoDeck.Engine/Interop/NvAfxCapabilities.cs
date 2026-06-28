namespace EchoDeck.Engine.Interop;

/// <summary>Result of probing the installed NVIDIA Audio Effects SDK.</summary>
public sealed class SdkStatus
{
    public required string SdkDir { get; init; }
    public bool DllPresent { get; init; }
    public bool ModelsDirPresent { get; init; }
    public string? Version { get; init; }
    public IReadOnlySet<string> AvailableEffects { get; init; } = new HashSet<string>();

    /// <summary>True when the DLL and the models folder both exist.</summary>
    public bool Installed => DllPresent && ModelsDirPresent;

    public bool Has(string effectCode) => AvailableEffects.Contains(effectCode);

    // Convenience flags for the GUI.
    public bool Aec => Has("aec");
    public bool Denoiser => Has("denoiser") || Has("dereverb_denoiser");
    public bool Dereverb => Has("dereverb") || Has("dereverb_denoiser");
    public bool StudioVoice => Has("studio_voice");
}

/// <summary>
/// Probes the installed SDK: which DLL/models are present (so the GUI can gate effects
/// and warn the user up front). Pure filesystem checks — no GPU calls.
/// </summary>
public static class NvAfxCapabilities
{
    public const string DefaultSdkDir =
        @"C:\Program Files\NVIDIA Corporation\NVIDIA Audio Effects SDK";

    // 48 kHz models for each effect the engine can use.
    private static readonly (string code, string model)[] Map =
    {
        ("aec", "aec_48k.trtpkg"),
        ("denoiser", "denoiser_48k.trtpkg"),
        ("dereverb", "dereverb_48k.trtpkg"),
        ("dereverb_denoiser", "dereverb_denoiser_48k.trtpkg"),
        ("studio_voice", "studio_voice_48k.trtpkg"),
    };

    public static SdkStatus Probe(string sdkDir)
    {
        bool dll = File.Exists(Path.Combine(sdkDir, "NVAudioEffects.dll"));
        string modelsDir = Path.Combine(sdkDir, "models");
        bool modelsPresent = Directory.Exists(modelsDir);

        var available = new HashSet<string>();
        if (modelsPresent)
        {
            foreach (var (code, model) in Map)
                if (File.Exists(Path.Combine(modelsDir, model)))
                    available.Add(code);
        }

        return new SdkStatus
        {
            SdkDir = sdkDir,
            DllPresent = dll,
            ModelsDirPresent = modelsPresent,
            Version = ReadVersion(sdkDir),
            AvailableEffects = available
        };
    }

    private static string? ReadVersion(string sdkDir)
    {
        try
        {
            string vh = Path.Combine(sdkDir, "version.h");
            if (!File.Exists(vh)) return null;
            foreach (string line in File.ReadLines(vh))
            {
                if (!line.Contains("NVIDIA_AUDIOFX_SDK_VERSION_STRING ", StringComparison.Ordinal)) continue;
                int q1 = line.IndexOf('"');
                int q2 = line.LastIndexOf('"');
                if (q1 >= 0 && q2 > q1) return line.Substring(q1 + 1, q2 - q1 - 1);
            }
        }
        catch { /* ignore */ }
        return null;
    }
}
