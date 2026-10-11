v1.57 — the rest of the audit

v1.56 fixed the three that destroyed data. These are the remainder: two that could corrupt a game's files, one that could empty your DLL list, and several where a small failure took the whole app down without saying why.

**If you are on 1.56, this is a normal upgrade.** No recovery steps are needed.

## Your game's DLL is no longer written in place

Swapping and restoring both copied the new file **straight onto the live DLL** in the game folder. That truncates the destination to zero and *then* writes, so a process kill, a full disk, or antivirus stepping in partway through left a **truncated `nvngx_dlss.dll`** sitting in your install directory.

That is exactly the state the backup chain exists to prevent — and nothing in the app would have told you. The game would simply stop launching, with the file sitting there looking superficially fine.

DLLs are now written to a temporary file beside the original and moved into place. The rename is atomic, so the file is either entirely the old one or entirely the new one, never half of each. **If anything fails, your game's own DLL is left untouched.**

## A game can no longer lose all of its tracked DLLs

Every place that rewrites a game's records did it as two separate statements: delete its rows, then insert the new ones. Anything that went wrong in between — a crash, a killed process, a locked database — left the game with **no tracked DLLs at all**.

That is the worst possible state: the game looks like it has nothing swappable, and the backup chain the reset button depends on has nothing to reference. It recovers on the next scan, if that scan succeeds.

These rewrites now happen inside a single transaction. Either all the rows are replaced or none are.

## A stray folder could empty your entire DLL list

While migrating an old DLL layout, Kronos checked whether a now-empty folder could be tidied away. That check sat **between two `try` blocks**, so if it threw — the folder removed by you, a sync client, antivirus, or a drive disappearing — nothing caught it.

It escaped all the way out, and the result was:

- the DLL list rendered **empty**
- the "Migrating DLLs" message never cleared
- the app then **terminated**, because the crash handler logs but doesn't handle

Tidying a folder you've finished with cannot now abort loading your whole DLL library.

## Failures that killed startup now fail quietly

**If Kronos couldn't write its log, it would not start at all.** Both the log folder and the log file are inside the install directory for a portable build — so unpacking it somewhere you can't write (Program Files, a read-only share) meant the app died inside its own constructor: no window, no error, no log explaining why. The log is now optional.

**The Windows version backstop didn't work.** If it ever fired, it returned from the constructor without building the window — and the app showed that empty frame anyway, then crashed on a null view model. It now refuses to build and shows you the proper "unsupported Windows" window.

## Smaller fixes

- **Signature checks leaked a native handle per file.** Verifying a DLL's signature allocates a native block that only a second call releases, and that release sat at the end of a `try` — so any failure leaked it. A mass update verifies many files.
- **A folder-cleanup failure could leave a deleted DLL marked as still downloaded.**
- **The unknown-assets list was cleared without the lock every other access takes**, so a running scan could race it.
- **A settings field is read but can no longer be written** — left alone, as nothing writes it and the migration that reads it is correct.

## One thing I want to flag

The audit reported that two `DROP INDEX` statements in the database migration were dead code that could never match. **They are not.** I had someone verify it against the actual package rather than take it on trust: `sqlite-net-e` names indexes `{table}_{column}`, exactly as those statements assume, and dropping the stale PascalCase index after a table rename is what lets the snake_case one be recreated. Working code, left alone.

## Testing

491 tests, up from 475. 16 new.

- Six run the atomic file replacement for real, including a forced failure that asserts your DLL is untouched.
- Ten assert on the specific fixes above.

Three consecutive full runs, all green.

## Downloading

`Kronos-1.57.0-portable.zip` is attached. Unzip anywhere and run `Kronos.exe`.

**It needs the [Windows App Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).** If `Kronos.exe` exits without a window, install it.

The build is **unsigned**, so SmartScreen will warn. GitHub publishes a SHA256 beside every asset and Kronos verifies it before installing anything.