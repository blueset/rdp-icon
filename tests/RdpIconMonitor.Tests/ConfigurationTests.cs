using RdpIconMonitor.Configuration;

namespace RdpIconMonitor.Tests;

public class ConfigurationTests
{
    [Fact]
    public void OptionalFieldsAndHueAreNormalized()
    {
        var config = IconConfiguration.Parse("""
            {"instances":[{"name":"Server","hue":-60,"smallText":" ","largeText":" B "},{"name":"Other"}]}
            """);
        Assert.Equal(300, config.Instances[0].Hue);
        Assert.Null(config.Instances[0].SmallText);
        Assert.Equal(" B ", config.Instances[0].LargeText);
        Assert.Null(config.Instances[1].Hue);
        Assert.Null(config.Instances[1].LargeText);
        Assert.Null(config.Dock);
        Assert.Null(config.Instances[1].Label);
    }

    [Fact]
    public void DockAndDisplayLabelsAreOptionalAndNormalized()
    {
        var config = IconConfiguration.Parse("""
            {"dock":{"enabled":true,"showOnAllMonitors":true},"instances":[{"name":"Server","label":" Main "},{"name":"Other","label":" "}]}
            """);
        Assert.True(config.Dock!.Enabled);
        Assert.True(config.Dock.ShowOnAllMonitors);
        Assert.Equal(" Main ", config.Instances[0].Label);
        Assert.Null(config.Instances[1].Label);
    }

    [Fact]
    public void HostAppsAreOptionalAndNormalized()
    {
        var config = IconConfiguration.Parse("""
            {"instances":[],"hostApps":[
              {"name":"Teams","process":"ms-teams","label":" ","hue":-30,"taskbarAlerts":true,"enabled":false},
              {"name":"Outlook","process":"olk","enabled":true}
            ]}
            """);

        Assert.Equal(2, config.HostApps!.Length);
        Assert.Equal(330, config.HostApps[0].Hue);
        Assert.Null(config.HostApps[0].Label);
        Assert.True(config.HostApps[0].TaskbarAlerts);
        Assert.False(config.HostApps[0].Enabled);
        Assert.True(config.HostApps[1].Enabled);
        Assert.False(config.HostApps[1].TaskbarAlerts);
        Assert.Empty(IconConfiguration.Parse("""{"instances":[],"hostApps":null}""").HostApps!);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("""{"instances":null}""")]
    [InlineData("""{"instances":[null]}""")]
    [InlineData("""{"instances":[{"name":""}]}""")]
    [InlineData("""{"instances":[{"name":"S"},{"name":"s"}]}""")]
    [InlineData("""{"instances":[{"name":"S","hue":"red"}]}""")]
    [InlineData("""{"instances":[{"name":"S","huue":30}]}""")]
    [InlineData("""{"instances":[{"name":"S","largeText":"A\nB"}]}""")]
    [InlineData("""{"instances":[{"name":"S","label":"A\nB"}]}""")]
    [InlineData("""{"dock":{"enabled":"yes"},"instances":[]}""")]
    [InlineData("""{"dock":{"extra":true},"instances":[]}""")]
    [InlineData("""{"instances":[],"hostApps":[null]}""")]
    [InlineData("""{"instances":[],"hostApps":[{"name":"","process":"olk"}]}""")]
    [InlineData("""{"instances":[],"hostApps":[{"name":"Outlook","process":" "}]}""")]
    [InlineData("""{"instances":[],"hostApps":[{"name":"Outlook","process":"olk.exe"}]}""")]
    [InlineData("""{"instances":[],"hostApps":[{"name":"Outlook","process":"olk"},{"name":"outlook","process":"olk"}]}""")]
    [InlineData("""{"instances":[],"hostApps":[{"name":"Outlook","process":"olk","hue":"blue"}]}""")]
    [InlineData("""{"instances":[],"hostApps":[{"name":"Outlook","process":"olk","label":"A\nB"}]}""")]
    [InlineData("""{"instances":[],"hostApps":[{"name":"Outlook","process":"olk","extra":1}]}""")]
    public void InvalidConfigurationsAreRejected(string json)
    {
        var error = Record.Exception(() => IconConfiguration.Parse(json));
        Assert.True(error is InvalidDataException or System.Text.Json.JsonException);
    }
}
