namespace RdpIconMonitor.Configuration;

internal sealed class ConfigurationWatcher : IDisposable
{
    private readonly FileSystemWatcher watcher;
    public ConfigurationWatcher(string path, Action changed, Action<string> error)
    {
        watcher = new(Path.GetDirectoryName(path)!, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        watcher.Changed += (_, _) => changed();
        watcher.Created += (_, _) => changed();
        watcher.Deleted += (_, _) => changed();
        watcher.Renamed += (_, _) => changed();
        watcher.Error += (_, args) =>
        {
            error($"Configuration watcher error; periodic reload checks remain active: {args.GetException().Message}");
            changed();
        };
        watcher.EnableRaisingEvents = true;
    }
    public void Dispose() => watcher.Dispose();
}
