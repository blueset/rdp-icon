namespace RdpIconMonitor.Tests;

public class StartupTests
{
    [Theory]
    [InlineData("\"C:\\Tools\\RdpIconMonitor.exe\" --config \"C:\\My Files\\rules.json\"", "C:\\Tools\\RdpIconMonitor.exe", true)]
    [InlineData("\"c:\\tools\\rdpiconmonitor.exe\"", "C:\\Tools\\RdpIconMonitor.exe", true)]
    [InlineData("\"C:\\Other\\RdpIconMonitor.exe\" --config \"rules.json\"", "C:\\Tools\\RdpIconMonitor.exe", false)]
    [InlineData("\"C:\\Tools\\Other.exe\" --config \"rules.json\"", "C:\\Tools\\RdpIconMonitor.exe", false)]
    [InlineData("\"C:\\Tools\\dotnet.exe\" --config \"rules.json\"", "C:\\Tools\\dotnet.exe", false)]
    [InlineData("", "C:\\Tools\\RdpIconMonitor.exe", false)]
    public void StartupOwnershipCannotDeleteAnotherExecutableEntry(string command, string executable, bool owned) =>
        Assert.Equal(owned, StartupRegistration.OwnsCommand(command, executable));
}
