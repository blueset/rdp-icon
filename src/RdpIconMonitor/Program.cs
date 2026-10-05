namespace RdpIconMonitor;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "--render-only")
            {
                var configuration = Configuration.IconConfiguration.Parse(File.ReadAllText(args[1]));
                Rendering.IconExport.Write(configuration, args[2]);
                return 0;
            }
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            string identity = $"Local\\RdpIconMonitor.{process.SessionId}";
            if (args.SequenceEqual(new[] { "--stop" }))
            {
                if (!EventWaitHandle.TryOpenExisting(identity + ".Stop", out var running))
                    throw new InvalidOperationException("RDP icon monitor is not running in this desktop session.");
                using (running) running.Set();
                return 0;
            }
            if (args.SequenceEqual(new[] { "--help" }))
            {
                Console.WriteLine("RdpIconMonitor [--config <file>]\nRdpIconMonitor --render-only <file> <output-folder>\nRdpIconMonitor --stop");
                return 0;
            }
            if (args.Length != 0 && !(args.Length == 2 && args[0] == "--config"))
                throw new ArgumentException("Use --config <file>, --render-only <file> <output-folder>, --stop, or --help.");
            string userDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RdpIconMonitor");
            Directory.CreateDirectory(userDirectory);
            string config = Path.GetFullPath(args.Length == 2 ? args[1] : Path.Combine(userDirectory, "instances.json"));
            if (args.Length == 0 && !File.Exists(config))
                File.WriteAllText(config, "{\n  \"instances\": []\n}\n");
            using var mutex = new Mutex(true, identity, out bool first);
            if (!first) throw new InvalidOperationException("RDP icon monitor is already running in this desktop session.");
            using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, identity + ".Stop");
            stop.Reset();
            ApplicationConfiguration.Initialize();
            using var application = new TrayApplicationContext(config, userDirectory, stop);
            Application.Run(application);
            return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
            or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine(error.Message);
            if (args.Length == 0 || args[0] == "--config")
                MessageBox.Show(error.Message, "RDP icon monitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}