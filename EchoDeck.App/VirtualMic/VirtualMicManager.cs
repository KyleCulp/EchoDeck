using System.Diagnostics;
using EchoDeck.Engine;

namespace EchoDeck.App.VirtualMic;

/// <summary>One virtual-mic provider's detected state + how apps consume it.</summary>
public sealed class VirtualMicInfo
{
    public required string Name { get; init; }
    public required string DownloadUrl { get; init; }
    public bool Installed { get; init; }
    /// <summary>The render endpoint EchoDeck should output to (its "speaker" side), if present.</summary>
    public string? RenderEndpointId { get; init; }
    /// <summary>What the user selects as their mic in other apps (the capture side).</summary>
    public required string CaptureEndpointName { get; init; }
}

/// <summary>
/// Detects which virtual-mic provider is available and drives installation of the
/// bundled open-source Virtual-Audio-Driver. A real virtual mic needs a kernel audio
/// driver — EchoDeck can't be one itself — so this manages an external/bundled driver.
///
/// Detection is pure (reads the device snapshot). Install runs the bundled driver's
/// own installer elevated; if no package is bundled, the UI points to the download.
/// </summary>
public sealed class VirtualMicManager
{
    public const string VadReleases = "https://github.com/VirtualDrivers/Virtual-Audio-Driver/releases";
    private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

    public VirtualMicInfo VirtualAudioDriver { get; }
    public VirtualMicInfo VbCable { get; }

    /// <summary>The provider to actually use — prefer the OSS driver, else VB-Cable, else none.</summary>
    public VirtualMicInfo? Active =>
        VirtualAudioDriver.Installed ? VirtualAudioDriver :
        VbCable.Installed ? VbCable : null;

    public VirtualMicManager(IReadOnlyList<DeviceInfo> inputs, IReadOnlyList<DeviceInfo> renders)
    {
        DeviceInfo? vadRender = renders.FirstOrDefault(d => d.FriendlyName.Contains("Virtual Audio Driver", OIC));
        VirtualAudioDriver = new VirtualMicInfo
        {
            Name = "Virtual Audio Driver",
            DownloadUrl = VadReleases,
            Installed = vadRender != null || inputs.Any(d => d.FriendlyName.Contains("Virtual Mic", OIC)),
            RenderEndpointId = vadRender?.Id,
            CaptureEndpointName = "Virtual Mic"
        };

        DeviceInfo? cableRender = renders.FirstOrDefault(d => d.FriendlyName.Contains("CABLE In", OIC));
        VbCable = new VirtualMicInfo
        {
            Name = "VB-Cable",
            DownloadUrl = "https://vb-audio.com/Cable/",
            Installed = cableRender != null && inputs.Any(d => d.FriendlyName.Contains("CABLE Output", OIC)),
            RenderEndpointId = cableRender?.Id,
            CaptureEndpointName = "CABLE Output"
        };
    }

    private static string DriverDir => Path.Combine(AppContext.BaseDirectory, "drivers", "VirtualAudioDriver");

    /// <summary>True when a driver package has been dropped into drivers\VirtualAudioDriver.</summary>
    public static bool BundledDriverPresent() =>
        Directory.Exists(DriverDir) && Directory.EnumerateFileSystemEntries(DriverDir).Any();

    /// <summary>
    /// Install the bundled Virtual-Audio-Driver elevated. Returns null on success,
    /// the sentinel "no-package"/"no-installer" if nothing is bundled (caller opens the
    /// download page), or an error message otherwise.
    /// </summary>
    public static async Task<string?> InstallVirtualAudioDriverAsync()
    {
        if (!Directory.Exists(DriverDir)) return "no-package";

        string? script = FirstWithExt(".bat", ".cmd");
        string? exe = Directory.EnumerateFiles(DriverDir).FirstOrDefault(f => f.EndsWith(".exe", OIC));
        string? inf = FirstWithExt(".inf");

        try
        {
            if (script != null) return await RunElevated("cmd.exe", $"/c \"\"{script}\"\"");
            if (exe != null) return await RunElevated(exe, "");
            if (inf != null) return await RunElevated("pnputil.exe", $"/add-driver \"{inf}\" /install");
            return "no-installer";
        }
        catch (System.ComponentModel.Win32Exception) { return "Install cancelled."; } // UAC declined
        catch (Exception ex) { return ex.Message; }
    }

    private static string? FirstWithExt(params string[] exts) =>
        Directory.EnumerateFiles(DriverDir).FirstOrDefault(f => exts.Any(e => f.EndsWith(e, OIC)));

    private static async Task<string?> RunElevated(string file, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            WorkingDirectory = DriverDir,
            UseShellExecute = true,
            Verb = "runas" // triggers the UAC prompt
        };
        using Process? p = Process.Start(psi);
        if (p == null) return "Could not start the installer.";
        await p.WaitForExitAsync();
        return p.ExitCode == 0 ? null : $"Installer exited with code {p.ExitCode}.";
    }
}
