v1.56 — three ways Kronos could lose your data

An audit of the code around settings, backups and the swap path found three bugs that destroy data. The first one can delete the only remaining copy of a game's original DLL.

**If you have a game whose backup was already destroyed, the notes at the bottom tell you what is left. Nothing here can bring a deleted file back.**

## 1. A swap that failed partway could delete your game's original DLL

**This is the serious one.**

A game's DLLs are overwritten one at a time, and the database was updated **after** all of them. The database is the only thing that records "Kronos wrote this file" as opposed to "something else did".

So when a swap failed partway through — a second copy of a DLL in a folder you don't have permission to write, a locked database, the app closed mid-swap — you were left with:

- the game's DLL **already overwritten**
- its backup file on disk
- the database still describing the **original** version

On the next launch, Kronos compared the DLL on disk against the version it had recorded, concluded the file had been changed by something else, and did what it does to files it did not write: **deleted the backup chain**.

That backup was the only surviving copy of the runtime your game shipped with. After that, "Reset" could only say *"Unable to reset to default. Please repair your game manually."*

**What changed:** the backup records are now written to the database *before* any game file is touched. An interrupted swap now leaves records that match what is on disk, so the chain is recognised as Kronos's and kept.

## 2. A failed settings save reset every setting you had

Settings were saved by opening `settings.json` and writing over it — which truncates the file to zero bytes *before* anything is written. Anything that interrupted that (full disk, antivirus lock, closing the app) left a 0-byte file.

On the next launch that file could not be parsed, Kronos treated it as a first run, and **wrote the defaults over it**. No warning, no prompt.

Lost silently: theme, language, *Allow untrusted DLLs*, logging level, grid width, ignored paths, window position.

Settings save on every change, so there were many chances to hit this.

**What changed:** settings are written to a temporary file and moved into place, so the real file is either the old one or the new one and never half-written. A settings file that cannot be parsed is now **kept**, alongside a copy, instead of being replaced by defaults.

## 3. "Reset" could report success while restoring nothing

Restore only matched the newer `.kronosbakN` backup chain. For a game carried over from an older Kronos — which has only the legacy `.dlsss` backup — every backup was skipped, nothing was restored, and the method returned **success**. The dialog closed as though the DLL had been put back while the swapped file was still installed.

**What changed:** a restore that matched nothing reports failure instead of success.

## Also

- **Imported DLLs no longer risk losing their manifest.** The same truncate-then-write bug meant a failed save left a 0-byte manifest, which Kronos read as "importing is disabled" — every custom DLL you had imported became untracked.
- **The games grid no longer races your settings.** Changing the grid width with Ctrl+scroll saved settings from a background thread, and a concurrent save from anywhere else silently lost one of them. Saving is now on the UI thread, and the pending save is discarded when you navigate away rather than firing during shutdown.

## Dead code removed

- `GameAsset.GetBackup()` — only understood the legacy `.dlsss`, so it would have found no backup for anything swapped by a current version
- `LocalRecord.UpdateFromNewLocalRecord` — copied `IsDownloaded` without checking the file existed
- `SwapVersionAdvisor.GetShippedVersion`, `VersionExtensions.DescribeVersion`, `DLLManager.HasLoadedImportedManifest`
- `FileStreamExtensions.GetSha256Hash` — **kept.** It looked unused; it is what verifies the downloaded update against GitHub's published digest. Nearly removed; the compiler caught it.

## Testing

475 tests, up from 470. `Storage` had **no** tests at all before this, which is how settings loss went unnoticed.

The new settings tests were checked against the old code and do fail on it, so they are guarding the fix rather than merely passing alongside it.

## If your backup was already destroyed

Kronos cannot restore a file it deleted. What is usually left:

1. **Other launcher versions.** Steam, Epic and Ubisoft keep older builds under ` depot` / `InstalledDepot` folders. The original DLL is almost always in one of those.
2. **Verify the game files** in its launcher. That restores the shipped DLL without touching your save data.
3. **Re-download** from Steam if the above fails.

Do this before swapping anything else in that game.

## Downloading

`Kronos-1.56.0-portable.zip` is attached. Unzip anywhere and run `Kronos.exe`.

**It needs the [Windows App Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).** If `Kronos.exe` exits without a window, install it.

The build is **unsigned**, so SmartScreen will warn. GitHub publishes a SHA256 beside every asset and Kronos verifies it before installing anything.