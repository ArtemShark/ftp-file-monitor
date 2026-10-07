using FluentFTP;

class Program
{
    static async Task Main()
    {
        Settings.Load();
        Database.CreateTables();

        bool firstRun = !Database.HasSuccessfulScan();
        if (firstRun)
            Console.WriteLine("First run: building baseline, nothing will be reported as new.");

        using var cancel = new CancellationTokenSource();

        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
            Console.WriteLine("Stopping after the current scan...");
        };

        int interval = Settings.MinInterval;

        using var ftp = new AsyncFtpClient(Settings.Host, Settings.User, Settings.Password, Settings.Port);
        ftp.Config.EncryptionMode = FtpEncryptionMode.None;
        ftp.Config.DataConnectionType = FtpDataConnectionType.AutoPassive;

        while (!cancel.IsCancellationRequested)
        {
            DateTime started = DateTime.UtcNow;

            try
            {
                if (!ftp.IsConnected)
                {
                    await ftp.Connect();
                    Console.WriteLine("Connected to FTP");
                }

                List<FileItem> foundFiles = await FtpScanner.Scan(ftp);
                List<FileItem> newFiles = Database.SaveResults(foundFiles, started, firstRun);
                firstRun = false;

                foreach (FileItem file in newFiles)
                {
                    string size = file.Size == Settings.UnknownSize ? "size unknown" : file.Size + " bytes";

                    Console.WriteLine("NEW  " + file.DetectedUtc + "  [" + file.Rule + "]  " + file.Path + "  (" + size + ")");
                }

                if (newFiles.Count > 0)
                    interval = Settings.MinInterval;
                else
                    interval = Math.Min(Settings.MaxInterval, interval + Settings.IntervalStep);

                Console.WriteLine("Scan done: " + foundFiles.Count + " matched, " + newFiles.Count + " new. Next scan in " + interval + "s.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Scan failed: " + ex.GetType().Name + ": " + ex.Message);
                Database.SaveError(started, ex.ToString());

                try { await ftp.Disconnect(); } catch { }

                interval = Math.Min(Settings.MaxInterval, interval + Settings.IntervalStep);
            }

            try
            {
                await Task.Delay(interval * 1000, cancel.Token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        Console.WriteLine("Stopped.");
    }
}