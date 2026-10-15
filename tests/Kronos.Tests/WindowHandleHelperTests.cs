using System.IO;
using Kronos.Helpers;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// The owner handle for the Win32 file and folder pickers.
/// </summary>
/// <remarks>
/// On Uno, <c>WindowNative.GetWindowHandle</c> returns 0x1. IFileDialog.Show rejects that with E_INVALIDARG
/// before showing anything, and every caller caught and logged it, so every picker in the app silently did
/// nothing. Measured directly: FileSystemHelper.OpenFile(0x1, ...) throws ArgumentException 0x80070057.
/// </remarks>
public class WindowHandleHelperTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)] // what Uno's WindowNative.GetWindowHandle returns
    [InlineData(0xDEAD0)]
    public void HandlesThatAreNotWindowsAreRejected(long handle)
    {
        Assert.False(WindowHandleHelper.IsWindowOfThisProcess((nint)handle));
    }

    [Fact]
    public void WithNoWindowItNeverReturnsAHandleThatIsNotAWindowOfThisProcess()
    {
        // The test host has no windows of its own, so the honest answer is 0: an unowned dialog, which
        // still opens. What it must never do is pass through a bogus handle like 0x1.
        var owner = WindowHandleHelper.GetOwnerHandle(null);

        Assert.True(owner == 0 || WindowHandleHelper.IsWindowOfThisProcess(owner), $"returned 0x{owner:X}");
    }

    [Fact]
    public void NoPickerCallSiteAsksWindowNativeDirectly()
    {
        // Every picker goes through WindowHandleHelper. A new call site written the WinAppSDK way would
        // compile, and silently never open on Uno.
        var src = Path.GetDirectoryName(RepositoryFile("src", "Kronos.csproj"))!;
        var offenders = new System.Collections.Generic.List<string>();

        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, file);
            if (relative.StartsWith("obj", System.StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("bin", System.StringComparison.OrdinalIgnoreCase) ||
                relative.EndsWith("WindowHandleHelper.cs", System.StringComparison.Ordinal) ||
                // RepaintWindow sends a Win32 message to the window; it is not a picker, and a bad
                // handle there is a no-op rather than a dialog that never opens.
                relative.EndsWith("WindowManager.cs", System.StringComparison.Ordinal))
            {
                continue;
            }

            if (File.ReadAllText(file).Contains("WindowNative.GetWindowHandle", System.StringComparison.Ordinal))
            {
                offenders.Add(relative);
            }
        }

        Assert.True(offenders.Count == 0, "Use WindowHandleHelper.GetOwnerHandle instead of WindowNative.GetWindowHandle in: " + string.Join(", ", offenders));
    }

    static string RepositoryFile(params string[] segments)
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && Directory.Exists(Path.Combine(dir.FullName, "src")) == false)
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, Path.Combine(segments));
    }
}
