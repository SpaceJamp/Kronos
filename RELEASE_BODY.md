v1.55 — the update that did not update

**If you used "Check for updates" on 1.53 or 1.54, your update did not fully apply, and your game library and settings were overwritten with an empty database.** Read this before anything else.

## What was wrong

The portable update could not fail loudly. It failed quietly, and it did damage on the way.

**It stopped at the first file it could not write.** The script that replaces the app ran with PowerShell's `Stop` error mode, so a single locked file — which is what a running database, or a file a virus scanner is holding, always looks like — turned the rest of the update into an error. It copied what it could, hit the locked file, put the old executable back, and gave up. The app then reopened on the **previous build**, reporting the **previous version, commit and build date**. Nothing on screen said the update had not worked.

**It overwrote your data.** The update copied the entire archive over your installation, and the archive contains a `StoredData` folder: the blank database and default settings created by the build's own startup test. `StoredData` is where Kronos keeps your game library and your settings — in the same folder. Every successful update replaced them with empty ones.

That is the file most likely to be locked in the first place, so the two faults fed each other.

**It never checked its own work.** The executable was backed up and then trusted. The script reported success on the strength of having attempted the copy.

## What it does now

- A file that will not write is recorded and the update **continues**. One locked DLL no longer cancels everything.
- The executable is **compared against the archive by hash** before the update is called a success. If it does not match, the old one is restored, the rollback copy is deleted, and the update reports failure.
- **`StoredData` is never touched.** Your library, your settings, your cached covers and logs all survive.
- Anything that genuinely could not be written is listed in **`update-errors.log`** beside `Kronos.exe`, so a partial update is visible instead of silent.
- The rollback copy is removed on both paths. Failed updates no longer leave a `Kronos.exe.updatebak` in your installation.

## Recovering your data

If you ran an update on 1.53 or 1.54, your `StoredData` may have been reset. Kronos can rebuild most of it:

1. Start Kronos. It will show an empty library.
2. **Settings → Check for updates** to fetch the current library, then add your games back — the installer paths are still on disk and can usually be detected again.
3. **Settings** will be back at their defaults. Re-apply your preferences.

Your games themselves are untouched. Only Kronos's record of them was replaced.

## Also fixed

- **The version in About now links to your actual release.** The build tag was never passed to the compiler, so the link always went to the releases index rather than the release you installed. The commit and branch are now stamped into the build too, and the "build commit" line appears on development builds rather than release ones.
- **The release script no longer reports success with a failure code.** `git push` writes progress to stderr, which PowerShell treats as a terminating error; the 1.54 release completed correctly and the script still exited 1. Anything watching that exit code would have called a successful release a failure.

## Testing

469 tests, up from 455.

Every test on the update script read it as text and asserted it said the right things — which is how a script that copied the user's database, and one that PowerShell could not even parse, sat there looking fine. **Three tests now run the script** against a fake installation and a locked file. That is what caught the parse error, on the first run.

## Downloading

`Kronos-1.55.0-portable.zip` is attached. Unzip anywhere and run `Kronos.exe`.

**It needs the [Windows App Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).** If `Kronos.exe` exits without a window, install it.

The build is **unsigned**, so SmartScreen will warn. GitHub publishes a SHA256 beside every asset and Kronos verifies it before installing anything.