using NAudio.CoreAudioApi;

namespace EchoDeck.Engine;

/// <summary>An audio endpoint, identified by its stable MMDevice ID (survives renames).</summary>
public sealed record DeviceInfo(string Id, string FriendlyName, DataFlow Flow, DeviceState State)
{
    public bool IsActive => State == DeviceState.Active;

    public override string ToString() => State switch
    {
        DeviceState.Active => FriendlyName,
        DeviceState.Unplugged => $"{FriendlyName}  (unplugged)",
        DeviceState.Disabled => $"{FriendlyName}  (disabled)",
        _ => $"{FriendlyName}  (unavailable)"
    };
}

public static class DeviceEnumerator
{
    // Mirror the Windows Sound panel: show active + disabled + unplugged endpoints.
    private const DeviceState States = DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged;

    public static List<DeviceInfo> Inputs() => List(DataFlow.Capture);
    public static List<DeviceInfo> Outputs() => List(DataFlow.Render);

    private static List<DeviceInfo> List(DataFlow flow)
    {
        using var en = new MMDeviceEnumerator();
        var result = new List<DeviceInfo>();
        foreach (MMDevice d in en.EnumerateAudioEndPoints(flow, States))
            result.Add(new DeviceInfo(d.ID, FriendlyName(d), flow, d.State));

        // Active devices first, then alphabetical.
        result.Sort((a, b) =>
            a.IsActive != b.IsActive
                ? (a.IsActive ? -1 : 1)
                : string.Compare(a.FriendlyName, b.FriendlyName, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private static string FriendlyName(MMDevice d)
    {
        try { return d.FriendlyName; }
        catch { try { return d.DeviceFriendlyName; } catch { return d.ID; } }
    }
}
