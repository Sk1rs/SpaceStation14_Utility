using SS14Utility.ImageTool;
using SS14Utility.Midi;

namespace SS14Utility;

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
                "SS14 Utility", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        ApplicationConfiguration.Initialize();

        using (var disclaimer = new DisclaimerDialog())
            if (disclaimer.ShowDialog() != DialogResult.OK)
                return 0;

        Application.Run(new AppMainForm());
        return 0;
    }
}
