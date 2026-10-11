using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Kronos.Helpers;

/// <summary>
/// Replaces a running portable build with a newer one.
/// </summary>
/// <remarks>
/// The installer path launches an <c>-installer.exe</c> and exits, letting the installer own the
/// files. A portable build has no installer, so something has to do the copy - and it cannot be this
/// process, because Windows will not let a running executable be overwritten. So the copy is handed
/// to a detached PowerShell script that waits for this process to exit first.
///
/// Handing off to a script rather than re-launching the app with a command line argument is deliberate.
/// WinUI generates the entry point, so there is no Main to intercept an argument in before the UI is
/// built. Waiting on the process id works from outside that constraint.
///
/// The script backs up the executable before replacing it and puts it back if the copy does not produce
/// the executable the archive actually contained, because the one file that cannot be recovered any
/// other way is the one you need to launch.
///
/// Two things it deliberately does not do, both of which used to happen. It does not abort the whole
/// update because one file could not be written - that is what left users on a stale build with no
/// indication that anything was wrong - and it does not touch StoredData, which is where the user's
/// game library and settings live in the same directory the update writes to.
///
/// Purely textual parts - asset selection and script generation - are separated from the file work so
/// they can be asserted on without touching a disk.
/// </remarks>
public static class PortableSelfUpdate
{
    /// <summary>Name of the executable to hand off to, relative to the extracted update.</summary>
    const string EntryPointFileName = "Kronos.exe";

    /// <summary>Name of the rollback copy, kept beside the executable only while the copy runs.</summary>
    const string BackupSuffix = ".updatebak";

    /// <summary>
    /// The user's live data folder, which sits in the same directory as the executable.
    /// </summary>
    /// <remarks>
    /// This is <c>Storage.StoragePath</c>, which is <c>AppContext.BaseDirectory/StoredData</c> for a
    /// portable build - the very directory the update writes into. The release archive contains a
    /// StoredData folder too, holding the blank database and default settings that the build smoke test
    /// generated. Copying it over the installation replaced the user's game library and settings with
    /// an empty database on every update, which is silent and total data loss.
    ///
    /// Skipping the folder wholesale is deliberate rather than skipping individual files: everything in
    /// it is either user data or a cache that the app refetches from GitHub on startup, so there is
    /// nothing in it worth carrying over from the archive.
    ///
    /// The value is pinned against Storage.cs by a source-scan test in
    /// PortableSelfUpdateTests.DataFolderNameMatchesTheOneStorageUses, because if the two drift apart the
    /// copy quietly starts overwriting user data again and nothing else would notice.
    /// </remarks>
    internal const string DataFolderName = "StoredData";

    /// <summary>
    /// Picks the portable archive out of a release's assets.
    /// </summary>
    /// <remarks>
    /// Requires a digest. Without one the download cannot be verified at all, and an update mechanism
    /// that silently accepts unverified files is worse than no update mechanism.
    /// </remarks>
    internal static Data.GitHub.GitHubReleaseAsset? FindPortableAsset(
        Data.GitHub.GitHubReleaseAsset[] assets)
    {
        if (assets is null || assets.Length == 0)
        {
            return null;
        }

        var candidates = assets
            .Where(asset => asset.Name.EndsWith("-portable.zip", StringComparison.OrdinalIgnoreCase))
            .Where(asset => string.IsNullOrWhiteSpace(asset.Digest) == false)
            .ToList();

        if (candidates.Count > 1)
        {
            // Two archives and no way to tell which is meant. Refusing is the only safe answer;
            // guessing could install the wrong build.
            return null;
        }

        return candidates.SingleOrDefault();
    }

