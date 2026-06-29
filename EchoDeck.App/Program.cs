using System.Text;
using EchoDeck.Engine;
using EchoDeck.Engine.Interop;

namespace EchoDeck.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--selftest"))
            return SelfTest();

        // Single instance — a second launch just exits (later: surface the existing window).
        using var mutex = new Mutex(initiallyOwned: true, "EchoDeck.SingleInstance", out bool createdNew);
        if (!createdNew) return 0;

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    /// <summary>
    /// Headless smoke test: build the engine from the saved config (or defaults),
    /// run it ~3s, record a 2s mic test, and write the results to a temp file.
    /// Verifies the real audio pipeline + NVIDIA effects work on this machine.
    /// </summary>
    private static int SelfTest()
    {
        const StringComparison OIC = StringComparison.OrdinalIgnoreCase;
        var log = new StringBuilder();
        void L(string s) => log.AppendLine(s);

        try
        {
            EngineConfig cfg = ConfigStore.Load();
            using var eng = new AudioEngine();
            if (!string.IsNullOrWhiteSpace(cfg.SdkDir)) eng.SdkDir = cfg.SdkDir!;

            var ins = DeviceEnumerator.Inputs();
            var outs = DeviceEnumerator.Outputs();
            eng.InputDeviceId = cfg.InputDeviceId
                ?? ins.FirstOrDefault(d => d.IsActive && d.FriendlyName.Contains("Shure", OIC))?.Id
                ?? ins.FirstOrDefault(d => d.IsActive)?.Id;
            eng.FarEndDeviceId = cfg.FarEndDeviceId
                ?? outs.FirstOrDefault(d => d.IsActive && d.FriendlyName.Contains("Modi", OIC))?.Id;
            eng.OutputDeviceId = cfg.OutputDeviceId
                ?? outs.FirstOrDefault(d => d.IsActive && d.FriendlyName.Contains("CABLE In", OIC))?.Id;
            eng.AecEnabled = true;

            SdkStatus sdk = eng.ProbeSdk();
            L($"SDK: installed={sdk.Installed} version={sdk.Version} aec={sdk.Aec} denoiser={sdk.Denoiser} dereverb={sdk.Dereverb}");
            L($"Devices: mic={eng.InputDeviceId ?? "(none)"}  ref={eng.FarEndDeviceId ?? "(none)"}  out={eng.OutputDeviceId ?? "(none)"}");

            string? fatal = null;
            eng.Status += (_, e) => { if (e.IsError) fatal = e.Message; L($"  status: {e.State} — {e.Message}"); };
            float maxIn = 0, maxOut = 0;
            double lastLatency = 0;
            eng.InputLevel += (_, e) => { if (e.Peak > maxIn) maxIn = e.Peak; };
            eng.OutputLevel += (_, e) => { if (e.Peak > maxOut) maxOut = e.Peak; };
            eng.LatencyEstimateMs += (_, ms) => lastLatency = ms;

            eng.Start();
            Thread.Sleep(3000);
            L($"After 3s: state={eng.State}  maxInputPeak={maxIn:0.000}  maxOutputPeak={maxOut:0.000}  latency≈{lastLatency:0}ms");

            if (eng.State == EngineState.Running)
            {
                TestCaptureResult test = eng.RunMicTestAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                L($"Record test: rawWav={test.RawWav.Length} bytes, processedWav={test.ProcessedWav.Length} bytes");
            }

            eng.Stop();

            // Passthrough pass (AEC off) — confirms the output path carries the mic signal.
            eng.AecEnabled = false;
            maxIn = 0; maxOut = 0;
            eng.Start();
            Thread.Sleep(1500);
            L($"Passthrough 1.5s (AEC off): state={eng.State}  maxInputPeak={maxIn:0.000}  maxOutputPeak={maxOut:0.000}");
            eng.Stop();

            L(fatal == null ? "RESULT: OK — pipeline ran with no fatal error." : $"RESULT: FAULT — {fatal}");
        }
        catch (Exception ex)
        {
            L("RESULT: EXCEPTION — " + ex);
        }

        string outPath = Path.Combine(Path.GetTempPath(), "echodeck_selftest.txt");
        File.WriteAllText(outPath, log.ToString());
        return 0;
    }
}
