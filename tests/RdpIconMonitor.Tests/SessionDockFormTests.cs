using RdpIconMonitor.Dock;
using RdpIconMonitor.Windows;

namespace RdpIconMonitor.Tests;

public class SessionDockFormTests
{
    [Fact]
    public void RecreatedRowsKeepTheirFontWhenFormFontChanges()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new SessionDockForm(_ => true, _ => true);
                var area = Screen.PrimaryScreen!.WorkingArea;
                SessionDockScreenState State(params string[] labels) => new(
                    "test", area, area,
                    labels.Select((label, index) => new SessionDockItemState(
                        label, label, null, true, false, new WindowIdentity(index + 1, 1, 1))).ToArray());
                form.Update(State("First"));
                using var scaledFont = new Font("Segoe UI", 5f, FontStyle.Bold);
                form.Font = scaledFont;
                form.Update(State("First", "Second"));
                var buttons = form.Controls.OfType<Button>()
                    .Where(button => button.Text is "First" or "Second").ToArray();
                Assert.Equal(2, buttons.Length);
                Assert.All(buttons, button => Assert.Equal(10f, button.Font.SizeInPoints));
            }
            catch (Exception exception) { error = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

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
