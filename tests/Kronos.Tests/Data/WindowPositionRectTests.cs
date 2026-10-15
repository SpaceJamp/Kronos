using System.Text.Json;
using Kronos.Data;
using Microsoft.UI.Windowing;
using Xunit;

namespace Kronos.Tests.Data;

/// <summary>
/// The saved window size, position and state, which settings.json carries from one launch to the next.
/// </summary>
public class WindowPositionRectTests
{
    [Fact]
    public void TheSavedStateNumbersMatchWhatTheWinUIBuildWrote()
    {
        // State is serialised as a number. The WinUI build wrote WinUI's values, and the Uno build reads
        // them back from the same settings.json, so Uno's enum has to number the states identically or a
        // user's saved Restored window would come back as something else after the cut-over.
        Assert.Equal(0, (int)OverlappedPresenterState.Maximized);
        Assert.Equal(1, (int)OverlappedPresenterState.Minimized);
        Assert.Equal(2, (int)OverlappedPresenterState.Restored);
    }

    static Win32.RECT Rect(int left, int top, int width, int height) =>
        new() { Left = left, Top = top, Right = left + width, Bottom = top + height };

    [Fact]
    public void AWindowThatFitsIsLeftWhereItIs()
    {
        var fitted = Kronos.Helpers.WindowPlacementHelper.FitToWorkArea(Rect(100, 100, 1200, 800), Rect(0, 0, 2560, 1440));

        Assert.Equal(Rect(100, 100, 1200, 800), fitted);
    }

    [Fact]
    public void AWindowLargerThanTheScreenIsShrunkToIt()
    {
        // The case the AppWindow restore produced: a window saved larger than its monitor - and on Uno,
        // 1.5 times larger again on every launch at 150% scaling.
        var fitted = Kronos.Helpers.WindowPlacementHelper.FitToWorkArea(Rect(418, 418, 4320, 2387), Rect(0, 0, 3840, 2100));

        Assert.Equal(Rect(0, 0, 3840, 2100), fitted);
    }

    [Fact]
    public void AWindowPartlyOffTheScreenIsMovedFullyOntoIt()
    {
        // Off the left edge of a monitor to the left of the primary, as when a monitor is rearranged.
        var fitted = Kronos.Helpers.WindowPlacementHelper.FitToWorkArea(Rect(-3000, 2000, 1200, 800), Rect(-2560, 0, 2560, 1440));

        Assert.Equal(Rect(-2560, 640, 1200, 800), fitted);
    }

    [Fact]
    public void AStateWrittenByTheWinUIBuildReadsBackAsRestored()
    {
        var json = """{"x":418,"y":418,"width":1600,"height":900,"State":2}""";

        var rect = JsonSerializer.Deserialize<WindowPositionRect>(json)!;

        Assert.Equal(OverlappedPresenterState.Restored, rect.State);
    }
}
