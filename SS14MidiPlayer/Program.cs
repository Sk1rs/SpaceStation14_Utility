namespace SS14MidiPlayer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--selftest"))
            return SelfTest.Run(args);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            MessageBox.Show("Непредвиденная ошибка:" + Environment.NewLine + e.ExceptionObject,
                "SS14 MIDI плеер", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
