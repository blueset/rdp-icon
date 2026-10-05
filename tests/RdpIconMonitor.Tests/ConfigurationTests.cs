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
    public void InvalidConfigurationsAreRejected(string json)
    {
        var error = Record.Exception(() => IconConfiguration.Parse(json));
        Assert.True(error is InvalidDataException or System.Text.Json.JsonException);
    }
}
