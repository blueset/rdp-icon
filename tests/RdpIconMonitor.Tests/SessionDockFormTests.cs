using RdpIconMonitor.Dock;

namespace RdpIconMonitor.Tests;

public class SessionDockFormTests
{
    [Theory]
    [InlineData(-100, -100, 100, 50)]
    [InlineData(240, 160, 120, 100)]
    public void ClampKeepsTheDockInsideTheWorkingArea(
        int desiredX,
        int desiredY,
        int expectedX,
        int expectedY)
    {
        Point result = SessionDockForm.Clamp(
            new(desiredX, desiredY),
            new(100, 50, 240, 150),
            new(220, 100));

        Assert.Equal(new Point(expectedX, expectedY), result);
    }

    [Fact]
    public void DefaultLocationUsesThePhysicalMonitorWorkingArea()
    {
        var workingArea = new Rectangle(1920, -420, 2560, 1400);
        var size = new Size(240, 126);

        Point result = SessionDockForm.DefaultLocation(workingArea, size, margin: 8);

        Assert.Equal(new Point(4232, 846), result);
    }

    [Fact]
    public void BottomAlignedLocationPreservesTheBottomEdgeAcrossCollapseAndExpand()
    {
        var workingArea = new Rectangle(0, 0, 1920, 1080);
        var expandedBounds = new Rectangle(1600, 800, 240, 200);

        Point collapsed = SessionDockForm.BottomAlignedLocation(
            expandedBounds,
            new(240, 22),
            workingArea);
        Point expanded = SessionDockForm.BottomAlignedLocation(
            new(collapsed, new Size(240, 22)),
            expandedBounds.Size,
            workingArea);

        Assert.Equal(new Point(1600, 978), collapsed);
        Assert.Equal(expandedBounds.Location, expanded);
    }
}
