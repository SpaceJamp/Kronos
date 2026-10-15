using System;
using System.Text;
using Kronos.Helpers;

namespace Kronos;

internal class ProxySettings
{
#if WINDOWS
    bool _hasLoaded;

    /// <summary>
    /// The Credential Manager entry the proxy is kept in. It used to be PasswordVault's ("Kronos", "proxy"),
    /// which Uno does not implement; see CredentialStore.
    /// </summary>
    internal const string CredentialTarget = "Kronos/proxy";
#endif
    public string Server { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    internal void LoadIfNeeded()
    {
#if WINDOWS
        if (_hasLoaded == false)
        {
            // Only bother trying to load once.
            _hasLoaded = true;

            try
            {
                var stored = CredentialStore.Read(CredentialTarget);
                if (stored is not null && TryDecode(stored, out var server, out var username, out var password))
                {
                    Settings.ProxySettings.Server = server;
                    Settings.ProxySettings.Username = username;
                    Settings.ProxySettings.Password = password;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        }
#endif
    }

    internal void SaveIfRequired(string? server, string? username, string? password)
    {
#if WINDOWS
        try
        {
            if (server is not null)
            {
                // Save new proxy settings
                if (CredentialStore.Write(CredentialTarget, "proxy", Encode(server, username, password)))
                {
                    Settings.ProxySettings.Server = server;
                    Settings.ProxySettings.Username = username ?? string.Empty;
                    Settings.ProxySettings.Password = password ?? string.Empty;
                }
            }
            else
            {
                // Try delete existing proxy settings
                CredentialStore.Delete(CredentialTarget);

                Settings.ProxySettings.Server = string.Empty;
                Settings.ProxySettings.Username = string.Empty;
                Settings.ProxySettings.Password = string.Empty;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
        }
#endif
    }

    /// <summary>
    /// The stored form: three base64 fields joined by '|', the same encoding the PasswordVault entry used,
    /// so a '|' inside a value cannot split it.
    /// </summary>
    internal static string Encode(string server, string? username, string? password)
    {
        var proxyServer = Convert.ToBase64String(Encoding.UTF8.GetBytes(server));
        var proxyUsername = username is not null ? Convert.ToBase64String(Encoding.UTF8.GetBytes(username)) : string.Empty;
        var proxyPassword = password is not null ? Convert.ToBase64String(Encoding.UTF8.GetBytes(password)) : string.Empty;
        return $"{proxyServer}|{proxyUsername}|{proxyPassword}";
    }

    internal static bool TryDecode(string stored, out string server, out string username, out string password)
    {
        server = username = password = string.Empty;

        var parts = stored.Split('|');
        if (parts.Length != 3)
        {
            return false;
        }

        try
        {
            server = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
            username = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
            password = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));
            return true;
        }
        catch (FormatException)
        {
            server = username = password = string.Empty;
            return false;
        }
    }
}
