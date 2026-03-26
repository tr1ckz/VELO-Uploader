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
        Application.Run(new TrayContext());
    }
}