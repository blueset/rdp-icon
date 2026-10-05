using Microsoft.Win32;

namespace RdpIconMonitor;

internal static class StartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RdpIconMonitor";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return OwnsCommand(key?.GetValue(ValueName), Environment.ProcessPath);
        }
    }

    public static void Set(bool enabled, string config)
    {
        using var existing = Registry.CurrentUser.OpenSubKey(KeyPath, true);
        object? previous = existing?.GetValue(ValueName);
        if (previous is not null && !OwnsCommand(previous, Environment.ProcessPath))
            throw new InvalidOperationException("The RdpIconMonitor startup entry belongs to another executable; it will not be changed.");
        if (!enabled)
        {
            existing?.DeleteValue(ValueName, false);
            return;
        }
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the application executable.");
        if (!Path.GetFileName(exe).Equals("RdpIconMonitor.exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(Path.Combine(AppContext.BaseDirectory, "published.marker")))
            throw new InvalidOperationException("Enable login startup from the published RdpIconMonitor.exe, not dotnet run/debug.");
        string command = $"\"{exe}\" --config \"{config}\"";
        if (command.Length > 260)
            throw new InvalidOperationException("The startup command exceeds Windows' 260-character limit. Use shorter paths.");
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true);
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    internal static bool OwnsCommand(object? command, string? executable)
    {
        if (executable is null || !Path.GetFileName(executable).Equals("RdpIconMonitor.exe", StringComparison.OrdinalIgnoreCase) ||
            command is not string value) return false;
        string quoted = $"\"{executable}\"";
        return value.Equals(quoted, StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith(quoted + " --config ", StringComparison.OrdinalIgnoreCase);
    }
}
