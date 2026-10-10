using System;
using System.Net;
using System.Net.Http;
using System.Reflection;

namespace Kronos;

/// <summary>
/// The shared <see cref="HttpClient"/> used for every network request: DLL downloads, zip fetches,
/// the Steam store API and the updater's release check.
/// </summary>
/// <remarks>
/// This lived on the WinUI <c>App</c> singleton, which meant anything outside the UI - the file
/// downloader, the cover URL resolver - could only reach it through <c>App.CurrentApp</c>. That put
/// a hard dependency on a WinUI Application in code the Linux target also needs, so those files
/// were excluded from the Linux build. Nothing here is Windows-specific, so it does not need to be.
///
/// One client is shared rather than created per call, deliberately: a new HttpClient per download
/// exhausts sockets under load, and the 30 minute timeout below is only safe on a long-lived one.
/// It is replaced wholesale when proxy settings change, which is why this is settable.
/// </remarks>
internal static class Http
{
    private static HttpClient? _client;

    /// <summary>
    /// The shared client, created on first use.
    /// </summary>
    /// <remarks>
    /// Lazy rather than initialised in App's constructor, because on Linux there is no App to run
    /// that constructor. Anything that reaches for this without calling <see cref="Initialise"/>
    /// still gets a working client.
    /// </remarks>
    internal static HttpClient Client => _client ??= GenerateNewHttpClient();

    /// <summary>Discards the current client and builds a fresh one.</summary>
    /// <remarks>
    /// Called when proxy settings change, because a handler cannot have its proxy swapped after
    /// construction - the whole client has to go.
    /// </remarks>
    internal static void Regenerate()
    {
        _client?.Dispose();
        _client = GenerateNewHttpClient();
    }

    static HttpClient GenerateNewHttpClient()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version();
        var versionString = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";

        var httpClientHandler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = true,
        };

        Settings.ProxySettings.LoadIfNeeded();

        if (string.IsNullOrWhiteSpace(Settings.ProxySettings.Server) == false)
        {
            try
            {
                var server = Settings.ProxySettings.Server;
                if (server.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    server.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var proxy = new WebProxy
                    {
                        BypassProxyOnLocal = false,
                        UseDefaultCredentials = false,
                        Address = new Uri(server),
                    };

                    if (string.IsNullOrWhiteSpace(Settings.ProxySettings.Username) == false &&
                        string.IsNullOrWhiteSpace(Settings.ProxySettings.Password) == false)
                    {
                        proxy.Credentials = new NetworkCredential(
                            Settings.ProxySettings.Username,
                            Settings.ProxySettings.Password);
                    }

                    httpClientHandler.UseProxy = true;
                    httpClientHandler.Proxy = proxy;
                }
                else
                {
                    Logger.Error($"Tried to set proxy with server address \"{server}\"");
                }
            }
            catch (Exception err)
            {
                Logger.Error(err, "Unable to set proxy for HttpClient");
            }
        }

        var newHttpClient = new HttpClient(httpClientHandler);
        newHttpClient.DefaultRequestHeaders.Add("User-Agent", $"dlss-swapper/{versionString}");
        newHttpClient.Timeout = TimeSpan.FromMinutes(30);
        newHttpClient.DefaultRequestVersion = new Version(2, 0);
        newHttpClient.DefaultRequestHeaders.ConnectionClose = true;

        return newHttpClient;
    }
}