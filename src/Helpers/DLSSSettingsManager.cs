using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace DLSS_Swapper.Helpers;

// Reg values defined in this file are from:
// https://github.com/NVIDIA/DLSS/tree/main/utils

internal class DLSSSettingsManager
{
    const string NGXCORE_REG_KEY = @"HKEY_LOCAL_MACHINE\SOFTWARE\NVIDIA Corporation\Global\NGXCore";

    public DLSSSettingsManager()
    {

    }

    bool RunRegAdd(string key, string name, string type, string value)
    {
        var processInfo = new ProcessStartInfo()
        {
            FileName = "reg",
            Arguments = $"add \"{key}\" /f /v {name} /t {type} /d {value}",
            Verb = "runas",
            UseShellExecute = true,
            CreateNoWindow = true
        };

        try
        {
            using (var process = Process.Start(processInfo))
            {
                if (process is not null)
                {
                    // Do not block the UI thread here. This runs with the "runas" verb, so a UAC
                    // prompt can appear, and WaitForExit would freeze the whole window until the
                    // user responded to it. This method is only ever called from a background
                    // thread via the *Async wrappers below.
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        return true;
                    }

                    throw new Exception($"Process exit code was {process.ExitCode}");
                }
            }
        }
        catch (Exception err)
        {
            Logger.Error(err, $"Could not command \"{processInfo.FileName} {processInfo.Arguments}");
        }

        return false;
    }

    /// <summary>
    /// Runs <see cref="RunRegAdd"/> off the calling thread.
    /// </summary>
    /// <remarks>
    /// The NGXCore key lives under HKEY_LOCAL_MACHINE, so writing to it needs elevation. Shelling
    /// out to reg.exe with the "runas" verb raises a UAC prompt, and the process can sit there
    /// waiting for the user. Blocking the UI thread on it made the application appear to hang
    /// whenever one of the DLSS settings was toggled.
    /// </remarks>
    Task<bool> RunRegAddAsync(string key, string name, string type, string value)
    {
        return Task.Run(() => RunRegAdd(key, name, type, value));
    }

    public Task<bool> SetShowDlssIndicatorAsync(int value)
    {
        return RunRegAddAsync(NGXCORE_REG_KEY, "ShowDlssIndicator", "REG_DWORD", value.ToString(CultureInfo.InvariantCulture));
    }

    public int GetShowDlssIndicator()
    {
        if (Registry.GetValue(NGXCORE_REG_KEY, "ShowDlssIndicator", 0) is int existingValue)
        {
            return existingValue;
        }

        return 0;
    }


    public Task<bool> SetLogLevelAsync(int logLevel)
    {
        if (logLevel == 0 || logLevel == 1 || logLevel == 2)
        {
            return RunRegAddAsync(NGXCORE_REG_KEY, "LogLevel", "REG_DWORD", $"{logLevel}");
        }

        return Task.FromResult(false);
    }

    public int GetLogLevel()
    {
        if (Registry.GetValue(NGXCORE_REG_KEY, "LogLevel", 0) is int existingValue)
        {
            return existingValue;
        }

        return 0;
    }


    public Task<bool> SetLoggingWindowAsync(bool enabled)
    {
        return RunRegAddAsync(NGXCORE_REG_KEY, "EnableConsoleLogging", "REG_DWORD", enabled ? "1" : "0");
    }

    public bool GetLoggingWindow()
    {
        if (Registry.GetValue(NGXCORE_REG_KEY, "EnableConsoleLogging", 0) is int existingValue)
        {
            return (existingValue == 1);
        }

        return false;
    }


}