    /// <summary>
    /// The PowerShell script that performs the replacement once the app has exited.
    /// </summary>
    /// <param name="sourceDirectory">Directory the archive was extracted into.</param>
    /// <param name="targetDirectory">Directory the running app lives in.</param>
    /// <param name="processId">Process to wait for before touching anything.</param>
    /// <param name="relaunch">Whether to start the updated app afterwards.</param>
    public static string BuildHandoffScript(
        string sourceDirectory,
        string targetDirectory,
        int processId,
        bool relaunch)
    {
        var source = Quote(sourceDirectory);
        var target = Quote(targetDirectory);
        var entryPoint = Quote(EntryPointFileName);
        var backup = Quote(EntryPointFileName + BackupSuffix);

        // $PID is a PowerShell automatic variable holding the *script's* own id, so the waiting
        // variable must not be called pid. Shadowing it would make this wait on itself, forever.
        var waitOn = "kronosProcessId";

        var script = new StringBuilder();
        // Not 'Stop'. A Stop preference turns any single unwritable file into a terminating error, and
        // that is how this update used to fail: it copied a few files, hit one it could not write, threw,
        // restored the old executable, and left the app relaunching the previous build. Nothing on screen
        // said so. Errors are collected per file here instead, and the executable is checked afterwards.
        script.AppendLine("$ErrorActionPreference = 'Continue'");

        // Wait for the app to exit. SilentlyContinue because the process may already be gone by the
        // time this runs, which is the common case rather than an error.
        script.AppendLine(CultureInfo.InvariantCulture, $"Wait-Process -Id {processId} -ErrorAction SilentlyContinue");
        script.AppendLine("Start-Sleep -Milliseconds 750");
        script.AppendLine(CultureInfo.InvariantCulture, $"${waitOn} = {source}");
        script.AppendLine($"$target = {target}");
        script.AppendLine($"$entry = Join-Path $target {entryPoint}");
        script.AppendLine("$backup = Join-Path $target $backup");
        script.AppendLine($"$dataFolder = {Quote(DataFolderName)}");
        script.AppendLine();

        script.AppendLine("# Rollback copy first: the executable is the one file that cannot be re-obtained.");
        script.AppendLine("$updated = $false");
        script.AppendLine("$copied = $false");
        script.AppendLine("try {");
        script.AppendLine("    Copy-Item -LiteralPath $entry -Destination $backup -Force");
        script.AppendLine("    $copied = $true");
        script.AppendLine();
        script.AppendLine("    # One file at a time, and the user's data folder is never touched.");
        script.AppendLine("    #");
        script.AppendLine("    # The archive ships a StoredData folder holding the blank database and default");
        script.AppendLine("    # settings that the build's smoke test generated. The installed copy holds the user's");
        script.AppendLine("    # game library and their settings, in the same directory the update writes into, so");
        script.AppendLine("    # copying that folder over the installation replaced their data with an empty");
        script.AppendLine("    # database on every update. Silent, and total.");
        script.AppendLine("    $failed = New-Object System.Collections.ArrayList");
        script.AppendLine($"    Get-ChildItem -LiteralPath ${waitOn} -Recurse -File | ForEach-Object {{");
        script.AppendLine("        $relative = $_.FullName.Substring($kronosProcessId.Length).TrimStart([char]'\\')");
        script.AppendLine("        if ($relative.Split([char]'\\')[0] -eq $dataFolder) { return }");
        script.AppendLine("        $destination = Join-Path $target $relative");
        script.AppendLine("        $folder = Split-Path $destination -Parent");
        script.AppendLine("        if (-not (Test-Path $folder)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }");
        script.AppendLine("        try { Copy-Item -LiteralPath $_.FullName -Destination $destination -Force }");
        script.AppendLine("        catch { [void]$failed.Add($relative) }");
        script.AppendLine("    }");
        script.AppendLine();
        script.AppendLine("    # Verified rather than assumed. The executable is the one file the whole update");
        script.AppendLine("    # rests on, so compare it against the archive instead of trusting the copy. The");
        script.AppendLine("    # previous version skipped this and relaunched a stale build reporting a stale");
        script.AppendLine("    # version, commit and build date.");
        script.AppendLine("    $sourceEntry = Join-Path $kronosProcessId 'Kronos.exe'");
        script.AppendLine("    if ((Test-Path $sourceEntry) -and (Test-Path $entry)) {");
        script.AppendLine("        if ((Get-FileHash -LiteralPath $sourceEntry -Algorithm SHA256).Hash -eq");
        script.AppendLine("            (Get-FileHash -LiteralPath $entry -Algorithm SHA256).Hash) { $updated = $true }");
        script.AppendLine("    }");
        script.AppendLine();
        script.AppendLine("    if ($failed.Count -gt 0) {");
        script.AppendLine("        $log = Join-Path $target 'update-errors.log'");
        // Kept on one line deliberately. A pipe at the start of a continuation line is a parse error in
        // Windows PowerShell 5.1, and the script is run by powershell.exe, which is 5.1 on Windows 10 and
        // 11. Every assertion on this file read the script as text, so nothing would have caught that.
        script.AppendLine("        ('{0} file(s) could not be replaced; the rest of the update was applied.' -f $failed.Count) | Out-File -LiteralPath $log -Encoding utf8");
        script.AppendLine("        $failed | Out-File -LiteralPath $log -Encoding utf8 -Append");
        script.AppendLine("    }");
        script.AppendLine("} catch {");
        script.AppendLine("    if ($copied) { Copy-Item -LiteralPath $backup -Destination $entry -Force }");
        script.AppendLine("}");
        script.AppendLine();
        script.AppendLine("if (-not $updated) {");
        script.AppendLine("    # Restoring the old executable is what makes this a failed update rather than a");
        script.AppendLine("    # broken installation, and the backup is removed either way. Leaving it behind put a");
        script.AppendLine("    # stale Kronos.exe.updatebak in every installation that hit this.");
        script.AppendLine("    if (Test-Path $backup) {");
        script.AppendLine("        Copy-Item -LiteralPath $backup -Destination $entry -Force");
        script.AppendLine("        Remove-Item -LiteralPath $backup -Force");
        script.AppendLine("    }");
        script.AppendLine("    exit 1");
        script.AppendLine("}");
        script.AppendLine();
        script.AppendLine("Remove-Item -LiteralPath $backup -Force");
        script.AppendLine();

        if (relaunch)
        {
            script.AppendLine("Start-Process -FilePath $entry");
        }

        return script.ToString();
    }

