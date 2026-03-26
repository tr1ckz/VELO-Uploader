namespace VeloUploader;

static class Program
{
    [STAThread]
    static void Main()
    {
        // Single instance check
        using var mutex = new Mutex(true, "VeloUploaderSingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("VELO Uploader is already running.", "VELO Uploader",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        // Catch exceptions thrown on the UI thread (including from async void)
        Application.ThreadException += (_, e) =>
        {
            Logger.Error("Unhandled UI thread exception", e.Exception);
            LocalCompressor.KillAll();
        };
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        // Catch exceptions from non-UI threads
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Logger.Error("Fatal unhandled exception", ex);
            LocalCompressor.KillAll();
        };

        Application.Run(new TrayContext());
    }
}