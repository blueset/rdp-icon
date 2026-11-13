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
    public void InvalidConfigurationsAreRejected(string json)
    {
        var error = Record.Exception(() => IconConfiguration.Parse(json));
        Assert.True(error is InvalidDataException or System.Text.Json.JsonException);
    }
}
