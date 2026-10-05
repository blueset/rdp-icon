namespace RdpIconMonitor;

internal sealed class Diagnostics
{
    public string DirectoryPath { get; }
    public string LogPath => Path.Combine(DirectoryPath, "monitor.log");
    private readonly Lock gate = new();
    private readonly Dictionary<string, DateTime> lastMessages = [];

    public Diagnostics(string directory)
    {
        DirectoryPath = directory;
        Directory.CreateDirectory(directory);
    }

    public void Report(string message)
    {
        lock (gate)
        {
            var now = DateTime.UtcNow;
            if (lastMessages.TryGetValue(message, out var previous) && now - previous < TimeSpan.FromMinutes(1)) return;
            if (lastMessages.Count > 256) lastMessages.Clear();
            lastMessages[message] = now;
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:o} {message}{Environment.NewLine}");
        }
    }
}
