using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards the keyboard shortcuts.
/// </summary>
/// <remarks>
/// The app had exactly one, Escape on the game flyout. A Refresh and a Filter sit in the toolbar with no
/// shortcuts, which is what a browser user reaches for constantly.
///
/// These are placement tests, not behaviour tests: a shortcut is a binding, and the only way it can be
/// wrong is by being attached to the wrong element or the wrong handler. That is checked here because
/// there is nothing to execute - the page needs a loaded window.
/// </remarks>
public class KeyboardShortcutTests
{
    static string Source(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kronos.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray()));
    }

    static string GamesPage => Source("src", "Pages", "GameGridPage.xaml");
    static string GamesPageCode => Source("src", "Pages", "GameGridPage.xaml.cs");
    static string MainWindowCode => Source("src", "MainWindow.xaml.cs");

    // ------------------------------------------------------------------ Ctrl+R

    [Fact]
    public void CtrlROnTheRefreshButtonRunsTheRefreshCommand()
    {
        // On the button, not on the page. That way the shortcut runs the same command the button does and
        // inherits the button's enabled state - there is no second route to the command that can disagree
        // with the button about whether a refresh is allowed.
        var element = ButtonBoundTo(GamesPage, "ViewModel.RefreshGamesButtonCommand");

        Assert.Contains("KeyboardAccelerators", element, StringComparison.Ordinal);
        Assert.Contains("<KeyboardAccelerator Key=\"R\" Modifiers=\"Control\"", element, StringComparison.Ordinal);
    }

    [Fact]
    public void CtrlRIsBoundToNoCommandOfItsOwn()
    {
        // If the accelerator also declared a Command, that command would run alongside the button's - and
        // the two guards would disagree about whether a second refresh is a no-op.
        var element = ButtonBoundTo(GamesPage, "ViewModel.RefreshGamesButtonCommand");

        var acceleratorAt = element.IndexOf("KeyboardAccelerators", StringComparison.Ordinal);
        Assert.True(acceleratorAt > 0);

        var accelerator = element.Substring(acceleratorAt);

        Assert.DoesNotContain("Command=", accelerator, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole XAML element that binds a given command, from its opening tag to its closing tag.
    /// </summary>
    /// <remarks>
    /// A fixed-size window around the binding is not enough here: the comments explaining why the
    /// accelerator is attached to the button are longer than the markup between the command and the key, so
    /// a window wide enough to reach the key also reached text belonging to the previous button. Anchoring
    /// on the element is both sufficient and what the test is actually asserting.
    /// </remarks>
    static string ButtonBoundTo(string xaml, string command)
    {
        var commandAt = xaml.IndexOf(command, StringComparison.Ordinal);
        Assert.True(commandAt > 0, $"expected an element bound to {command}");

        var openAt = xaml.LastIndexOf("<AppBarButton ", commandAt, StringComparison.Ordinal);
        Assert.True(openAt > 0, "the command should be bound on an AppBarButton");

        var closeAt = xaml.IndexOf("</AppBarButton>", commandAt, StringComparison.Ordinal);
        Assert.True(closeAt > 0, "the AppBarButton should be closed rather than self-closing");

        return xaml.Substring(openAt, closeAt - openAt);
    }

    // ------------------------------------------------------------------ Ctrl+F

    [Fact]
    public void CtrlFIsOnThePageRatherThanOnTheSearchBox()
    {
        // The search box is not a button running a command - it is a control that has to take focus.
        // Attached to the TextBox, the accelerator would be shadowed by the very field it jumps to, since
        // a focused TextBox consumes character input before an accelerator on it is reached.
        var xaml = GamesPage;

        Assert.Contains("<Page.KeyboardAccelerators>", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"F\" Modifiers=\"Control\"", xaml, StringComparison.Ordinal);

        var searchBoxAt = xaml.IndexOf("x:Name=\"SearchBox\"", StringComparison.Ordinal);
        Assert.True(searchBoxAt > 0, "the search box should still be named, or nothing can focus it");
    }

    [Fact]
    public void CtrlFMarksTheEventHandledSoTheKeypressCannotFireTwice()
    {
        var code = GamesPageCode;

        var handlerAt = code.IndexOf("void SearchAccelerator_Invoked", StringComparison.Ordinal);
        Assert.True(handlerAt > 0, "the Ctrl+F accelerator should have a handler");

        var body = code.Substring(handlerAt, Math.Min(700, code.Length - handlerAt));

        Assert.Contains("args.Handled = true;", body, StringComparison.Ordinal);
        Assert.Contains("SearchBox.Focus(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void CtrlFDoesNothingWhileTheSearchBoxIsDisabled()
    {
        // The box is disabled while the library is loading, and focusing a disabled control does nothing -
        // so without the check the shortcut would appear broken rather than unavailable.
        var code = GamesPageCode;

        var handlerAt = code.IndexOf("void SearchAccelerator_Invoked", StringComparison.Ordinal);
        var body = code.Substring(handlerAt, Math.Min(900, code.Length - handlerAt));

        var guardAt = body.IndexOf("IsEnabled == false", StringComparison.Ordinal);
        var focusAt = body.IndexOf("SearchBox.Focus(", StringComparison.Ordinal);

        Assert.True(guardAt > 0, "the handler should check whether the box can take focus");
        Assert.True(focusAt > guardAt, "and return before focusing it if it cannot");
    }

    // ------------------------------------------------------------------ Ctrl+,

    [Fact]
    public void CtrlCommaIsRegisteredOnTheRootGridSoItWorksFromEveryPage()
    {
        // Navigation is the window's business. On the games toolbar it would do nothing on the settings
        // page, which is exactly where someone who has lost their shortcut would go to look for it.
        var code = MainWindowCode;

        var registerAt = code.IndexOf("void RegisterSettingsAccelerator", StringComparison.Ordinal);
        Assert.True(registerAt > 0, "the settings shortcut should be registered in code");

        var body = code.Substring(registerAt, Math.Min(800, code.Length - registerAt));

        Assert.Contains("RootGrid.KeyboardAccelerators.Add", body, StringComparison.Ordinal);
        Assert.Contains("SettingsAccelerator_Invoked", body, StringComparison.Ordinal);
    }

    [Fact]
    public void CtrlCommaNavigatesToSettingsAndMarksTheEventHandled()
    {
        var code = MainWindowCode;

        var handlerAt = code.IndexOf("void SettingsAccelerator_Invoked", StringComparison.Ordinal);
        Assert.True(handlerAt > 0);

        var body = code.Substring(handlerAt, Math.Min(700, code.Length - handlerAt));

        Assert.Contains("args.Handled = true;", body, StringComparison.Ordinal);
        Assert.Contains("GoToPage(SettingsPage.PageTag)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void CtrlCommaIsThePhysicalCommaKeyNotACharacter()
    {
        // The Win32 virtual-key code, because there is no VirtualKey member for it: this SDK's projection
        // has no Oem-prefixed names, so both VirtualKey.OemComma and Key="OemComma" in XAML fail to compile
        // (WMC0055 for the latter). The numeric value is stable and is what every Windows app means.
        //
        // Naming the constant, rather than writing 0xBE inline, is what makes that legible.
        var code = MainWindowCode;

        Assert.Contains("const VirtualKey CommaKey = (VirtualKey)0xBE;", code, StringComparison.Ordinal);

        var registerAt = code.IndexOf("void RegisterSettingsAccelerator", StringComparison.Ordinal);
        var body = code.Substring(registerAt, Math.Min(800, code.Length - registerAt));

        Assert.Contains("Key = CommaKey", body, StringComparison.Ordinal);
        Assert.Contains("Modifiers = VirtualKeyModifiers.Control", body, StringComparison.Ordinal);
        Assert.DoesNotContain("(VirtualKey)0xBE", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSettingsAcceleratorIsNotAlsoDeclaredInXaml()
    {
        // It cannot be - the markup spelling does not compile - but declaring it there as well would mean
        // two shortcuts for one keypress if that ever changed, and only one of them would be findable here.
        var xaml = Source("src", "MainWindow.xaml");

        Assert.DoesNotContain("OemComma", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyboardAccelerator", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryAcceleratorNamesAModifier()
    {
        // A bare Key with no modifier fires on that key alone - so Ctrl+F declared without Control would
        // make typing "f" in the notes field jump to the search box.
        var declared = EnumerateDeclaredAccelerators(GamesPage)
            .Concat(EnumerateDeclaredAccelerators(MainWindowCode))
            .ToList();

        // The two page-level ones. The window's is registered in code and checked separately.
        Assert.Equal(2, declared.Count);

        foreach (var line in declared)
        {
            Assert.Contains("Modifiers=\"Control\"", line, StringComparison.Ordinal);
        }
    }

    /// <summary>The <c>&lt;KeyboardAccelerator ... /&gt;</c> declarations in a file, one per line.</summary>
    static System.Collections.Generic.IEnumerable<string> EnumerateDeclaredAccelerators(string source)
    {
        return source
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("<KeyboardAccelerator ", StringComparison.Ordinal));
    }
}
