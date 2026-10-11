v1.59 — Kronos now tells you when something went wrong

Two changes with the same theme: **problems Kronos used to swallow now say so.**

## If an update didn't finish, you'll be told which files

Since 1.55, an update that couldn't replace every file has written a log naming the ones it couldn't. That was supposed to stop a partial update being silent.

**It was still silent.** Nothing ever read that log. It was written to be seen, and no one saw it.

Now, on the next launch, Kronos says how many files were left alone, names them, and offers to open the file. The usual cause is something holding a file open — closing Kronos fully and updating again usually clears it.

The log is not deleted when you dismiss it. If you're reporting the problem, you'll want it, and the next update overwrites it anyway.

## When Kronos crashes, it now explains itself

It used to log the exception and close. Which means: the app vanished, you had no idea whether anything was saved, and the log it had just written was somewhere you'd never think to look.

**Kronos now keeps running and shows you what happened.** It explains what failed, tells you that anything already saved is safe, gives you the full error to copy, and offers a button to open the log folder.

### Why the app stays open

This is the significant change, so it's worth being blunt about the trade-off: the operation that was running is stopped, but the window survives.

Losing an operation mid-way is recoverable. Losing the app means losing your window and re-scanning your whole library on the way back in. A crash that leaves you with a usable app and a log beats a clean exit that shows nothing.

If you'd rather it exited, that's a reasonable preference — say so and I'll change it.

### Three things it won't report

- **Cancelling something.** Closing a dialog mid-download isn't a crash, and telling you it was would be worse than silence.
- **Running out of memory.** Logged, but by then the process is usually in no state to build a window.
- **The log path itself**, if Kronos is installed somewhere unwritable. The buttons are simply inert rather than pointing somewhere that doesn't exist.

## Testing

534 tests, up from 516.

One test here exists purely to stop a repeat of something that cost me real time: **the XAML compiler reports a translation property that's bound but never declared as an opaque internal error, with no file or line.** The new tests check both directions — every key the windows use exists in the resources, and every property the XAML binds to is actually declared.

## Downloading

`Kronos-1.59.0-portable.zip` is attached. Unzip anywhere and run `Kronos.exe`.

**It needs the [Windows App Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).** If `Kronos.exe` exits without a window, install it.

The build is **unsigned**, so SmartScreen will warn. GitHub publishes a SHA256 beside every asset and Kronos verifies it before installing anything.