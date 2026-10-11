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
    // A lock rather than a bare field. The client is read on every network call in the app and replaced
    // when the proxy changes, and `??=` is a read-modify-write: two threads reaching for it at once could
    // both build a client, and one of them would be discarded without being disposed while the other
    // published a client nobody else would ever dispose.
    static readonly object _clientLock = new();
    private static HttpClient? _client;

    /// <summary>
    /// The shared client, created on first use.
    /// </summary>
    /// <remarks>
    /// Lazy rather than initialised in App's constructor, because on Linux there is no App to run
    /// that constructor. Anything that reaches for this without calling <see cref="Initialise"/>
    /// still gets a working client.
    /// </remarks>
    internal static HttpClient Client
    {
        get
        {
            lock (_clientLock)
            {
                return _client ??= GenerateNewHttpClient();
            }
        }
    }

    /// <summary>Discards the current client and builds a fresh one.</summary>
    /// <remarks>
    /// Called when proxy settings change, because a handler cannot have its proxy swapped after
    /// construction - the whole client has to go.
    ///
    /// The old client is deliberately *not* disposed here. It is not safe to: a download may be in the
    /// middle of reading from it, and disposing out from under an active request produces an
    /// ObjectDisposedException that surfaces to the user as "Update download failed" for a change they made
    /// to their proxy settings some time earlier. The cost of not disposing is one socket per proxy change,
    /// which is nothing; the cost of disposing is a failed download.
    ///
    /// It is left for the finaliser instead, which is the correct owner for a client that may still be in
    /// use. HttpClient holds a handler whose connections close when the client is collected.
    /// </remarks>
    internal static void Regenerate()
    {
        lock (_clientLock)
        {
            // Published before the old one is abandoned, so a caller that arrives mid-swap gets the new
            // client rather than a disposed one.
            _client = GenerateNewHttpClient();
        }
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