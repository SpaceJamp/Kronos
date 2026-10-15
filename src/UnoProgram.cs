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
}
