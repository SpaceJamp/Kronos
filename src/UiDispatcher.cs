using System;
using System.Threading.Tasks;

namespace Kronos;

/// <summary>
/// Marshals work onto the UI thread, on both targets.
/// </summary>
/// <remarks>
/// The game model, DLL manager and record types all need to update bound properties from
/// background threads, and on Windows that has to go through WinUI's dispatcher. Those files
/// previously called <c>App.CurrentApp.RunOnUIThread</c> directly, which put a hard dependency
/// on the WinUI <c>Application</c> into code that the Linux target also compiles - so the whole
/// game model was excluded from the Linux build rather than being decoupled from the UI.
///
/// On Linux there is no UI thread, so these run inline on the calling thread. That is correct
/// for the CLI, which has no bindings to update, and it keeps the call sites reading the same on
/// both targets instead of being scattered with <c>#if</c> around every marshalling call.
/// </remarks>
internal static class UiDispatcher
{
#if WINDOWS
    /// <summary>Runs <paramref name="action"/> on the UI thread.</summary>
    internal static void Invoke(Action action) => App.CurrentApp.RunOnUIThread(action);

    /// <summary>Runs <paramref name="function"/> on the UI thread and awaits it.</summary>
    internal static Task InvokeAsync(Func<Task> function) => App.CurrentApp.RunOnUIThreadAsync(function);

    /// <summary>Whether the process is elevated, which decides whether protected paths are writable.</summary>
    internal static bool IsAdministrator => App.CurrentApp.IsAdminUser();
#else
    internal static void Invoke(Action action) => action();

    internal static Task InvokeAsync(Func<Task> function) => function();

    /// <summary>
    /// Always false. Windows UAC has no Linux equivalent, so the "run as administrator"
    /// fallbacks never trigger there - a permission error is reported as-is instead.
    /// </summary>
    internal static bool IsAdministrator => false;
#endif
}