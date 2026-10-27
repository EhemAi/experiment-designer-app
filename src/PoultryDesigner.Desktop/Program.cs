namespace PoultryDesigner.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--startup-check")
        {
            var launchTime = System.Diagnostics.Stopwatch.StartNew();
            long displayedMilliseconds = 0;
            bool opened = false;
            using var checkStartup = new StartupContext(main =>
            {
                displayedMilliseconds = launchTime.ElapsedMilliseconds;
                opened = main.Visible && main.Icon is not null && displayedMilliseconds >= SplashForm.MinimumDisplayMilliseconds;
                main.BeginInvoke(main.CloseForSmoke);
            });
            Application.Run(checkStartup);
            File.WriteAllText(args[1], opened ? $"PASS: five-second animated splash transitions to the branded main window after {displayedMilliseconds} ms and closes cleanly.\n" : "FAIL: startup transition or minimum display time.\n");
            return opened ? 0 : 1;
        }
        if (args.Length >= 2 && args[0] == "--smoke")
        {
            // Test exceptions must fail the run, rather than open an offscreen error dialog.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            return DesktopSmoke.Run(args[1]);
        }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "Experiment Designer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        using var startup = new StartupContext(); Application.Run(startup); return 0;
    }
}
