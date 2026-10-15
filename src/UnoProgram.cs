using System;
using Uno.UI.Hosting;

namespace Kronos;

/// <summary>
/// The desktop entry point.
/// </summary>
/// <remarks>
/// WinAppSDK generated Main from App.xaml. Uno does not on desktop: the host is built here, the same way
/// spike/Kronos.UnoSpike does it.
///
/// On Linux the command-line interface in Program.cs is kept. Any argument hands the process to it, so
/// <c>kronos update --check</c> keeps working; no arguments starts the GUI.
/// </remarks>
internal static class UnoProgram
{
    [STAThread]
    public static int Main(string[] args)
    {
#if LINUX
        if (args.Length > 0)
        {
            return Program.Main(args).GetAwaiter().GetResult();
        }
#endif

        ConfigureRenderer();

        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseLinuxFrameBuffer()
            .UseMacOS()
            .UseWin32()
            .Build();

        host.Run();
        return 0;
    }

    /// <summary>
    /// GPU first, CPU as the fallback: the chosen GPU API, and software rendering if it cannot be used.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exactly one GPU API is enabled - Vulkan by default, or OpenGL if chosen in Settings - and Uno's
    /// defaults (which use OpenGL) are not relied on. Uno's Win32 host tries the enabled one
    /// (TryCreateVulkanRenderer or TryCreateGlRenderer) and, if that fails - an old driver, a remote
    /// desktop session, a virtual machine - renders in software on the CPU, so the app still starts. The
    /// other API is deliberately not tried in between: the order is the chosen GPU API, then the CPU.
    /// </para>
    /// <para>
    /// Uno reads these flags when the host is built, so the choice applies from the next start.
    /// RenderingDiagnostics reports which renderer a run actually got, in the log and on the Settings page.
    /// </para>
    /// </remarks>
    static void ConfigureRenderer()
    {
        var preference = RendererPreference.Vulkan;
        try
        {
            preference = Settings.Instance.RendererPreference;
        }
        catch (Exception)
        {
            // Unreadable settings must not stop the app starting; Vulkan is the default.
        }

        var vulkan = preference == RendererPreference.Vulkan;
        Uno.UI.FeatureConfiguration.Rendering.UseVulkanOnWin32 = vulkan;
        Uno.UI.FeatureConfiguration.Rendering.UseVulkanOnX11 = vulkan;
        Uno.UI.FeatureConfiguration.Rendering.UseOpenGLOnWin32 = vulkan == false;
        Uno.UI.FeatureConfiguration.Rendering.UseOpenGLOnX11 = vulkan == false;
    }
}
