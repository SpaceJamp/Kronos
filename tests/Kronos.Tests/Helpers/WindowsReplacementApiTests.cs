using System;
using Kronos.Helpers;
using Xunit;

namespace Kronos.Tests.Helpers;

/// <summary>
/// The Win32 replacements for the WinRT APIs Uno does not implement: PackageManager (the Xbox library)
/// and PasswordVault (the proxy settings).
/// </summary>
public class WindowsReplacementApiTests
{
    [Fact]
    public void TheFamilyNameIsComputedTheWayWindowsDoes()
    {
        // The publisher hash is derived from the publisher string, so this holds on any Windows machine,
        // with or without the package installed. Both values are as they appear in a real
        // MicrosoftGame.config and in the family name Windows reports for the installed package.
        var family = AppxPackages.FamilyNameFromIdentity(
            "Microsoft.MinecraftUWP",
            "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US");

        Assert.Equal("Microsoft.MinecraftUWP_8wekyb3d8bbwe", family);
    }

    [Fact]
    public void AFamilyThatIsNotInstalledHasNoPackages()
    {
        Assert.Empty(AppxPackages.FindInstalledPackages($"Kronos.Tests.NotInstalled{Guid.NewGuid():N}_8wekyb3d8bbwe"));
    }

    [Fact]
    public void TheProxyEncodingRoundTripsValuesContainingTheSeparator()
    {
        var stored = ProxySettings.Encode("http://proxy:8080", "domain|user", "p|ss");

        Assert.True(ProxySettings.TryDecode(stored, out var server, out var username, out var password));
        Assert.Equal("http://proxy:8080", server);
        Assert.Equal("domain|user", username);
        Assert.Equal("p|ss", password);
    }

    [Fact]
    public void AMalformedStoredProxyIsRejectedRatherThanThrowing()
    {
        Assert.False(ProxySettings.TryDecode("not|base64|!!!", out _, out _, out _));
        Assert.False(ProxySettings.TryDecode("only-one-field", out _, out _, out _));
    }

    [Fact]
    public void ACredentialCanBeWrittenReadBackAndDeleted()
    {
        // A throwaway target, removed in the finally, so this never touches the real proxy entry.
        var target = $"Kronos.Tests/{Guid.NewGuid():N}";
        try
        {
            Assert.Null(CredentialStore.Read(target));

            Assert.True(CredentialStore.Write(target, "test", "secret value"));
            Assert.Equal("secret value", CredentialStore.Read(target));

            Assert.True(CredentialStore.Write(target, "test", "replaced"));
            Assert.Equal("replaced", CredentialStore.Read(target));
        }
        finally
        {
            CredentialStore.Delete(target);
        }

        Assert.Null(CredentialStore.Read(target));
    }
}