    /// <summary>Quotes a value for PowerShell, where a single quote is escaped by doubling it.</summary>
    internal static string Quote(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    /// <summary>
    /// Runs the handoff: extracts the archive, writes the script, starts it detached, and returns.
    /// The caller is responsible for exiting afterwards.
    /// </summary>
    /// <returns>A message describing what happened, for the user.</returns>
    public static string PrepareHandoff(string archivePath, string targetDirectory, bool relaunch)
    {
        var stagingRoot = Path.Combine(Path.GetTempPath(), "KronosUpdate-" + Guid.NewGuid().ToString("N"));
        var extracted = Path.Combine(stagingRoot, "extracted");
        Directory.CreateDirectory(extracted);

        try
        {
            ZipFile.ExtractToDirectory(archivePath, extracted, true);

            // The archive holds Kronos.exe at its root, but locate it rather than assuming: if the
            // layout ever changes, copying a tree that contains no executable is worse than failing.
            var entryPoint = Directory
                .EnumerateFiles(extracted, EntryPointFileName, SearchOption.AllDirectories)
                .FirstOrDefault();

            if (entryPoint is null)
            {
                throw new FileNotFoundException(
                    $"{EntryPointFileName} was not found in the downloaded update.", archivePath);
            }

            // Copy from the directory containing it, so the payload sits at the archive root.
            var source = Path.GetDirectoryName(entryPoint)!;

            var scriptPath = Path.Combine(stagingRoot, "apply-update.ps1");
            File.WriteAllText(
                scriptPath,
                BuildHandoffScript(source, targetDirectory, Environment.ProcessId, relaunch),
                new UTF8Encoding(false));

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-WindowStyle");
            startInfo.ArgumentList.Add("Hidden");
            // The script is generated, so it cannot be signed. Without this it would not run at all
            // under the default execution policy.
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start the update handoff process.");

            return "Kronos will close and reopen to finish updating.";
        }
        catch
        {
            TryDelete(stagingRoot);
            throw;
        }
    }

    static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing an update over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
