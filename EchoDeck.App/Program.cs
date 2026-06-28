namespace EchoDeck.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Single instance — a second launch just exits (later: surface the existing window).
        using var mutex = new Mutex(initiallyOwned: true, "EchoDeck.SingleInstance", out bool createdNew);
        if (!createdNew) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
