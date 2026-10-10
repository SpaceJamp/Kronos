# Release notes

Notes for significant releases only. Routine fixes and refactors live in the commit history and are
not written up here — this file is for changes big enough that someone upgrading should read about
them before they do.

The current version is in [`src/Kronos.csproj`](src/Kronos.csproj). There is no release automation;
builds are manual and unsigned, so there is no published artifact to attach.

---

## Unreleased

### Games page rendered empty on every build

The Games page showed no games at all, on every platform and every configuration, while the database
held them correctly — Steam, GOG and the rest were being discovered and stored the whole time.

**Cause.** A refactor for Linux support rebuilt the collection views with `Source = _allGames`, a
list of `Game` objects, while leaving `IsSourceGrouped = true`. WinUI then tried to group individual
games by a group-name property that `Game` does not have, and produced nothing. Separately, each
library's view was constructed over a throwaway `ObservableCollection<Game>()` instead of the shared
collection, so even the ungrouped path lost every game.

**Fix.** Restored the original design: a list of `GameGroup` objects, each group's `Games` being an
`AdvancedCollectionView` filtered over the shared collection, plus the `GameLibrariesOrderChangedMessage`
handler the refactor had deleted — library reordering in Settings had been silently dead for the same
period.

### "Reset to default" never worked

Swapping in a DLL was fine; putting it back was not. Every reset returned *"Unable to reset to
default. Please repair your game manually."*

**Cause.** Backups had been changed from a single `.dlsss` file to a numbered chain
(`.kronosbak1`, `.kronosbak2`, …) so that swaps are undoable more than once. The write paths were
updated; the read paths were not. Reset looked for the literal string `".dlsss"` in a path that does
not contain it, matched zero primary records, and gave up.

The same omission meant a rescan found no backups, dropped those records from memory and deleted
them from the database — leaving the files on disk untracked. That made reset permanently impossible
and made each subsequent swap append yet another chain entry, without bound.

**Fix.** Added `DllBackupStack.GetPrimaryPathFromBackup`, which maps a backup path back to its DLL
for both the chain and the legacy suffix, and used it in reset. `LoadBackupForGameAsset` now tracks
every entry in the chain rather than looking for a single legacy file, so a rescan no longer discards
backups.

### One unreadable registry key could delete game data

**Cause.** The Ubisoft Connect library used `break` where `continue` was meant. A single
permission-denied install subkey abandoned the rest of the enumeration — and the cleanup pass that
follows deletes cached games it did not rediscover, taking their assets, history and notes with it.

**Fix.** `continue`, with the subkey logged. GOG had a related problem: `continue` on a cover-art
failure skipped the calls that save and process the game, so a missing or corrupt `webcache.zip`
made an installed, working game never appear at all. Cover handling is now a separate method that
cannot skip the game.

### Thread-safety holes in the game scanner

Five sites passed the live game-assets list to sqlite-net, or to a `Where`/`Select`, from a thread
pool thread. `ProcessGame` runs up to four scans concurrently and clears that list to replace it, so
any interleaving threw `InvalidOperationException: Collection was modified` — which a catch-all
converted into "this game has no DLLs", permanently.

**Fix.** All five now use the snapshot helper the class comment already required.

### A single failed cover fetch broke cover art library-wide

The in-progress flag guarding cover loading was cleared by a trailing statement rather than a
`finally`. One network failure or truncated image left the flag stuck, and every later call returned
immediately.

### Signature verification now says what it actually checked

**Cause.** `WinTrust.VerifyEmbeddedSignature` returned `bool`, and on Linux returned `true` — which
reads as "verified" when nothing was checked. Authenticode is a Microsoft PE certificate format with
no Linux implementation, so there is nothing to check there.

**Fix.** The result is now `Valid` / `Invalid` / `Unavailable`, and only `Invalid` blocks. Where a
file is accepted without being checked — importing a DLL that is not in the signed manifest, so has
no expected hash and no signature gate either — the import summary shows an orange warning banner and
the dialog title states how many files were not verified. Windows never returns `Unavailable`, so the
banner never appears there.

Worth being precise: **the download and swap path was already hash-verified on both platforms**
(`ZipMD5Hash` on download, `MD5Hash` before the swap). That was never broken. This change is about
the import paths and about not implying a check happened when it did not.

### Dependency advisories cleared

`dotnet list package --vulnerable` reports **no vulnerable packages** for either project on either
target framework.

| Package | Was | Now | Advisory |
|---|---|---|---|
| SixLabors.ImageSharp | 3.1.5 → 4.0.0 | **4.1.2** | CVE-2026-106115 — out-of-bounds write in the TIFF CCITT T6 encoder — plus six others |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.8 | **3.0.5** | CVE-2025-6965 — memory corruption. Every 2.x release is affected with no patched 2.x, so 3.x was the only option. This also aligns the bundle with the SQLitePCLRaw 3.0.2 that `sqlite-net-e` already depended on. |

ImageSharp 3.2 and later require a licence. Kronos builds against a **Community Licence**, supplied
at build time via `IMAGESHARP_LICENSE_KEY` and never committed.

### Linux core restored to the build

The Linux target had stopped compiling the game model. Nine files were removed from the `net10.0`
compile list during the original port without touching their cross-platform branches, so those
branches had never been compiled and the Linux CLI shared no code with the Windows app — no database,
no manifest, no DLL management.

They are back. Getting them to compile meant undoing three couplings that were wrong on Windows too:

- **`App.CurrentApp.RunOnUIThread` called from the game model.** Marshalling bound-property updates is
  a UI concern, so `Game.cs` reaching into a WinUI `Application` is what put the file out of Linux
  reach. Now goes through `UiDispatcher.Invoke`, which forwards to the WinUI dispatcher on Windows and
  runs inline on Linux. Windows behaviour is unchanged.
- **The shared `HttpClient` lived on `App`**, reachable only as `App.CurrentApp.HttpClient`. The file
  downloader and the Steam cover URL resolver — both needed on Linux — could not compile there. Now a
  standalone `Http` holder that `App` delegates to.
- **`ResourceHelper` was built entirely on WinUI's `ResourceManager`.** On Linux it now parses the
  `.resw` XML directly, which is the same data WinUI compiles into a PRI, so the CLI is localised
  rather than printing resource keys.

Three things were needlessly Windows-only with no Windows types in them: `DLLAssetTypes` (a pure data
table); `IsInKnownGameAsset` (which existed twice behind `#if WINDOWS` with *different signatures* —
that mismatch was the sole reason its only two call sites could not compile); and
`Game.ResizeCoverAsync` / `AddCustomCover` (plain ImageSharp work).

**The Linux CLI still has no DLL commands**, and the Avalonia GUI still does not build. See
[Platform support](README.md#platform-support).