namespace EchoDeck.Engine;

public enum EngineState
{
    Stopped,
    Starting,
    Running,
    Faulted
}

/// <summary>Input/output level for a VU meter (one processed block).</summary>
public sealed class LevelEventArgs : EventArgs
{
    public float Rms { get; init; }
    public float Peak { get; init; }
    public bool Clip { get; init; }
}

/// <summary>Engine state change / status message for the UI.</summary>
public sealed class EngineStatusEventArgs : EventArgs
{
    public EngineState State { get; init; }
    public string Message { get; init; } = "";
    public bool IsError { get; init; }
}

/// <summary>Result of a mic test: raw input and processed output as in-memory WAVs.</summary>
public sealed record TestCaptureResult(byte[] RawWav, byte[] ProcessedWav, NAudio.Wave.WaveFormat Format);
