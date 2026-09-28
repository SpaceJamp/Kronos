using Chronos.Helpers;

namespace Chronos.Tests.Helpers;

public class PathHelpersTests
{
    [Theory]
    [InlineData(@"C:\Games\MyGame", @"C:\Games\MyGame")]
    [InlineData(@"C:\Games\MyGame\", @"C:\Games\MyGame")]
    [InlineData(@"C:\Games\..\Games\MyGame", @"C:\Games\MyGame")]
    [InlineData(@"C:\Games\MyGame\.\", @"C:\Games\MyGame")]
    public void NormalizePath_ProducesTheSameResultRegardlessOfTrailingSeparator(string input, string expected)
    {
        // The whole point of NormalizePath is that two paths which refer to the same directory
        // normalise to the same string, so install paths from different launchers compare equal.
        Assert.Equal(expected, PathHelpers.NormalizePath(input));
    }

    [Fact]
    public void NormalizePath_IsIdempotent()
    {
        var once = PathHelpers.NormalizePath(@"C:\Games\MyGame\");
        Assert.Equal(once, PathHelpers.NormalizePath(once));
    }

    [Fact]
    public void NormalizePath_PreservesDriveRoot()
    {
        // Known bug: TrimEnd removes the separator from "C:\" and leaves "C:", which Windows treats
        // as *drive relative* (i.e. relative to the current directory on that drive) rather than
        // the root. Directory.Exists("C:") is false, so a game installed at a drive root was
        // treated as missing.
        var root = Path.GetPathRoot(Path.GetTempPath())!;

        var result = PathHelpers.NormalizePath(root);

        Assert.Equal(root, result);
        Assert.True(Directory.Exists(result),
            $"{result} should still resolve to an existing directory.");
    }

    [Fact]
    public void NormalizePath_IsCasePreserving()
    {
        // NormalizePath must not change case: callers compare with OrdinalIgnoreCase themselves, and
        // silently lowercasing would corrupt paths for case sensitive volumes.
        Assert.Equal(@"C:\Games\MyGame", PathHelpers.NormalizePath(@"C:\Games\MyGame"));
    }
}
